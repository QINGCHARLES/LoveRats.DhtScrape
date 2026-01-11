namespace DhtScraper.Services;

/// <summary>Renders live TUI dashboard to console.</summary>
public sealed class ConsoleRenderer : BackgroundService
{
	private const int RefreshIntervalMs = 500;
	private const int MaxRecentItems = 10;
	private const int BoxWidth = 70;
	private const int NameMaxWidth = 64;

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
		Sb.AppendLine($"\e[1;36m╔{"".PadRight(BoxWidth, '═')}╗\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m{Center("⚡ DHT SCRAPER - LIVE DASHBOARD ⚡", BoxWidth, "\e[1;33m")}\e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m╠{"".PadRight(BoxWidth, '═')}╣\e[0m");

		// Uptime
		Sb.AppendLine($"\e[1;36m║\e[0m  \e[1mUptime:\e[0m {Uptime:hh\\:mm\\:ss}{"".PadRight(BoxWidth - 20)}\e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m╠{"".PadRight(BoxWidth, '═')}╣\e[0m");

		// Crawler stats
		Sb.AppendLine($"\e[1;36m║\e[0m  \e[1;32m📡 DHT CRAWLER\e[0m{"".PadRight(BoxWidth - 17)}\e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Packets: \e[33m{CrawlerPacketsSent,10}\e[0m sent  │  \e[33m{CrawlerPacketsReceived,10}\e[0m recv       \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Queue:   \e[33m{CrawlerQueueSize,10}\e[0m       │  Nodes: \e[33m{CrawlerNodesDiscovered,10}\e[0m       \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Hashes:  \e[33m{CrawlerHashesDiscovered,10}\e[0m found │  \e[33m{CrawlerUniqueHashes,10}\e[0m unique    \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m╠{"".PadRight(BoxWidth, '═')}╣\e[0m");

		// Fetcher stats
		Sb.AppendLine($"\e[1;36m║\e[0m  \e[1;35m📥 METADATA FETCHER\e[0m{"".PadRight(BoxWidth - 22)}\e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Received: \e[33m{FetcherReceived,8}\e[0m       │  Active:  \e[33m{FetcherActive,8}\e[0m           \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Success:  \e[1;32m{FetcherSuccesses,8}\e[0m       │  Rate:    \e[1;32m{SuccessRate,7:F1}%\e[0m           \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m║\e[0m    Timeout:  \e[33m{FetcherTimeouts,8}\e[0m       │  Errors:  \e[31m{FetcherErrors,8}\e[0m           \e[1;36m║\e[0m");
		Sb.AppendLine($"\e[1;36m╠{"".PadRight(BoxWidth, '═')}╣\e[0m");

		// Recent indexed
		Sb.AppendLine($"\e[1;36m║\e[0m  \e[1;34m📋 RECENTLY INDEXED\e[0m{"".PadRight(BoxWidth - 22)}\e[1;36m║\e[0m");

		string[] Recent = RecentIndexed.ToArray().TakeLast(MaxRecentItems).Reverse().ToArray();
		for (int i = 0; i < MaxRecentItems; i++)
		{
			if (i < Recent.Length)
			{
				string Name = TruncateToDisplayWidth(Recent[i], NameMaxWidth);
				int DisplayWidth = GetDisplayWidth(Name);
				int Padding = NameMaxWidth - DisplayWidth;
				Sb.AppendLine($"\e[1;36m║\e[0m    \e[32m✓\e[0m {Name}{new string(' ', Padding)} \e[1;36m║\e[0m");
			}
			else
			{
				Sb.AppendLine($"\e[1;36m║\e[0m    \e[90m-\e[0m {"".PadRight(NameMaxWidth)} \e[1;36m║\e[0m");
			}
		}

		Sb.AppendLine($"\e[1;36m╚{"".PadRight(BoxWidth, '═')}╝\e[0m");
		Sb.AppendLine("\e[90mPress Ctrl+C to stop\e[0m");

		Console.Write(Sb.ToString());
	}

	private static string Center(string Text, int Width, string Color = "")
	{
		int Padding = (Width - Text.Length) / 2;
		return $"{new string(' ', Padding)}{Color}{Text}\e[0m{new string(' ', Width - Padding - Text.Length)}";
	}

	/// <summary>Gets display width accounting for East Asian double-width characters.</summary>
	private static int GetDisplayWidth(string Text)
	{
		int Width = 0;
		foreach (char C in Text)
		{
			// CJK characters and other full-width characters are double-width
			if (C >= 0x1100 && (
				C <= 0x115F || // Hangul Jamo
				C == 0x2329 || C == 0x232A || // Angle brackets
				(C >= 0x2E80 && C <= 0xA4CF && C != 0x303F) || // CJK
				(C >= 0xAC00 && C <= 0xD7A3) || // Hangul Syllables
				(C >= 0xF900 && C <= 0xFAFF) || // CJK Compatibility
				(C >= 0xFE10 && C <= 0xFE1F) || // Vertical forms
				(C >= 0xFE30 && C <= 0xFE6F) || // CJK Compatibility Forms
				(C >= 0xFF00 && C <= 0xFF60) || // Fullwidth Forms
				(C >= 0xFFE0 && C <= 0xFFE6))) // Fullwidth symbols
			{
				Width += 2;
			}
			else
			{
				Width += 1;
			}
		}
		return Width;
	}

	/// <summary>Truncates string to fit within display width, adding ellipsis if needed.</summary>
	private static string TruncateToDisplayWidth(string Text, int MaxWidth)
	{
		if (GetDisplayWidth(Text) <= MaxWidth)
		{
			return Text;
		}

		StringBuilder Result = new();
		int CurrentWidth = 0;
		int EllipsisWidth = 3; // "..."

		foreach (char C in Text)
		{
			int CharWidth = (C >= 0x1100 && (
				C <= 0x115F ||
				C == 0x2329 || C == 0x232A ||
				(C >= 0x2E80 && C <= 0xA4CF && C != 0x303F) ||
				(C >= 0xAC00 && C <= 0xD7A3) ||
				(C >= 0xF900 && C <= 0xFAFF) ||
				(C >= 0xFE10 && C <= 0xFE1F) ||
				(C >= 0xFE30 && C <= 0xFE6F) ||
				(C >= 0xFF00 && C <= 0xFF60) ||
				(C >= 0xFFE0 && C <= 0xFFE6))) ? 2 : 1;

			if (CurrentWidth + CharWidth + EllipsisWidth > MaxWidth)
			{
				Result.Append("...");
				break;
			}

			Result.Append(C);
			CurrentWidth += CharWidth;
		}

		return Result.ToString();
	}
}
