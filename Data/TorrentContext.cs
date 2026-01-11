namespace DhtScraper.Data;

/// <summary>Database context for torrent indexing with SQLite.</summary>
public sealed class TorrentContext : DbContext
{
	/// <summary>Indexed torrents discovered from DHT network.</summary>
	public DbSet<TorrentInfo> Torrents { get; set; } = null!;

	/// <summary>Files contained within indexed torrents.</summary>
	public DbSet<TorrentFile> Files { get; set; } = null!;

	/// <summary>Known DHT nodes for warm-start on restart.</summary>
	public DbSet<DhtNode> Nodes { get; set; } = null!;

	/// <summary>Pending hashes awaiting metadata fetch.</summary>
	public DbSet<PendingHash> PendingHashes { get; set; } = null!;

	/// <inheritdoc/>
	protected override void OnConfiguring(DbContextOptionsBuilder Options)
	{
		// Cache=Shared is critical for concurrent read/write performance
		Options.UseSqlite("Data Source=dht_index.db;Cache=Shared");
	}

	/// <inheritdoc/>
	protected override void OnModelCreating(ModelBuilder ModelBuilder)
	{
		ModelBuilder.Entity<TorrentInfo>()
			.HasIndex(T => T.InfoHash)
			.IsUnique();

		ModelBuilder.Entity<DhtNode>()
			.HasIndex(N => new { N.IpAddress, N.Port })
			.IsUnique();

		ModelBuilder.Entity<PendingHash>()
			.HasIndex(P => P.InfoHash)
			.IsUnique();
	}
}
