namespace DhtScraper.Data;

/// <summary>File entry within a torrent.</summary>
[Table("Files")]
public sealed class TorrentFile
{
	/// <summary>Primary key.</summary>
	[Column("Id")]
	public int Id { get; set; }

	/// <summary>File path within the torrent.</summary>
	[Column("Path")]
	public required string Path { get; set; }

	/// <summary>File size in bytes.</summary>
	[Column("SizeBytes")]
	public long SizeBytes { get; set; }

	/// <summary>Foreign key to parent torrent.</summary>
	[Column("TorrentInfoId")]
	public int TorrentInfoId { get; set; }
}
