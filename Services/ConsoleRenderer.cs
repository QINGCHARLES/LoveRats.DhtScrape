namespace DhtScraper.Services;

/// <summary>Renders live TUI dashboard to console.</summary>
public sealed class ConsoleRenderer : BackgroundService
{
	private const int RefreshIntervalMs = 500;
	private const int MaxRecentItems = 10;
	private const int WindowWidth = 78; // Fixed width for the box
	private const int ContentWidth = WindowWidth - 3; // Space inside

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
		await Task.Delay(2000, CancellationToken);
		Console.Write("\e[?25l");
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
			Console.Write("\e[?25h");
		}
	}

	private void Render()
	{
		TimeSpan Uptime = DateTime.UtcNow - StartTime;
		double SuccessRate = FetcherAttempts > 0 ? (double)FetcherSuccesses / FetcherAttempts * 100 : 0;

		const string Cyan = "\e[1;36m";
		const string Yellow = "\e[1;33m";
		const string Green = "\e[1;32m";
		const string Magenta = "\e[1;35m";
		const string Blue = "\e[1;34m";
		const string Red = "\e[31m";
		const string Gray = "\e[90m";
		const string Reset = "\e[0m";
		const string Bold = "\e[1m";

		StringBuilder Sb = new();
		Sb.Append("\e[H"); // Home cursor

		// Helper to draw a line with perfect alignment
		void DrawLine(string Content)
		{
			// 1. Draw Left Border
			Sb.Append($"{Cyan}║{Reset} ");
			
			// 2. Draw Content
			Sb.Append(Content);
			
			// 3. Force cursor to right edge using ANSI absolute positioning
			// \e[nG moves cursor to column n. We use WindowWidth.
			Sb.Append($"\e[{WindowWidth}G");
			
			// 4. Draw Right Border
			Sb.AppendLine($"{Cyan}║{Reset}");
		}

		void DrawDivider() => Sb.AppendLine($"{Cyan}╠{new string('═', WindowWidth - 2)}╣{Reset}");
		void DrawTop() => Sb.AppendLine($"{Cyan}╔{new string('═', WindowWidth - 2)}╗{Reset}");
		void DrawBottom() => Sb.AppendLine($"{Cyan}╚{new string('═', WindowWidth - 2)}╝{Reset}");

		// --- Header ---
		DrawTop();
		
		string Title = $"{Yellow}⚡ DHT SCRAPER - LIVE DASHBOARD ⚡{Reset}";
		int PadLeft = (ContentWidth - VisibleWidth("⚡ DHT SCRAPER - LIVE DASHBOARD ⚡")) / 2;
		DrawLine($"{new string(' ', PadLeft)}{Title}");
		
		DrawDivider();

		// --- Uptime ---
		DrawLine($"{Bold}Uptime:{Reset} {Uptime:hh\\:mm\\:ss}");
		
		DrawDivider();

		// --- Crawler ---
		DrawLine($"{Green}📡 DHT CRAWLER{Reset}");
		DrawLine($"   Packets:  {Yellow}{CrawlerPacketsSent,9}{Reset} sent   │ {Yellow}{CrawlerPacketsReceived,9}{Reset} recv");
		DrawLine($"   Queue:    {Yellow}{CrawlerQueueSize,9}{Reset}        │ Nodes:    {Yellow}{CrawlerNodesDiscovered,9}{Reset}");
		DrawLine($"   Hashes:   {Yellow}{CrawlerHashesDiscovered,9}{Reset} found  │ Unique:   {Yellow}{CrawlerUniqueHashes,9}{Reset}");
		
		DrawDivider();

		// --- Fetcher ---
		DrawLine($"{Magenta}📥 METADATA FETCHER{Reset}");
		DrawLine($"   Received: {Yellow}{FetcherReceived,9}{Reset}        │ Active:   {Yellow}{FetcherActive,9}{Reset}");
		// Check for 0 successes to avoid divide by zero, though SuccessRate handles it above visually
		DrawLine($"   Success:  {Green}{FetcherSuccesses,9}{Reset}        │ Rate:     {Green}{SuccessRate,8:F1}%{Reset}");
		DrawLine($"   Timeout:  {Yellow}{FetcherTimeouts,9}{Reset}        │ Errors:   {Red}{FetcherErrors,9}{Reset}");
		
		DrawDivider();

		// --- Recent ---
		DrawLine($"{Blue}📋 RECENTLY INDEXED{Reset}");

		string[] Recent = RecentIndexed.ToArray().TakeLast(MaxRecentItems).Reverse().ToArray();
		for (int i = 0; i < MaxRecentItems; i++)
		{
			if (i < Recent.Length)
			{
				// Truncate carefully taking double-width chars into account
				string Name = TruncateWithEllipsis(Recent[i], WindowWidth - 8); 
				DrawLine($"   {Green}✓{Reset} {Name}");
			}
			else
			{
				DrawLine($"   {Gray}-{Reset}");
			}
		}

		DrawBottom();
		Sb.AppendLine($"{Gray}Press Ctrl+C to stop{Reset}");

		Console.Write(Sb.ToString());
	}

	/// <summary>Calculates the visible width (accounting for double-width chars) without ANSI codes.</summary>
	private static int VisibleWidth(string Text)
	{
		int Width = 0;
		bool InAnsi = false;
		
		foreach (char C in Text)
		{
			if (C == '\e') { InAnsi = true; continue; }
			if (InAnsi && C == 'm') { InAnsi = false; continue; }
			if (InAnsi) continue;

			Width += IsDoubleWidth(C) ? 2 : 1;
		}
		return Width;
	}

	private static bool IsDoubleWidth(char C)
	{
		return C >= 0x1100 && (
			C <= 0x115F || // Hangul Jamo
			C == 0x2329 || C == 0x232A || // Angle brackets
			(C >= 0x2E80 && C <= 0xA4CF && C != 0x303F) || // CJK
			(C >= 0xAC00 && C <= 0xD7A3) || // Hangul Syllables
			(C >= 0xF900 && C <= 0xFAFF) || // CJK Compatibility
			(C >= 0xFE10 && C <= 0xFE1F) || // Vertical forms
			(C >= 0xFE30 && C <= 0xFE6F) || // CJK Compatibility Forms
			(C >= 0xFF00 && C <= 0xFF60) || // Fullwidth Forms
			(C >= 0xFFE0 && C <= 0xFFE6)); // Fullwidth symbols
	}

	private static string TruncateWithEllipsis(string Text, int MaxWidth)
	{
		int CurrentWidth = 0;
		StringBuilder Sb = new();

		foreach (char C in Text)
		{
			int CharWidth = IsDoubleWidth(C) ? 2 : 1;
			if (CurrentWidth + CharWidth + 3 > MaxWidth) // Leave room for ...
			{
				Sb.Append("...");
				break;
			}
			Sb.Append(C);
			CurrentWidth += CharWidth;
		}
		return Sb.ToString();
	}
}
