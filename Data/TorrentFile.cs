namespace DhtScraper.Data;

/// <summary>File entry within a torrent.</summary>
[Table("Files")]
public sealed class TorrentFile
{
	/// <summary>Primary key.</summary>
	public int Id { get; set; }

	/// <summary>File path within the torrent.</summary>
	public required string Path { get; set; }

	/// <summary>File size in bytes.</summary>
	public long SizeBytes { get; set; }

	/// <summary>Foreign key to parent torrent.</summary>
	public int TorrentInfoId { get; set; }
}
