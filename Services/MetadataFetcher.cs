namespace DhtScraper.Services;

/// <summary>Fetches torrent metadata via BEP 009 extension protocol.</summary>
/// <remarks>
/// Consumes info_hash values from the channel and uses MonoTorrent
/// to download only the metadata (file names, sizes) from the swarm.
/// </remarks>
public sealed class MetadataFetcher(
	ChannelReader<string> HashChannelReader,
	IServiceScopeFactory ScopeFactory) : BackgroundService
{
	private const int TimeoutSeconds = 10;
	private const int MaxConcurrentFetches = 100;
	private const int TcpListenPort = 55555;
	private static readonly string MetadataSavePath = Path.Combine(AppContext.BaseDirectory, "Downloads_Metadata");

	private readonly HashSet<string> ProcessedHashes = [];
	private ClientEngine? Engine;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		EngineSettingsBuilder SettingsBuilder = new()
		{
			AllowedEncryption = [EncryptionType.PlainText, EncryptionType.RC4Full, EncryptionType.RC4Header],
			ListenEndPoints = new Dictionary<string, IPEndPoint>
			{
				[string.Empty] = new IPEndPoint(IPAddress.Any, TcpListenPort)
			},
			// Enable MonoTorrent's DHT so it can find peers for metadata exchange
			DhtEndPoint = new IPEndPoint(IPAddress.Any, 6882)
		};

		Engine = new ClientEngine(SettingsBuilder.ToSettings());
		await Engine.StartAllAsync();

		SemaphoreSlim Semaphore = new(MaxConcurrentFetches);

		await foreach (string HashHex in HashChannelReader.ReadAllAsync(CancellationToken))
		{
			Interlocked.Increment(ref ConsoleRenderer.FetcherReceived);

			if (ProcessedHashes.Contains(HashHex))
			{
				continue;
			}

			ProcessedHashes.Add(HashHex);

			await Semaphore.WaitAsync(CancellationToken);
			Interlocked.Increment(ref ConsoleRenderer.FetcherActive);
			_ = ProcessHashAsync(HashHex, Semaphore, CancellationToken);
		}
	}

	private async Task ProcessHashAsync(string HashHex, SemaphoreSlim Semaphore, CancellationToken CancellationToken)
	{
		Interlocked.Increment(ref ConsoleRenderer.FetcherAttempts);

		try
		{
			using IServiceScope Scope = ScopeFactory.CreateScope();
			TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();

			bool Exists = await Db.Torrents.AnyAsync(T => T.InfoHash == HashHex, CancellationToken);
			if (Exists)
			{
				return;
			}

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

				while (true)
				{
					if (CancellationToken.IsCancellationRequested)
					{
						return;
					}

					if (Manager.HasMetadata)
					{
						await SaveToDatabaseAsync(Db, Manager, HashHex, CancellationToken);
						Interlocked.Increment(ref ConsoleRenderer.FetcherSuccesses);

						// Add to recent list for TUI
						string Name = Manager.Torrent?.Name ?? HashHex;
						ConsoleRenderer.RecentIndexed.Enqueue(Name);
						while (ConsoleRenderer.RecentIndexed.Count > 50)
						{
							ConsoleRenderer.RecentIndexed.TryDequeue(out _);
						}

						return;
					}

					if ((DateTime.UtcNow - StartTime).TotalSeconds >= TimeoutSeconds)
					{
						Interlocked.Increment(ref ConsoleRenderer.FetcherTimeouts);
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
		catch
		{
			Interlocked.Increment(ref ConsoleRenderer.FetcherErrors);
		}
		finally
		{
			Interlocked.Decrement(ref ConsoleRenderer.FetcherActive);
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
			CreationDate = Manager.Torrent.CreationDate,
			Comment = Manager.Torrent.Comment,
			CreatedBy = Manager.Torrent.CreatedBy,
			IsPrivate = Manager.Torrent.IsPrivate,
			PieceLengthBytes = Manager.Torrent.PieceLength,
			PieceCount = (int)Math.Ceiling((double)Manager.Torrent.Size / Manager.Torrent.PieceLength),
			FileCount = Manager.Torrent.Files.Count,
			Trackers = string.Join(";", Manager.Torrent.AnnounceUrls.SelectMany(T => T)),
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
