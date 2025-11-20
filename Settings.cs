namespace RssTracker;

public class Settings
{
    public string[] Subreddits { get; set; } = Array.Empty<string>();
    public string[] KeywordPatterns { get; set; } = Array.Empty<string>();
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 300;
    public string SeenPostsFilePath { get; set; } = "seenposts.json";
    public long MaxSeenPostsFileSizeBytes { get; set; } = 1048576; // 1MB default
    public int MaxRetryDelaySeconds { get; set; } = 21600; // 6 hours default
}
