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
	private const int TimeoutSeconds = 15; // Reduced from 30 - fail fast
	private const int MaxConcurrentFetches = 25; // Increased from 10
	private const int TcpListenPort = 55555;
	private const int StatsIntervalSeconds = 10;
	private static readonly string MetadataSavePath = Path.Combine(AppContext.BaseDirectory, "Downloads_Metadata");

	private readonly HashSet<string> ProcessedHashes = [];
	private ClientEngine? Engine;

	// Stats counters
	private long HashesReceived = 0;
	private long FetchAttempts = 0;
	private long FetchSuccesses = 0;
	private long FetchTimeouts = 0;
	private long FetchErrors = 0;
	private long ActiveFetches = 0;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		Logger.LogInformation("Starting Metadata Fetcher on TCP port {Port} (max {Max} concurrent, {Timeout}s timeout)...", 
			TcpListenPort, MaxConcurrentFetches, TimeoutSeconds);

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
		Logger.LogInformation("MonoTorrent engine started");

		SemaphoreSlim Semaphore = new(MaxConcurrentFetches);
		_ = StatsLoopAsync(CancellationToken);

		await foreach (string HashHex in HashChannelReader.ReadAllAsync(CancellationToken))
		{
			Interlocked.Increment(ref HashesReceived);

			if (ProcessedHashes.Contains(HashHex))
			{
				continue;
			}

			ProcessedHashes.Add(HashHex);

			await Semaphore.WaitAsync(CancellationToken);
			Interlocked.Increment(ref ActiveFetches);
			_ = ProcessHashAsync(HashHex, Semaphore, CancellationToken);
		}
	}

	private async Task StatsLoopAsync(CancellationToken CancellationToken)
	{
		while (!CancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(StatsIntervalSeconds), CancellationToken);

			Logger.LogInformation(
				"[FETCHER] Received: {Recv} | Attempts: {Attempts} | Success: {Success} | Timeout: {Timeout} | Errors: {Errors} | Active: {Active}",
				Interlocked.Read(ref HashesReceived),
				Interlocked.Read(ref FetchAttempts),
				Interlocked.Read(ref FetchSuccesses),
				Interlocked.Read(ref FetchTimeouts),
				Interlocked.Read(ref FetchErrors),
				Interlocked.Read(ref ActiveFetches));
		}
	}

	private async Task ProcessHashAsync(string HashHex, SemaphoreSlim Semaphore, CancellationToken CancellationToken)
	{
		Interlocked.Increment(ref FetchAttempts);

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
				DateTime StartTime = DateTime.UtcNow;
				await Manager.StartAsync();

				// Poll for metadata with timeout
				while (true)
				{
					if (CancellationToken.IsCancellationRequested)
					{
						return; // App shutdown
					}

					if (Manager.HasMetadata)
					{
						await SaveToDatabaseAsync(Db, Manager, HashHex, CancellationToken);
						Interlocked.Increment(ref FetchSuccesses);
						Logger.LogInformation("[INDEXED] {Name} ({Size} bytes)", 
							Manager.Torrent?.Name ?? HashHex, 
							Manager.Torrent?.Size ?? 0);
						return;
					}

					// Check timeout
					if ((DateTime.UtcNow - StartTime).TotalSeconds >= TimeoutSeconds)
					{
						Interlocked.Increment(ref FetchTimeouts);
						return;
					}

					await Task.Delay(500, CancellationToken);
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
			Interlocked.Increment(ref FetchErrors);
			Logger.LogDebug("[ERROR] {Hash}: {Message}", HashHex[..16] + "...", Ex.Message);
		}
		finally
		{
			Interlocked.Decrement(ref ActiveFetches);
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
