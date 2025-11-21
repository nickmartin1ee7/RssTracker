using Microsoft.Extensions.Options;
using RssTracker.Services;
using RssTracker.Models;

namespace RssTracker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<Settings> _settingsMonitor;
    private readonly SeenPostsStore _seenPostsStore;
    private readonly RssFeedService _rssFeedService;
    private readonly KeywordMatcher _keywordMatcher;
    private readonly DiscordNotifier _discordNotifier;

    private Settings Settings => _settingsMonitor.CurrentValue;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<Settings> settingsMonitor,
        SeenPostsStore seenPostsStore,
        RssFeedService rssFeedService,
        KeywordMatcher keywordMatcher,
        DiscordNotifier discordNotifier)
    {
        _logger = logger;
        _settingsMonitor = settingsMonitor;
        _seenPostsStore = seenPostsStore;
        _rssFeedService = rssFeedService;
        _keywordMatcher = keywordMatcher;
        _discordNotifier = discordNotifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RssTracker Worker starting up");
        _logger.LogInformation("Monitoring {Count} subreddits with {PatternCount} keyword patterns", 
            Settings.Subreddits.Length, Settings.KeywordPatterns.Length);

        await _seenPostsStore.LoadAsync();

        var pollInterval = TimeSpan.FromSeconds(Settings.PollIntervalSeconds);
        _logger.LogInformation("Poll interval set to {Interval}", pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessFeedsAsync(stoppingToken);
                await _seenPostsStore.SaveAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing feeds");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }

        await _seenPostsStore.SaveAsync();
        _logger.LogInformation("RssTracker Worker shutting down");
    }

    private async Task ProcessFeedsAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting feed processing cycle");

        var totalMatches = 0;

        foreach (var subreddit in Settings.Subreddits)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                var postItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Post);
                var commentItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Comment);

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

                        // Mark as seen only if matched to prevent duplicate notifications
                        _seenPostsStore.MarkAsSeen(item.Id);
                    }
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
