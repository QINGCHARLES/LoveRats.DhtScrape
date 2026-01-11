namespace DhtScraper.Data;

/// <summary>Known DHT node endpoint for warm-start on restart.</summary>
[Table("DhtNodes")]
public sealed class DhtNode
{
	/// <summary>Primary key.</summary>
	[Column("Id")]
	public int Id { get; set; }

	/// <summary>IP address (IPv4 or IPv6).</summary>
	[Column("IpAddress"), MaxLength(45)]
	public required string IpAddress { get; set; }

	/// <summary>Port number.</summary>
	[Column("Port")]
	public int Port { get; set; }

	/// <summary>When this node was last seen responding.</summary>
	[Column("LastSeenUtc")]
	public DateTime LastSeenUtc { get; set; }

	/// <summary>Number of valid responses received from this node.</summary>
	[Column("ResponseCount")]
	public int ResponseCount { get; set; }
}
