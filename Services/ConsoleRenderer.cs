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

		// Use simple ASCII box drawing - no emojis
		const string Cyan = "\e[1;36m";
		const string Yellow = "\e[1;33m";
		const string Green = "\e[1;32m";
		const string Magenta = "\e[1;35m";
		const string Blue = "\e[1;34m";
		const string Red = "\e[31m";
		const string Gray = "\e[90m";
		const string Reset = "\e[0m";

		StringBuilder Sb = new();
		Sb.Append("\e[H");

		// All lines are exactly 72 characters wide (70 content + 2 border)
		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}            {Yellow}* DHT SCRAPER - LIVE DASHBOARD *{Reset}                       {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}  Uptime: {Uptime:hh\\:mm\\:ss}                                                   {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}  {Green}[DHT CRAWLER]{Reset}                                                     {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Packets: {Yellow}{CrawlerPacketsSent,10}{Reset} sent   |   {Yellow}{CrawlerPacketsReceived,10}{Reset} recv        {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Queue:   {Yellow}{CrawlerQueueSize,10}{Reset}        |   Nodes: {Yellow}{CrawlerNodesDiscovered,10}{Reset}        {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Hashes:  {Yellow}{CrawlerHashesDiscovered,10}{Reset} found  |   {Yellow}{CrawlerUniqueHashes,10}{Reset} unique     {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}  {Magenta}[METADATA FETCHER]{Reset}                                                {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Received: {Yellow}{FetcherReceived,8}{Reset}        |   Active:  {Yellow}{FetcherActive,8}{Reset}            {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Success:  {Green}{FetcherSuccesses,8}{Reset}        |   Rate:    {Green}{SuccessRate,7:F1}%{Reset}            {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}    Timeout:  {Yellow}{FetcherTimeouts,8}{Reset}        |   Errors:  {Red}{FetcherErrors,8}{Reset}            {Cyan}|{Reset}");
		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Cyan}|{Reset}  {Blue}[RECENTLY INDEXED]{Reset}                                                 {Cyan}|{Reset}");

		string[] Recent = RecentIndexed.ToArray().TakeLast(MaxRecentItems).Reverse().ToArray();
		for (int i = 0; i < MaxRecentItems; i++)
		{
			if (i < Recent.Length)
			{
				string Name = TruncateAscii(Recent[i], 64);
				Sb.AppendLine($"{Cyan}|{Reset}    {Green}+{Reset} {Name,-64} {Cyan}|{Reset}");
			}
			else
			{
				Sb.AppendLine($"{Cyan}|{Reset}    {Gray}-{Reset} {"",64} {Cyan}|{Reset}");
			}
		}

		Sb.AppendLine($"{Cyan}+----------------------------------------------------------------------+{Reset}");
		Sb.AppendLine($"{Gray}Press Ctrl+C to stop{Reset}");

		Console.Write(Sb.ToString());
	}

	/// <summary>Truncates string to max ASCII display width, replacing wide chars.</summary>
	private static string TruncateAscii(string Text, int MaxWidth)
	{
		StringBuilder Result = new();
		int Width = 0;

		foreach (char C in Text)
		{
			// Replace non-ASCII with ?
			char OutChar = C < 32 || C > 126 ? '?' : C;

			if (Width >= MaxWidth - 3 && Width < MaxWidth)
			{
				Result.Append("...");
				break;
			}

			if (Width >= MaxWidth)
			{
				break;
			}

			Result.Append(OutChar);
			Width++;
		}

		return Result.ToString();
	}
}
