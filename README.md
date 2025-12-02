# Reddit RSS Feed Tracker

A .NET Worker Service that monitors Reddit RSS feeds for specified subreddits, scans posts and comments for keyword matches using regex patterns, and sends notifications to Discord via webhook.

![Example Discord Screenshot](https://i.imgur.com/7qSs8dp.png)

## Features

- Monitor multiple subreddits for both posts and comments
- Flexible regex pattern matching for keywords
- Discord webhook notifications with rich embeds
  - Separates original post preview and comment when available (SC_OFF/SC_ON parsing)
- Persistent tracking of seen posts/comments to prevent duplicates
- Automatic file size management with pruning of old entries
- Exponential backoff retry logic for resilient operation
- Configurable request rate limiting (per minute)
- Fail-fast validation of regex patterns at startup
- Structured logging with Serilog (daily rolling files)

## Configuration

Edit `appsettings.json` to configure the tracker:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/RssTracker-.log",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": 10485760,
          "rollOnFileSizeLimit": true,
          "retainedFileCountLimit": 3
        }
      }
    ]
  },
  "RssTrackerSettings": {
    "Subreddits": [
      "programming",
      "csharp",
      "dotnet"
    ],
    "KeywordPatterns": [
      "[Bb]lazer",
      "[Aa][Ss][Pp]\\.[Nn][Ee][Tt]",
      "\\b(MAUI|maui)\\b",
      "[Ww]orker [Ss]ervice"
    ],
    "DiscordWebhookUrl": "https://discord.com/api/webhooks/YOUR_WEBHOOK_ID/YOUR_WEBHOOK_TOKEN",
    "MaxRequestsPerMinute": 10,
    "SeenPostsFilePath": "seenposts.json",
    "MaxSeenPostsFileSizeBytes": 1048576,
    "MaxRetryDelaySeconds": 21600
  }
}
```

### Configuration Options

| Setting | Section | Description | Default |
|---------|---------|-------------|---------|
| `Serilog.MinimumLevel.Default` | `Serilog` | Global minimum log level | `Information` |
| `Serilog.WriteTo[0].Args.path` | `Serilog` | Rolling log file path pattern | `logs/RssTracker-.log` |
| `Subreddits` | `RssTrackerSettings` | Array of subreddit names to monitor (without `r/` prefix) | Required |
| `KeywordPatterns` | `RssTrackerSettings` | Array of regex patterns to match against post/comment content | Required |
| `DiscordWebhookUrl` | `RssTrackerSettings` | Full Discord webhook URL for notifications | Required |
| `MaxRequestsPerMinute` | `RssTrackerSettings` | Global throttle for outbound requests (Reddit + Discord) | `10` |
| `SeenPostsFilePath` | `RssTrackerSettings` | Path to JSON file storing seen IDs | `seenposts.json` |
| `MaxSeenPostsFileSizeBytes` | `RssTrackerSettings` | Max size before pruning oldest entries | `1048576` (1MB) |
| `MaxRetryDelaySeconds` | `RssTrackerSettings` | Maximum delay cap for exponential backoff | `21600` (6 hours) |

### Regex Pattern Examples

- Case-insensitive matching: `[Bb]lazer` or `[Aa][Pp][Ii]`
- Word boundaries: `\b(MAUI|maui)\b` (matches "MAUI" but not "mauii")
- Multiple options: `(C#|F#|VB\.NET)`
- Escaped special chars: `ASP\.NET` (matches "ASP.NET")

## Getting a Discord Webhook URL

1. Open Discord and go to the channel where you want notifications
2. Click the gear icon (Edit Channel) → Integrations → Webhooks
3. Click "New Webhook" or "Create Webhook"
4. Copy the webhook URL and paste it into `appsettings.json`

## Running the Service

### Development
```powershell
dotnet run
```

### Production (Windows Service)
```powershell
# Publish the application
dotnet publish -c Release -o ./publish

# Install as Windows Service
sc.exe create RssTracker binPath="C:\path\to\publish\RssTracker.exe"
sc.exe start RssTracker
```

### Production (Linux systemd)
```bash
# Publish the application
dotnet publish -c Release -o ./publish

# Create systemd service file at /etc/systemd/system/rsstracker.service
[Unit]
Description=Reddit RSS Tracker
After=network.target

[Service]
Type=simple
WorkingDirectory=/path/to/publish
ExecStart=/usr/bin/dotnet /path/to/publish/RssTracker.dll
Restart=always

[Install]
WantedBy=multi-user.target

# Enable and start
sudo systemctl enable rsstracker
sudo systemctl start rsstracker
```

## How It Works

1. Startup: Loads previously seen posts from `seenposts.json` and validates all regex patterns
2. Loop: The service fetches RSS feeds for posts (`.rss`) and comments (`/comments/.rss`) for each subreddit, within `MaxRequestsPerMinute`
3. Filtering: Filters out previously seen posts/comments by ID
4. Matching: Scans content against all regex patterns
5. Notifications: Sends Discord notifications for matches
   - If the content contains `<!-- SC_OFF --> ... <!-- SC_ON -->`, two embeds are sent: one for the original post preview and one for the comment text
6. Persistence: Marks items as seen with timestamp and saves seen posts (auto-prunes when exceeding `MaxSeenPostsFileSizeBytes`)
7. Error Handling: Uses exponential backoff for Reddit API and Discord webhook failures (capped by `MaxRetryDelaySeconds`)

## Discord Notification Format

Notifications include:
- Title: Post or Comment indicator with emoji
- Description:
  - Original post preview (up to 1000 chars)
  - Comment body (up to 1000 chars), when present
- Author: Reddit username
- Matched Pattern: The regex pattern that triggered the match
- Timestamp: Relative time and ISO timestamp
- Link: Direct link to the Reddit post/comment
- Color: Blue for posts, pink for comments

## Logs

- Structured logging via Serilog to `logs/RssTracker-.log` with daily rolling, 10MB per file, and 3 retained files by default.
- Adjust log level in `Serilog.MinimumLevel`.

## Requirements

- .NET 10.0 or later
- Network access to reddit.com and Discord webhook endpoint

## Troubleshooting

No notifications appearing?
- Verify your Discord webhook URL is correct
- Check regex patterns are valid (service will fail-fast on startup if invalid)
- Ensure subreddit names are spelled correctly (without `r/` prefix)
- Check logs for errors under `logs/`

Too many notifications?
- Make regex patterns more specific with word boundaries: `\bkeyword\b`
- Reduce `MaxRequestsPerMinute` to pace the crawler

Service crashing?
- Validate regex patterns at https://regex101.com
- Check `seenposts.json` is not corrupted (delete it to start fresh)
- Ensure Discord webhook URL is accessible

## License

MIT
