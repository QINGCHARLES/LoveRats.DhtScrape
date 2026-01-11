namespace DhtScraper.Data;

/// <summary>Info hash queued for metadata fetching, persisted for restart recovery.</summary>
[Table("PendingHashes")]
public sealed class PendingHash
{
	/// <summary>Primary key.</summary>
	[Column("Id")]
	public int Id { get; set; }

	/// <summary>Hex-encoded info hash (40 characters).</summary>
	[Column("InfoHash"), MaxLength(40)]
	public required string InfoHash { get; set; }

	/// <summary>When this hash was added to the queue.</summary>
	[Column("QueuedAtUtc")]
	public DateTime QueuedAtUtc { get; set; }
}
