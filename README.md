# DHT Scraper

A world-scale BitTorrent DHT scraper that indexes torrent metadata.

## Requirements

- .NET 10 SDK
- Linux (Ubuntu 22.04+ recommended)

## Quick Start (Linux)

### 1. Install .NET 10 SDK

```bash
# Add Microsoft package repo
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Install SDK
sudo apt update
sudo apt install -y dotnet-sdk-10.0
```

### 2. Clone and Build

```bash
git clone https://github.com/YOUR_USERNAME/LoveRats.DhtScrape.git
cd LoveRats.DhtScrape

dotnet restore
dotnet build
```

### 3. Run

```bash
dotnet run
```

The scraper will:
- Bootstrap from DHT routers on UDP port 6881
- Listen for metadata requests on TCP port 55555
- Create `dht_index.db` in the current directory
- Log `[INDEXED] <torrent name>` as torrents are discovered

### 4. Query the Database

```bash
sqlite3 dht_index.db "SELECT COUNT(*) FROM Torrents;"
sqlite3 dht_index.db "SELECT Name, TotalSizeBytes FROM Torrents ORDER BY DiscoveredAtUtc DESC LIMIT 10;"
```

## Ports Used

| Port | Protocol | Purpose |
|------|----------|---------|
| 6881 | UDP | DHT KRPC queries |
| 55555 | TCP | BEP 009 metadata exchange |

Ensure these ports are open in your firewall for full functionality.

## Running as a Service (systemd)

```bash
sudo nano /etc/systemd/system/dhtscraper.service
```

```ini
[Unit]
Description=DHT Scraper
After=network.target

[Service]
Type=simple
User=YOUR_USER
WorkingDirectory=/path/to/LoveRats.DhtScrape
ExecStart=/usr/bin/dotnet run --project DhtScraper.csproj
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable dhtscraper
sudo systemctl start dhtscraper
sudo journalctl -u dhtscraper -f  # View logs
```
