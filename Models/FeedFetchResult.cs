namespace RssTracker.Models;

public class FeedFetchResult
{
    public List<RssFeedItem> Items { get; }
    public RateLimitSnapshot? RateLimit { get; }

    public FeedFetchResult(List<RssFeedItem> items, RateLimitSnapshot? rateLimit)
    {
        Items = items;
        RateLimit = rateLimit;
    }
}