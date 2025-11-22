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
    private readonly SubredditPollTracker _pollTracker;

    private Settings Settings => _settingsMonitor.CurrentValue;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<Settings> settingsMonitor,
        SeenPostsStore seenPostsStore,
        RssFeedService rssFeedService,
        KeywordMatcher keywordMatcher,
        DiscordNotifier discordNotifier,
        SubredditPollTracker pollTracker)
    {
        _logger = logger;
        _settingsMonitor = settingsMonitor;
        _seenPostsStore = seenPostsStore;
        _rssFeedService = rssFeedService;
        _keywordMatcher = keywordMatcher;
        _discordNotifier = discordNotifier;
        _pollTracker = pollTracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RssTracker Worker starting up");
        _logger.LogInformation("Monitoring {Count} subreddits with {PatternCount} keyword patterns (MaxRequestsPerMinute={Max})",
            Settings.Subreddits.Length, Settings.KeywordPatterns.Length, Settings.MaxRequestsPerMinute);

        _pollTracker.Initialize(Settings.Subreddits);
        await _seenPostsStore.LoadAsync();

        var windowStart = DateTime.UtcNow;
        var requestCount = 0;
        const int requestsPerSubreddit = 2; // posts + comments
        var matchesThisWindow = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var remainingCapacity = Settings.MaxRequestsPerMinute - requestCount;

            if (remainingCapacity < requestsPerSubreddit)
            {
                var until = windowStart.AddMinutes(1) - DateTime.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    _logger.LogDebug("Window exhausted (used {Used}/{Max}); sleeping {Sleep} until next window", requestCount, Settings.MaxRequestsPerMinute, until);
                    try
                    {
                        await Task.Delay(until, stoppingToken);
                    }
                    catch
                    {
                        // Ignore cancellation
                    }
                }

                // Reset window
                windowStart = DateTime.UtcNow;
                requestCount = 0;
                matchesThisWindow = 0;
                continue;
            }

            var batch = _pollTracker.GetOldestBatch(remainingCapacity, requestsPerSubreddit);
            if (batch.Count == 0)
            {
                throw new InvalidOperationException("No subreddits available to poll, but capacity available.");
            }

            foreach (var subreddit in batch)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var matches = await PollSubredditAsync(subreddit, stoppingToken);
                matchesThisWindow += matches;
                requestCount += requestsPerSubreddit;
                _pollTracker.MarkPolled(subreddit);

                if (requestCount >= Settings.MaxRequestsPerMinute)
                {
                    break; // window will reset in next loop iteration
                }
            }
        }

        await _seenPostsStore.SaveAsync();
        _logger.LogInformation("RssTracker Worker shutting down");
    }

    private async Task<int> PollSubredditAsync(string subreddit, CancellationToken stoppingToken)
    {
        var matches = 0;
        try
        {
            var postItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Post);
            var commentItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Comment);

            var allItems = postItems.Concat(commentItems).ToList();
            _logger.LogDebug("Fetched {Count} total items from r/{Subreddit}", allItems.Count, subreddit);

            foreach (var item in allItems)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (_seenPostsStore.IsSeenPost(item.Id))
                {
                    continue;
                }

                var (hasMatch, matchedPattern) = _keywordMatcher.FindMatch(item.Content);
                if (hasMatch && matchedPattern != null)
                {
                    _logger.LogInformation("Match found in {Type} by {Author} on r/{Subreddit} - Pattern: {Pattern}",
                        item.Type, item.Author, subreddit, matchedPattern);
                    await _discordNotifier.SendNotificationAsync(item, matchedPattern);
                    matches++;
                    _seenPostsStore.MarkAsSeen(item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling feeds for r/{Subreddit}", subreddit);
        }
        return matches;
    }
}
