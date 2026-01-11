namespace DhtScraper.Data;

/// <summary>Discovered torrent metadata indexed from DHT network.</summary>
[Table("Torrents")]
public sealed class TorrentInfo
{
	/// <summary>Primary key.</summary>
	[Column("Id")]
	public int Id { get; set; }

	/// <summary>Hex-encoded info hash (40 characters).</summary>
	[Column("InfoHash"), MaxLength(40)]
	public required string InfoHash { get; set; }

	/// <summary>Torrent name extracted from metadata.</summary>
	[Column("Name")]
	public string? Name { get; set; }

	/// <summary>Total size of all files in bytes.</summary>
	[Column("TotalSizeBytes")]
	public long TotalSizeBytes { get; set; }

	/// <summary>UTC timestamp when this torrent was discovered.</summary>
	[Column("DiscoveredAtUtc")]
	public DateTime DiscoveredAtUtc { get; set; } = DateTime.UtcNow;

	/// <summary>Creation date from the .torrent metadata.</summary>
	[Column("CreationDate")]
	public DateTime? CreationDate { get; set; }

	/// <summary>Comment field from torrent metadata.</summary>
	[Column("Comment")]
	public string? Comment { get; set; }

	/// <summary>Client that created the torrent (e.g. "uTorrent/3.5.5").</summary>
	[Column("CreatedBy")]
	public string? CreatedBy { get; set; }

	/// <summary>Whether this is a private tracker torrent.</summary>
	[Column("IsPrivate")]
	public bool IsPrivate { get; set; }

	/// <summary>Size of each piece in bytes.</summary>
	[Column("PieceLengthBytes")]
	public int PieceLengthBytes { get; set; }

	/// <summary>Total number of pieces.</summary>
	[Column("PieceCount")]
	public int PieceCount { get; set; }

	/// <summary>Number of files (denormalized for fast queries).</summary>
	[Column("FileCount")]
	public int FileCount { get; set; }

	/// <summary>Semicolon-separated tracker URLs.</summary>
	[Column("Trackers")]
	public string? Trackers { get; set; }

	/// <summary>Files contained in this torrent.</summary>
	public List<TorrentFile> Files { get; set; } = [];
}
