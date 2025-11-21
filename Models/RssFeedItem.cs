namespace RssTracker.Models;

public class RssFeedItem
{
    public string Id { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public RssFeedItemType Type { get; set; } = RssFeedItemType.Unknown;
}
