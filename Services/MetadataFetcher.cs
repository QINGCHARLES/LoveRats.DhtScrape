namespace DhtScraper.Services;

/// <summary>Fetches torrent metadata via BEP 009 extension protocol.</summary>
/// <remarks>
/// Consumes info_hash values from the channel and uses MonoTorrent
/// to download only the metadata (file names, sizes) from the swarm.
/// </remarks>
public sealed class MetadataFetcher(
	ChannelReader<string> HashChannelReader,
	IServiceScopeFactory ScopeFactory,
	ILogger<MetadataFetcher> Logger) : BackgroundService
{
	private const int TimeoutSeconds = 45;
	private const int MaxConcurrentFetches = 50;
	private const int TcpListenPort = 55555;
	private static readonly string MetadataSavePath = Path.Combine(AppContext.BaseDirectory, "Downloads_Metadata");

	private readonly HashSet<string> ProcessedHashes = [];
	private ClientEngine? Engine;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		Logger.LogInformation("Starting Metadata Fetcher on TCP port {Port}...", TcpListenPort);

		EngineSettingsBuilder SettingsBuilder = new()
		{
			AllowedEncryption = [EncryptionType.PlainText, EncryptionType.RC4Full, EncryptionType.RC4Header],
			ListenEndPoints = new Dictionary<string, IPEndPoint>
			{
				[string.Empty] = new IPEndPoint(IPAddress.Any, TcpListenPort)
			},
			DhtEndPoint = null // Disable engine's internal DHT - we use our own crawler
		};

		Engine = new ClientEngine(SettingsBuilder.ToSettings());
		await Engine.StartAllAsync();

		SemaphoreSlim Semaphore = new(MaxConcurrentFetches);

		await foreach (string HashHex in HashChannelReader.ReadAllAsync(CancellationToken))
		{
			if (ProcessedHashes.Contains(HashHex))
			{
				continue;
			}

			ProcessedHashes.Add(HashHex);

			await Semaphore.WaitAsync(CancellationToken);
			_ = ProcessHashAsync(HashHex, Semaphore, CancellationToken);
		}
	}

	private async Task ProcessHashAsync(string HashHex, SemaphoreSlim Semaphore, CancellationToken CancellationToken)
	{
		try
		{
			using IServiceScope Scope = ScopeFactory.CreateScope();
			TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();

			// Check if already indexed
			bool Exists = await Db.Torrents.AnyAsync(T => T.InfoHash == HashHex, CancellationToken);
			if (Exists)
			{
				return;
			}

			// Parse hex string to InfoHash
			if (HashHex.Length != 40)
			{
				return;
			}

			byte[] HashBytes = Convert.FromHexString(HashHex);
			InfoHash ParsedHash = new(HashBytes);

			MagnetLink Magnet = new(ParsedHash);
			TorrentManager Manager = await Engine!.AddAsync(Magnet, MetadataSavePath);

			try
			{
				TaskCompletionSource<bool> MetadataReceived = new();

				using CancellationTokenSource TimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
				TimeoutCts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

				// Poll for metadata instead of using event (API changed in v3)
				await Manager.StartAsync();

				while (!TimeoutCts.Token.IsCancellationRequested)
				{
					if (Manager.HasMetadata)
					{
						await SaveToDatabaseAsync(Db, Manager, HashHex, CancellationToken);
						Logger.LogInformation("[INDEXED] {Name}", Manager.Torrent?.Name ?? HashHex);
						break;
					}

					await Task.Delay(500, TimeoutCts.Token);
				}
			}
			finally
			{
				await Manager.StopAsync();
				await Engine.RemoveAsync(Manager);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown
		}
		catch (Exception Ex)
		{
			Logger.LogDebug("Failed to fetch {Hash}: {Message}", HashHex, Ex.Message);
		}
		finally
		{
			Semaphore.Release();
		}
	}

	private static async Task SaveToDatabaseAsync(
		TorrentContext Db,
		TorrentManager Manager,
		string HashHex,
		CancellationToken CancellationToken)
	{
		if (Manager.Torrent is null)
		{
			return;
		}

		TorrentInfo TorrentInfo = new()
		{
			InfoHash = HashHex,
			Name = Manager.Torrent.Name,
			TotalSizeBytes = Manager.Torrent.Size,
			Files = [.. Manager.Torrent.Files.Select(F => new Data.TorrentFile
			{
				Path = F.Path,
				SizeBytes = F.Length
			})]
		};

		Db.Torrents.Add(TorrentInfo);
		await Db.SaveChangesAsync(CancellationToken);
	}
}
