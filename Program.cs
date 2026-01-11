using System.Text;

HostApplicationBuilder Builder = Host.CreateApplicationBuilder(args);

// Disable default console logging (we have TUI)
Builder.Logging.ClearProviders();

// 1. Channel for producer/consumer pattern between Crawler and Fetcher
Channel<string> HashChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
{
	SingleReader = false,
	SingleWriter = false
});

Builder.Services.AddSingleton(HashChannel.Writer);
Builder.Services.AddSingleton(HashChannel.Reader);

// 2. Database context
Builder.Services.AddDbContext<TorrentContext>();

// 3. Background services
Builder.Services.AddHostedService<DhtCrawler>();
Builder.Services.AddHostedService<MetadataFetcher>();
Builder.Services.AddHostedService<ConsoleRenderer>();

IHost App = Builder.Build();

// 4. Initialize database with WAL mode (critical for concurrent read/write)
using (IServiceScope Scope = App.Services.CreateScope())
{
	TorrentContext Db = Scope.ServiceProvider.GetRequiredService<TorrentContext>();
	Db.Database.EnsureCreated();
	Db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
	Db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
	Db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
}

await App.RunAsync();
