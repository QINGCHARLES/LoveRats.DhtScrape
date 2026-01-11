namespace DhtScraper.Services;

/// <summary>Recursive DHT spider using KRPC protocol over UDP.</summary>
/// <remarks>
/// Acts as a Sybil node by using random node IDs per request.
/// Sniffs info_hash values from get_peers and announce_peer queries.
/// </remarks>
public sealed class DhtCrawler(
	ChannelWriter<string> HashChannelWriter,
	ILogger<DhtCrawler> Logger) : BackgroundService
{
	private const int ReceiveBufferSizeBytes = 1024 * 1024;
	private const int StandardDhtPort = 6881;
	private const int RebootstrapDelayMs = 1000;
	private const int ThrottleDelayMs = 10;
	private const int LowQueueThreshold = 100;
	private const int MaxSeenNodes = 100_000;
	private const int StatsIntervalSeconds = 10;

	private static readonly string[] Routers =
	[
		"router.bittorrent.com",
		"dht.transmissionbt.com",
		"router.utorrent.com"
	];

	private readonly Socket UdpSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
	private readonly BencodeParser Parser = new();
	private readonly ConcurrentQueue<IPEndPoint> NodesToVisit = new();
	private readonly HashSet<string> SeenNodes = [];
	private readonly HashSet<string> SeenHashes = [];

	// Stats counters
	private long PacketsReceived = 0;
	private long PacketsSent = 0;
	private long HashesDiscovered = 0;
	private long NodesDiscovered = 0;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		Logger.LogInformation("Starting Recursive DHT Crawler on port {Port}...", StandardDhtPort);

		UdpSocket.Bind(new IPEndPoint(IPAddress.Any, StandardDhtPort));
		UdpSocket.ReceiveBufferSize = ReceiveBufferSizeBytes;

		await BootstrapAsync(CancellationToken);

		Task ReceiveTask = ReceiveLoopAsync(CancellationToken);
		Task CrawlTask = CrawlLoopAsync(CancellationToken);
		Task StatsTask = StatsLoopAsync(CancellationToken);

		await Task.WhenAll(ReceiveTask, CrawlTask, StatsTask);
	}

	private async Task StatsLoopAsync(CancellationToken CancellationToken)
	{
		while (!CancellationToken.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(StatsIntervalSeconds), CancellationToken);

			Logger.LogInformation(
				"[STATS] Sent: {Sent} | Recv: {Recv} | Queue: {Queue} | Nodes: {Nodes} | Hashes: {Hashes} | Unique: {Unique}",
				Interlocked.Read(ref PacketsSent),
				Interlocked.Read(ref PacketsReceived),
				NodesToVisit.Count,
				Interlocked.Read(ref NodesDiscovered),
				Interlocked.Read(ref HashesDiscovered),
				SeenHashes.Count);
		}
	}

	private async Task BootstrapAsync(CancellationToken CancellationToken)
	{
		Logger.LogInformation("Bootstrapping from {Count} routers...", Routers.Length);

		foreach (string Router in Routers)
		{
			try
			{
				IPAddress[] Addresses = await Dns.GetHostAddressesAsync(Router, CancellationToken);
				Logger.LogInformation("Resolved {Router} to {Count} addresses", Router, Addresses.Length);

				foreach (IPAddress Address in Addresses)
				{
					NodesToVisit.Enqueue(new IPEndPoint(Address, StandardDhtPort));
				}
			}
			catch (Exception Ex)
			{
				Logger.LogWarning("Failed to resolve {Router}: {Message}", Router, Ex.Message);
			}
		}

		Logger.LogInformation("Bootstrap complete, {Count} initial nodes queued", NodesToVisit.Count);
	}

	private async Task ReceiveLoopAsync(CancellationToken CancellationToken)
	{
		byte[] Buffer = new byte[65535];
		Logger.LogInformation("Receive loop started, waiting for packets...");

		while (!CancellationToken.IsCancellationRequested)
		{
			try
			{
				SocketReceiveFromResult Result = await UdpSocket.ReceiveFromAsync(
					Buffer,
					SocketFlags.None,
					new IPEndPoint(IPAddress.Any, 0),
					CancellationToken);

				Interlocked.Increment(ref PacketsReceived);

				byte[] Data = Buffer.AsSpan(0, Result.ReceivedBytes).ToArray();
				ProcessPacket(Data, (IPEndPoint)Result.RemoteEndPoint);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception Ex)
			{
				Logger.LogError("UDP receive error: {Message}", Ex.Message);
			}
		}
	}

	private async Task CrawlLoopAsync(CancellationToken CancellationToken)
	{
		Logger.LogInformation("Crawl loop started...");

		while (!CancellationToken.IsCancellationRequested)
		{
			if (NodesToVisit.TryDequeue(out IPEndPoint? Endpoint))
			{
				await SendFindNodeAsync(Endpoint);

				// Pace ourselves slightly to avoid flooding network card
				if (NodesToVisit.Count < LowQueueThreshold)
				{
					await Task.Delay(ThrottleDelayMs, CancellationToken);
				}
			}
			else
			{
				Logger.LogDebug("Queue empty, re-bootstrapping...");
				await BootstrapAsync(CancellationToken);
				await Task.Delay(RebootstrapDelayMs, CancellationToken);
			}
		}
	}

	private void ProcessPacket(byte[] Data, IPEndPoint Sender)
	{
		try
		{
			BDictionary Dict = Parser.Parse<BDictionary>(Data);

			// 1. Sniff InfoHashes from queries (get_peers / announce_peer)
			if (Dict.Get<BString>("y")?.ToString() == "q")
			{
				string? QueryType = Dict.Get<BString>("q")?.ToString();
				BDictionary? Args = Dict.Get<BDictionary>("a");

				if (Args is not null && Args.ContainsKey("info_hash"))
				{
					byte[] HashBytes = Args.Get<BString>("info_hash")!.Value.ToArray();
					string HashHex = BitConverter.ToString(HashBytes).Replace("-", "");

					if (SeenHashes.Add(HashHex))
					{
						Interlocked.Increment(ref HashesDiscovered);
						HashChannelWriter.TryWrite(HashHex);
						Logger.LogDebug("[{Type}] New hash from {Sender}: {Hash}",
							QueryType?.ToUpperInvariant() ?? "QUERY", Sender, HashHex[..16] + "...");
					}
				}
			}

			// 2. Extract new nodes from responses
			if (Dict.Get<BString>("y")?.ToString() == "r")
			{
				BDictionary? Response = Dict.Get<BDictionary>("r");
				if (Response is not null && Response.ContainsKey("nodes"))
				{
					byte[] NodesBytes = Response.Get<BString>("nodes")!.Value.ToArray();
					int NodeCount = ParseCompactNodes(NodesBytes);

					if (NodeCount > 0)
					{
						Logger.LogDebug("Got {Count} nodes from {Sender}", NodeCount, Sender);
					}
				}
			}
		}
		catch
		{
			// Ignore malformed packets
		}
	}

	private int ParseCompactNodes(byte[] NodesBytes)
	{
		// Each node is 26 bytes: [20 bytes ID][4 bytes IP][2 bytes Port]
		const int NodeSizeBytes = 26;
		const int IdSizeBytes = 20;
		const int IpSizeBytes = 4;
		const int PortSizeBytes = 2;

		int NodesAdded = 0;

		for (int i = 0; i < NodesBytes.Length; i += NodeSizeBytes)
		{
			if (i + NodeSizeBytes > NodesBytes.Length)
			{
				break;
			}

			byte[] IpBytes = NodesBytes.AsSpan(i + IdSizeBytes, IpSizeBytes).ToArray();
			byte[] PortBytes = NodesBytes.AsSpan(i + IdSizeBytes + IpSizeBytes, PortSizeBytes).ToArray();

			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(PortBytes);
			}

			ushort Port = BitConverter.ToUInt16(PortBytes, 0);
			IPAddress Ip = new(IpBytes);
			IPEndPoint Endpoint = new(Ip, Port);

			// Add to crawl queue if not seen recently
			if (SeenNodes.Add(Endpoint.ToString()))
			{
				NodesToVisit.Enqueue(Endpoint);
				Interlocked.Increment(ref NodesDiscovered);
				NodesAdded++;

				// Crude memory cleanup when set grows too large
				if (SeenNodes.Count > MaxSeenNodes)
				{
					Logger.LogInformation("Clearing seen nodes cache ({Count} entries)", SeenNodes.Count);
					SeenNodes.Clear();
				}
			}
		}

		return NodesAdded;
	}

	private async Task SendFindNodeAsync(IPEndPoint Target)
	{
		// Generate random target ID to explore different parts of the network
		byte[] TargetId = new byte[20];
		Random.Shared.NextBytes(TargetId);

		// Random ID makes us look like a new node every time (Sybil technique)
		byte[] MyId = new byte[20];
		Random.Shared.NextBytes(MyId);

		BDictionary Dict = new()
		{
			{ "t", "aa" },
			{ "y", "q" },
			{ "q", "find_node" },
			{
				"a", new BDictionary
				{
					{ "id", new BString(MyId) },
					{ "target", new BString(TargetId) }
				}
			}
		};

		byte[] Bytes = Dict.EncodeAsBytes();
		try
		{
			await UdpSocket.SendToAsync(Bytes, SocketFlags.None, Target);
			Interlocked.Increment(ref PacketsSent);
		}
		catch
		{
			// Ignore send failures
		}
	}
}
