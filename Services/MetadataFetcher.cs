namespace DhtScraper.Services;

/// <summary>Fetches torrent metadata via BEP 009 extension protocol.</summary>
/// <remarks>
/// Consumes info_hash values from the channel and uses MonoTorrent
/// to download only the metadata (file names, sizes) from the swarm.
/// Persists pending hashes for restart recovery.
/// </remarks>
public sealed class MetadataFetcher(
	ChannelReader<string> HashChannelReader,
	ChannelWriter<string> HashChannelWriter,
	IServiceScopeFactory ScopeFactory,
	IHostApplicationLifetime Lifetime) : BackgroundService
{
	private const int TimeoutSeconds = 10;
	private const int MaxConcurrentFetches = 100;
	private const int TcpListenPort = 55555;
	private static readonly string MetadataSavePath = Path.Combine(AppContext.BaseDirectory, "Downloads_Metadata");
	private static readonly string EngineStatePath = Path.Combine(AppContext.BaseDirectory, "engine_state");

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
			DhtEndPoint = new IPEndPoint(IPAddress.Any, 6882),
			// Set cache directory for state persistence
			CacheDirectory = EngineStatePath
		};

		EngineSettings Settings = SettingsBuilder.ToSettings();

		// Try to restore engine state if exists (includes DHT routing table)
		if (Directory.Exists(EngineStatePath))
		{
			try
			{
				Engine = await ClientEngine.RestoreStateAsync(EngineStatePath);
			}
			catch
			{
				// Fall back to creating new engine on restore failure
				Engine = new ClientEngine(Settings);
			}
		}
		else
		{
			Engine = new ClientEngine(Settings);
		}

		await Engine.StartAllAsync();

		// Register shutdown handler to save engine state
		Lifetime.ApplicationStopping.Register(() => SaveEngineStateOnShutdown());

		// Load initial state from database
		await LoadInitialStateAsync(CancellationToken);

		SemaphoreSlim Semaphore = new(MaxConcurrentFetches);

		await foreach (string HashHex in HashChannelReader.ReadAllAsync(CancellationToken))
		{
			Interlocked.Increment(ref ConsoleRenderer.FetcherReceived);

			if (ProcessedHashes.Contains(HashHex))
			{
				continue;
			}

			ProcessedHashes.Add(HashHex);

			// Persist hash to pending table before processing
			await AddToPendingAsync(HashHex, CancellationToken);

			await Semaphore.WaitAsync(CancellationToken);
			Interlocked.Increment(ref ConsoleRenderer.FetcherActive);
			_ = ProcessHashAsync(HashHex, Semaphore, CancellationToken);
		}
	}

	private async Task LoadInitialStateAsync(CancellationToken CancellationToken)
	{
		using IServiceScope Scope = ScopeFactory.CreateScope();
		TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();

		// Pre-populate ProcessedHashes from existing torrents
		List<string> ExistingHashes = await Db.Torrents
			.AsNoTracking()
			.Select(T => T.InfoHash)
			.ToListAsync(CancellationToken);

		foreach (string Hash in ExistingHashes)
		{
			ProcessedHashes.Add(Hash);
		}

		// Re-queue pending hashes from previous run
		List<string> PendingHashes = await Db.PendingHashes
			.AsNoTracking()
			.Select(P => P.InfoHash)
			.ToListAsync(CancellationToken);

		foreach (string Hash in PendingHashes)
		{
			if (!ProcessedHashes.Contains(Hash))
			{
				HashChannelWriter.TryWrite(Hash);
			}
		}
	}

	private async Task AddToPendingAsync(string HashHex, CancellationToken CancellationToken)
	{
		using IServiceScope Scope = ScopeFactory.CreateScope();
		TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();

		bool Exists = await Db.PendingHashes.AnyAsync(P => P.InfoHash == HashHex, CancellationToken);
		if (!Exists)
		{
			Db.PendingHashes.Add(new PendingHash
			{
				InfoHash = HashHex,
				QueuedAtUtc = DateTime.UtcNow
			});
			await Db.SaveChangesAsync(CancellationToken);
		}
	}

	private async Task RemoveFromPendingAsync(string HashHex, CancellationToken CancellationToken)
	{
		using IServiceScope Scope = ScopeFactory.CreateScope();
		TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();

		PendingHash? Pending = await Db.PendingHashes.FirstOrDefaultAsync(P => P.InfoHash == HashHex, CancellationToken);
		if (Pending is not null)
		{
			Db.PendingHashes.Remove(Pending);
			await Db.SaveChangesAsync(CancellationToken);
		}
	}

	private void SaveEngineStateOnShutdown()
	{
		if (Engine is null)
		{
			return;
		}

		// Fire and forget save on shutdown
		_ = Task.Run(async () =>
		{
			try
			{
				await Engine.SaveStateAsync();
			}
			catch
			{
				// Ignore errors during shutdown
			}
		});
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
				// Already in DB, remove from pending
				await RemoveFromPendingAsync(HashHex, CancellationToken);
				return;
			}

			if (HashHex.Length != 40)
			{
				await RemoveFromPendingAsync(HashHex, CancellationToken);
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

						// Remove from pending after successful save
						await RemoveFromPendingAsync(HashHex, CancellationToken);

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
						// Keep in pending for retry on next run
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
			// Keep in pending for retry on next run
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

