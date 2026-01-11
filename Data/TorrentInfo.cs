namespace DhtScraper.Data;

/// <summary>Discovered torrent metadata indexed from DHT network.</summary>
[Table("Torrents")]
public sealed class TorrentInfo
{
	/// <summary>Primary key.</summary>
	public int Id { get; set; }

	/// <summary>Hex-encoded info hash (40 characters).</summary>
	[MaxLength(40)]
	public required string InfoHash { get; set; }

	/// <summary>Torrent name extracted from metadata.</summary>
	public string? Name { get; set; }

	/// <summary>Total size of all files in bytes.</summary>
	public long TotalSizeBytes { get; set; }

	/// <summary>UTC timestamp when this torrent was discovered.</summary>
	public DateTime DiscoveredAtUtc { get; set; } = DateTime.UtcNow;

	/// <summary>Files contained in this torrent.</summary>
	public List<TorrentFile> Files { get; set; } = [];
}
