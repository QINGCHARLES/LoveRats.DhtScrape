namespace DhtScraper.Services;

/// <summary>Recursive DHT spider using KRPC protocol over UDP.</summary>
/// <remarks>
/// Acts as a Sybil node by using random node IDs per request.
/// Sniffs info_hash values from get_peers and announce_peer queries.
/// </remarks>
public sealed class DhtCrawler(
	ChannelWriter<string> HashChannelWriter) : BackgroundService
{
	private const int ReceiveBufferSizeBytes = 256 * 1024;
	private const int StandardDhtPort = 6881;
	private const int RebootstrapDelayMs = 5000;
	private const int MaxSeenNodes = 50_000;
	private const int MaxQueriesPerSecond = 100;

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

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		UdpSocket.Bind(new IPEndPoint(IPAddress.Any, StandardDhtPort));
		UdpSocket.ReceiveBufferSize = ReceiveBufferSizeBytes;

		await BootstrapAsync(CancellationToken);

		Task ReceiveTask = ReceiveLoopAsync(CancellationToken);
		Task CrawlTask = CrawlLoopAsync(CancellationToken);

		await Task.WhenAll(ReceiveTask, CrawlTask);
	}

	private async Task BootstrapAsync(CancellationToken CancellationToken)
	{
		foreach (string Router in Routers)
		{
			try
			{
				IPAddress[] Addresses = await Dns.GetHostAddressesAsync(Router, CancellationToken);
				foreach (IPAddress Address in Addresses)
				{
					NodesToVisit.Enqueue(new IPEndPoint(Address, StandardDhtPort));
				}
			}
			catch
			{
				// Ignore resolution failures
			}
		}
	}

	private async Task ReceiveLoopAsync(CancellationToken CancellationToken)
	{
		byte[] Buffer = new byte[65535];

		while (!CancellationToken.IsCancellationRequested)
		{
			try
			{
				SocketReceiveFromResult Result = await UdpSocket.ReceiveFromAsync(
					Buffer,
					SocketFlags.None,
					new IPEndPoint(IPAddress.Any, 0),
					CancellationToken);

				Interlocked.Increment(ref ConsoleRenderer.CrawlerPacketsReceived);

				byte[] Data = Buffer.AsSpan(0, Result.ReceivedBytes).ToArray();
				ProcessPacket(Data, (IPEndPoint)Result.RemoteEndPoint);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
				// Ignore errors
			}
		}
	}

	private async Task CrawlLoopAsync(CancellationToken CancellationToken)
	{
		int DelayBetweenQueriesMs = 1000 / MaxQueriesPerSecond;

		while (!CancellationToken.IsCancellationRequested)
		{
			// Update queue size for TUI
			ConsoleRenderer.CrawlerQueueSize = NodesToVisit.Count;

			if (NodesToVisit.TryDequeue(out IPEndPoint? Endpoint))
			{
				await SendFindNodeAsync(Endpoint);
				await Task.Delay(DelayBetweenQueriesMs, CancellationToken);
			}
			else
			{
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
				BDictionary? Args = Dict.Get<BDictionary>("a");

				if (Args is not null && Args.ContainsKey("info_hash"))
				{
					byte[] HashBytes = Args.Get<BString>("info_hash")!.Value.ToArray();
					string HashHex = BitConverter.ToString(HashBytes).Replace("-", "");

					Interlocked.Increment(ref ConsoleRenderer.CrawlerHashesDiscovered);

					if (SeenHashes.Add(HashHex))
					{
						ConsoleRenderer.CrawlerUniqueHashes = SeenHashes.Count;
						HashChannelWriter.TryWrite(HashHex);
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
					ParseCompactNodes(NodesBytes);
				}
			}
		}
		catch
		{
			// Ignore malformed packets
		}
	}

	private void ParseCompactNodes(byte[] NodesBytes)
	{
		const int NodeSizeBytes = 26;
		const int IdSizeBytes = 20;
		const int IpSizeBytes = 4;
		const int PortSizeBytes = 2;

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

			if (SeenNodes.Add(Endpoint.ToString()))
			{
				NodesToVisit.Enqueue(Endpoint);
				Interlocked.Increment(ref ConsoleRenderer.CrawlerNodesDiscovered);

				if (SeenNodes.Count > MaxSeenNodes)
				{
					SeenNodes.Clear();
				}
			}
		}
	}

	private async Task SendFindNodeAsync(IPEndPoint Target)
	{
		byte[] TargetId = new byte[20];
		Random.Shared.NextBytes(TargetId);

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
			Interlocked.Increment(ref ConsoleRenderer.CrawlerPacketsSent);
		}
		catch
		{
			// Ignore send failures
		}
	}
}
