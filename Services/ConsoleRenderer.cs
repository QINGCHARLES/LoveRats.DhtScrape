namespace DhtScraper.Services;

/// <summary>Renders live TUI dashboard to console.</summary>
public sealed class ConsoleRenderer : BackgroundService
{
	private const int RefreshIntervalMs = 500;
	private const int MaxRecentItems = 10;

	// Shared stats (set by other services)
#pragma warning disable CS1591 // Internal cross-service stats, no docs needed
	public static long CrawlerPacketsSent = 0;
	public static long CrawlerPacketsReceived = 0;
	public static long CrawlerQueueSize = 0;
	public static long CrawlerNodesDiscovered = 0;
	public static long CrawlerHashesDiscovered = 0;
	public static long CrawlerUniqueHashes = 0;

	public static long FetcherReceived = 0;
	public static long FetcherAttempts = 0;
	public static long FetcherSuccesses = 0;
	public static long FetcherTimeouts = 0;
	public static long FetcherErrors = 0;
	public static long FetcherActive = 0;

	public static readonly ConcurrentQueue<string> RecentIndexed = new();
#pragma warning restore CS1591

	private readonly DateTime StartTime = DateTime.UtcNow;

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken CancellationToken)
	{
		// Wait a moment for other services to start
		await Task.Delay(2000, CancellationToken);

		// Hide cursor and clear screen
		Console.Write("\e[?25l"); // Hide cursor
		Console.Clear();

		try
		{
			while (!CancellationToken.IsCancellationRequested)
			{
				Render();
				await Task.Delay(RefreshIntervalMs, CancellationToken);
			}
		}
		finally
		{
			Console.Write("\e[?25h"); // Show cursor
		}
	}

	private void Render()
	{
		TimeSpan Uptime = DateTime.UtcNow - StartTime;
		double SuccessRate = FetcherAttempts > 0 ? (double)FetcherSuccesses / FetcherAttempts * 100 : 0;

		StringBuilder Sb = new();

		// Move cursor to top-left
		Sb.Append("\e[H");

		// Header
		Sb.AppendLine("\e[1;36m╔══════════════════════════════════════════════════════════════════╗\e[0m");
		Sb.AppendLine("\e[1;36m║\e[0m              \e[1;33m⚡ DHT SCRAPER - LIVE DASHBOARD ⚡\e[0m                 \e[1;36m║\e[0m");
		Sb.AppendLine("\e[1;36m╠══════════════════════════════════════════════════════════════════╣\e[0m");

		// Uptime
		Sb.AppendLine($"\e[1;36m║\e[0m  \e[1mUptime:\e[0m {Uptime:hh\\:mm\\:ss}                                                  \e[1;36m║\e[0m");
		Sb.AppendLine("\e[1;36m╠══════════════════════════════════════════════════════════════════╣\e[0m");

		// Crawler stats
		Sb.AppendLine("\e[1;36m║\e[0m  \e[1;32m📡 DHT CRAWLER\e[0m                                                   \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Packets:  \e[33m{CrawlerPacketsSent,8}\e[0m sent  │  \e[33m{CrawlerPacketsReceived,8}\e[0m recv          \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Queue:    \e[33m{CrawlerQueueSize,8}\e[0m       │  Nodes: \e[33m{CrawlerNodesDiscovered,8}\e[0m          \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Hashes:   \e[33m{CrawlerHashesDiscovered,8}\e[0m found │  \e[33m{CrawlerUniqueHashes,8}\e[0m unique        \e[1;36m║\e[0m");
		Sb.AppendLine("\e[1;36m╠══════════════════════════════════════════════════════════════════╣\e[0m");

		// Fetcher stats
		Sb.AppendLine("\e[1;36m║\e[0m  \e[1;35m📥 METADATA FETCHER\e[0m                                              \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Received: \e[33m{FetcherReceived,8}\e[0m       │  Active: \e[33m{FetcherActive,8}\e[0m          \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Success:  \e[1;32m{FetcherSuccesses,8}\e[0m       │  Rate:   \e[1;32m{SuccessRate,7:F1}%\e[0m          \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Timeout:  \e[33m{FetcherTimeouts,8}\e[0m       │  Errors: \e[31m{FetcherErrors,8}\e[0m          \e[1;36m║\e[0m");
		Sb.AppendLine("\e[1;36m╠══════════════════════════════════════════════════════════════════╣\e[0m");

		// Recent indexed
		Sb.AppendLine("\e[1;36m║\e[0m  \e[1;34m📋 RECENTLY INDEXED\e[0m                                             \e[1;36m║\e[0m");

		string[] Recent = RecentIndexed.ToArray().TakeLast(MaxRecentItems).ToArray();
		for (int i = 0; i < MaxRecentItems; i++)
		{
			if (i < Recent.Length)
			{
				string Name = Recent[Recent.Length - 1 - i];
				if (Name.Length > 60)
				{
					Name = Name[..57] + "...";
				}
				Sb.AppendLine($"\e[1;36m║\e[0m    \e[32m✓\e[0m {Name,-62}\e[1;36m║\e[0m");
			}
			else
			{
				Sb.AppendLine($"\e[1;36m║\e[0m    \e[90m-\e[0m {"",-62}\e[1;36m║\e[0m");
			}
		}

		Sb.AppendLine("\e[1;36m╚══════════════════════════════════════════════════════════════════╝\e[0m");
		Sb.AppendLine("\e[90mPress Ctrl+C to stop\e[0m");

		Console.Write(Sb.ToString());
	}
}
