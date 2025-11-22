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
    private readonly RateLimitMonitor _rateLimitMonitor;

    private Settings Settings => _settingsMonitor.CurrentValue;

    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<Settings> settingsMonitor,
        SeenPostsStore seenPostsStore,
        RssFeedService rssFeedService,
        KeywordMatcher keywordMatcher,
        DiscordNotifier discordNotifier,
        SubredditPollTracker pollTracker,
        RateLimitMonitor rateLimitMonitor)
    {
        _logger = logger;
        _settingsMonitor = settingsMonitor;
        _seenPostsStore = seenPostsStore;
        _rssFeedService = rssFeedService;
        _keywordMatcher = keywordMatcher;
        _discordNotifier = discordNotifier;
        _pollTracker = pollTracker;
        _rateLimitMonitor = rateLimitMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RssTracker Worker starting up");
        _logger.LogInformation("Monitoring {Count} subreddits with {PatternCount} keyword patterns", 
            Settings.Subreddits.Length, Settings.KeywordPatterns.Length);

        _pollTracker.Initialize(Settings.Subreddits);
        await _seenPostsStore.LoadAsync();

        const int requestsPerSubreddit = 2;
        DateTime lastPollTime = DateTime.UtcNow.AddSeconds(-5); // allow immediate first poll
        int pollSequence = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var spacing = _rateLimitMonitor.ComputeSpacing(requestsPerSubreddit, Settings.MaxRequestsPerMinute);
            var nextAllowed = lastPollTime + spacing;
            var snapshot = _rateLimitMonitor.GetCurrent();

            if (snapshot != null && snapshot.Remaining < requestsPerSubreddit)
            {
                _logger.LogWarning("Rate limit low Remaining={Remaining} < {Needed}; sleeping {Sleep}s until reset", 
                    snapshot.Remaining, requestsPerSubreddit, snapshot.ResetSeconds);
                try { await Task.Delay(TimeSpan.FromSeconds(snapshot.ResetSeconds), stoppingToken); } catch { }
                continue;
            }

            if (DateTime.UtcNow < nextAllowed)
            {
                var delay = nextAllowed - DateTime.UtcNow;
                _logger.LogDebug("Waiting {Delay} before next poll (Spacing={Spacing}s)", delay, spacing.TotalSeconds);
                try { await Task.Delay(delay, stoppingToken); } catch { }
            }

            if (stoppingToken.IsCancellationRequested) break;

            var subreddit = _pollTracker.GetOldest();
            if (subreddit == null)
            {
                _logger.LogWarning("No subreddits configured; sleeping 30s");
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch { }
                continue;
            }

            pollSequence++;
            await PollSubredditAsync(subreddit, pollSequence, spacing, stoppingToken);
            lastPollTime = DateTime.UtcNow;
            _pollTracker.MarkPolled(subreddit);
        }

        await _seenPostsStore.SaveAsync();
        _logger.LogInformation("RssTracker Worker shutting down");
    }

    private async Task PollSubredditAsync(string subreddit, int pollNumber, TimeSpan spacing, CancellationToken stoppingToken)
    {
        try
        {
            var postResult = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Post);
            _rateLimitMonitor.Update(postResult.RateLimit);
            var commentResult = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Comment);
            _rateLimitMonitor.Update(commentResult.RateLimit);

            var allItems = postResult.Items.Concat(commentResult.Items).ToList();
            var snap = _rateLimitMonitor.GetCurrent();
            if (snap != null)
            {
                _logger.LogInformation("Poll {Poll} r/{Subreddit} items={Count} Rate Used={Used} Rem={Rem} ResetIn={Reset}s Spacing={Spacing}s", 
                    pollNumber, subreddit, allItems.Count, snap.Used, snap.Remaining, snap.ResetSeconds, spacing.TotalSeconds);
            }
            else
            {
                _logger.LogInformation("Poll {Poll} r/{Subreddit} items={Count} (No rate data) Spacing={Spacing}s", 
                    pollNumber, subreddit, allItems.Count, spacing.TotalSeconds);
            }

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
                    _logger.LogInformation("Match r/{Subreddit} {Type} by {Author} Pattern={Pattern} Poll={Poll}", 
                        subreddit, item.Type, item.Author, matchedPattern, pollNumber);
                    await _discordNotifier.SendNotificationAsync(item, matchedPattern);
                    _seenPostsStore.MarkAsSeen(item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling r/{Subreddit} Poll={Poll}", subreddit, pollNumber);
        }
    }
}
