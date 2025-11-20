using Microsoft.Extensions.Options;
using RssTracker.Services;

namespace RssTracker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly Settings _settings;
    private readonly SeenPostsStore _seenPostsStore;
    private readonly RssFeedService _rssFeedService;
    private readonly KeywordMatcher _keywordMatcher;
    private readonly DiscordNotifier _discordNotifier;

    public Worker(
        ILogger<Worker> logger,
        IOptions<Settings> settings,
        SeenPostsStore seenPostsStore,
        RssFeedService rssFeedService,
        KeywordMatcher keywordMatcher,
        DiscordNotifier discordNotifier)
    {
        _logger = logger;
        _settings = settings.Value;
        _seenPostsStore = seenPostsStore;
        _rssFeedService = rssFeedService;
        _keywordMatcher = keywordMatcher;
        _discordNotifier = discordNotifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RssTracker Worker starting up");
        _logger.LogInformation("Monitoring {Count} subreddits with {PatternCount} keyword patterns", 
            _settings.Subreddits.Length, _settings.KeywordPatterns.Length);

        // Load previously seen posts
        await _seenPostsStore.LoadAsync();

        var pollInterval = TimeSpan.FromSeconds(_settings.PollIntervalSeconds);
        _logger.LogInformation("Poll interval set to {Interval}", pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessFeedsAsync(stoppingToken);
                
                // Save seen posts after each cycle
                await _seenPostsStore.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing feeds");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }

        // Save on shutdown
        await _seenPostsStore.SaveAsync();
        _logger.LogInformation("RssTracker Worker shutting down");
    }

    private async Task ProcessFeedsAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting feed processing cycle");
        var totalMatches = 0;

        foreach (var subreddit in _settings.Subreddits)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                // Fetch both posts and comments
                var postItems = await _rssFeedService.FetchFeedAsync(subreddit, "posts");
                var commentItems = await _rssFeedService.FetchFeedAsync(subreddit, "comments");

                var allItems = postItems.Concat(commentItems).ToList();
                _logger.LogDebug("Fetched {Count} total items from r/{Subreddit}", allItems.Count, subreddit);

                foreach (var item in allItems)
                {
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Skip if we've seen this post/comment before
                    if (_seenPostsStore.IsSeenPost(item.Id))
                    {
                        continue;
                    }

                    // Check for keyword matches
                    var (hasMatch, matchedPattern) = _keywordMatcher.FindMatch(item.Content);

                    if (hasMatch && matchedPattern != null)
                    {
                        _logger.LogInformation("Match found in {Type} by {Author} on r/{Subreddit} - Pattern: {Pattern}", 
                            item.Type, item.Author, subreddit, matchedPattern);

                        // Send Discord notification
                        await _discordNotifier.SendNotificationAsync(item, matchedPattern);
                        totalMatches++;
                    }

                    // Mark as seen regardless of match
                    _seenPostsStore.MarkAsSeen(item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing feeds for r/{Subreddit}", subreddit);
            }
        }

        _logger.LogInformation("Feed processing cycle complete. Found {TotalMatches} new matches", totalMatches);
    }
}
