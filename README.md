# Reddit RSS Feed Tracker

A .NET Worker Service that monitors Reddit RSS feeds for specified subreddits, scans posts and comments for keyword matches using regex patterns, and sends notifications to Discord via webhook.

## Features

- ✅ Monitor multiple subreddits for both posts and comments
- ✅ Flexible regex pattern matching for keywords
- ✅ Discord webhook notifications with rich embeds
- ✅ Persistent tracking of seen posts/comments to prevent duplicates
- ✅ Automatic file size management with pruning of old entries
- ✅ Exponential backoff retry logic for resilient operation
- ✅ Configurable polling interval
- ✅ Fail-fast validation of regex patterns at startup

## Configuration

Edit `appsettings.json` to configure the tracker:

```json
{
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
    "PollIntervalSeconds": 300,
    "SeenPostsFilePath": "seenposts.json",
    "MaxSeenPostsFileSizeBytes": 1048576,
    "MaxRetryDelaySeconds": 21600
  }
}
```

### Configuration Options

| Setting | Description | Default |
|---------|-------------|---------|
| `Subreddits` | Array of subreddit names to monitor (without "r/" prefix) | Required |
| `KeywordPatterns` | Array of regex patterns to match against post/comment content | Required |
| `DiscordWebhookUrl` | Full Discord webhook URL for notifications | Required |
| `PollIntervalSeconds` | How often to check feeds (in seconds) | 300 (5 min) |
| `SeenPostsFilePath` | Path to JSON file storing seen post IDs | seenposts.json |
| `MaxSeenPostsFileSizeBytes` | Max size before pruning oldest entries | 1048576 (1MB) |
| `MaxRetryDelaySeconds` | Maximum delay for exponential backoff | 21600 (6 hours) |

### Regex Pattern Examples

- Case-insensitive matching: `[Bb]lazer` or `[Aa][Pp][Ii]`
- Word boundaries: `\\b(MAUI|maui)\\b` (matches "MAUI" but not "mauii")
- Multiple options: `(C#|F#|VB\\.NET)`
- Escaped special chars: `ASP\\.NET` (matches "ASP.NET")

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

1. **Startup**: Loads previously seen posts from `seenposts.json` and validates all regex patterns
2. **Polling Loop**: Every `PollIntervalSeconds`, the service:
   - Fetches RSS feeds for posts (`.rss`) and comments (`/comments/.rss`) for each subreddit
   - Filters out previously seen posts/comments by ID
   - Scans content against all regex patterns
   - Sends Discord notifications for matches
   - Marks items as seen with timestamp
   - Saves seen posts (with auto-pruning if file exceeds max size)
3. **Error Handling**: Uses exponential backoff for Reddit API and Discord webhook failures

## Discord Notification Format

Notifications include:
- **Title**: Post or Comment indicator with emoji
- **Description**: Content preview (up to 1000 characters)
- **Author**: Reddit username
- **Matched Pattern**: The regex pattern that triggered the match
- **Timestamp**: Relative time (e.g., "2 hours ago")
- **Link**: Direct link to the Reddit post/comment
- **Color**: Blue for posts, pink for comments

## Logs

The service uses standard .NET logging. Adjust log levels in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "RssTracker": "Debug"
    }
  }
}
```

## Requirements

- .NET 10.0 or later
- Network access to reddit.com and Discord webhook endpoint

## Troubleshooting

**No notifications appearing?**
- Verify your Discord webhook URL is correct
- Check regex patterns are valid (service will fail-fast on startup if invalid)
- Ensure subreddit names are spelled correctly (without "r/" prefix)
- Check logs for errors

**Too many notifications?**
- Make regex patterns more specific with word boundaries: `\\bkeyword\\b`
- Increase `PollIntervalSeconds` to reduce checking frequency

**Service crashing?**
- Validate regex patterns at https://regex101.com
- Check `seenposts.json` is not corrupted (delete it to start fresh)
- Ensure Discord webhook URL is accessible

## License

MIT
