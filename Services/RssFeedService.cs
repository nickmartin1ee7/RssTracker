using System.ServiceModel.Syndication;
using System.Xml;
using RssTracker.Models;

namespace RssTracker.Services;

public class RssFeedService
{
    private readonly HttpClient _httpClient;
    private readonly int _maxRetryDelaySeconds;
    private readonly ILogger<RssFeedService> _logger;

    public RssFeedService(HttpClient httpClient, int maxRetryDelaySeconds, ILogger<RssFeedService> logger)
    {
        _httpClient = httpClient;
        _maxRetryDelaySeconds = maxRetryDelaySeconds;
        _logger = logger;
        
        // Set User-Agent to avoid Reddit blocking
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RssTracker/1.0)");
    }

    // Changed feedType from string to enum RssFeedItemType
    public async Task<List<RssFeedItem>> FetchFeedAsync(string subreddit, RssFeedItemType itemType)
    {
        if (itemType is not (RssFeedItemType.Post or RssFeedItemType.Comment))
        {
            throw new ArgumentOutOfRangeException(nameof(itemType), itemType, "Only Post and Comment are valid feed types.");
        }

        var url = itemType == RssFeedItemType.Post 
            ? $"https://www.reddit.com/r/{subreddit}/.rss"
            : $"https://www.reddit.com/r/{subreddit}/comments/.rss";

        var items = new List<RssFeedItem>();
        var attempt = 0;
        var maxAttempts = 5;

        while (attempt < maxAttempts)
        {
            try
            {
                _logger.LogDebug("Fetching {FeedType} feed for r/{Subreddit} (attempt {Attempt})", itemType, subreddit, attempt + 1);
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var xmlReader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(xmlReader);

                foreach (var item in feed.Items)
                {
                    var feedItem = new RssFeedItem
                    {
                        Id = item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? Guid.NewGuid().ToString(),
                        Author = item.Authors.FirstOrDefault()?.Name ?? "Unknown",
                        Content = GetContentFromItem(item),
                        Link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty,
                        Timestamp = item.PublishDate,
                        Type = itemType
                    };

                    items.Add(feedItem);
                }

                _logger.LogDebug("Successfully fetched {Count} items from {FeedType} feed for r/{Subreddit}", 
                    items.Count, itemType, subreddit);
                
                return items;
            }
            catch (HttpRequestException ex)
            {
                attempt++;
                var delay = CalculateExponentialBackoff(attempt, _maxRetryDelaySeconds);
                _logger.LogWarning(ex, "HTTP error fetching feed for r/{Subreddit} {FeedType}, attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s", 
                    subreddit, itemType, attempt, maxAttempts, delay);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error fetching feed for r/{Subreddit} {FeedType}", subreddit, itemType);
                break;
            }
        }

        return items;
    }

    private static string GetContentFromItem(SyndicationItem item)
    {
        var content = item.Summary?.Text ?? string.Empty;
        
        if (item.Content is TextSyndicationContent textContent)
        {
            content = textContent.Text;
        }

        // Also include title as it may contain keywords
        var title = item.Title?.Text ?? string.Empty;
        return $"{title} {content}".Trim();
    }

    private static int CalculateExponentialBackoff(int attempt, int maxDelaySeconds)
    {
        // Exponential backoff: 2^attempt seconds, capped at maxDelaySeconds
        var delay = Math.Pow(2, attempt);
        return (int)Math.Min(delay, maxDelaySeconds);
    }
}
