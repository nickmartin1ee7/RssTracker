
namespace RssTracker;

public class Settings
{
    public const string Key = "RssTrackerSettings";

    public string[] Subreddits { get; set; } = [];
    public string[] KeywordPatterns { get; set; } = [];
    public string DiscordWebhookUrl { get; set; } = string.Empty;
    public int MaxRequestsPerMinute { get; set; } = 10; // Reddit enforced default
    public string SeenPostsFilePath { get; set; } = "seenposts.json";
    public long MaxSeenPostsFileSizeBytes { get; set; } = 31_457_280; // 30MB default
    public int MaxRetryDelaySeconds { get; set; } = 3600; // 1 hour default
}
