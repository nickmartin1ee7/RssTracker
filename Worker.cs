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
        var pollsExecuted = 0;
        const int requestsPerSubreddit = 2; // posts + comments

        while (!stoppingToken.IsCancellationRequested)
        {
            // Check if window expired
            var timeSinceWindowStart = DateTime.UtcNow - windowStart;
            if (timeSinceWindowStart >= TimeSpan.FromSeconds(60))
            {
                // Reset window
                windowStart = DateTime.UtcNow;
                requestCount = 0;
                pollsExecuted = 0;
                _logger.LogDebug("Window reset at {Time}", windowStart);
            }

            var remainingCapacity = Settings.MaxRequestsPerMinute - requestCount;

            // If no capacity left in current window, wait until next window
            if (remainingCapacity < requestsPerSubreddit)
            {
                var until = windowStart.AddMinutes(1) - DateTime.UtcNow;
                if (until > TimeSpan.Zero)
                {
                    _logger.LogDebug("Window exhausted (used {Used}/{Max}); sleeping {Sleep} until next window", 
                        requestCount, Settings.MaxRequestsPerMinute, until);
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
                pollsExecuted = 0;
                continue;
            }

            // Get next subreddit to poll
            var batch = _pollTracker.GetOldestBatch(requestsPerSubreddit, requestsPerSubreddit);
            if (batch.Count == 0)
            {
                // No subreddits available; short delay and continue
                _logger.LogDebug("No subreddits available to poll; waiting 5 seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch
                {
                    // Ignore cancellation
                }
                continue;
            }

            var subreddit = batch[0];

            // Calculate target spacing between polls
            var maxPollsPerWindow = Settings.MaxRequestsPerMinute / requestsPerSubreddit;
            var spacing = 60.0 / maxPollsPerWindow; // spacing in seconds (double precision)
            
            // Calculate scheduled time for this poll
            var scheduledTime = windowStart.AddSeconds(pollsExecuted * spacing);
            var now = DateTime.UtcNow;
            
            // If we're ahead of schedule, delay until scheduled time
            if (now < scheduledTime)
            {
                var delayTime = scheduledTime - now;
                _logger.LogDebug("Delaying {Delay}ms until scheduled poll time for r/{Subreddit}", 
                    delayTime.TotalMilliseconds, subreddit);
                try
                {
                    await Task.Delay(delayTime, stoppingToken);
                }
                catch
                {
                    // Ignore cancellation
                }
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Poll the subreddit
            var currentPollNumber = pollsExecuted + 1;
            await PollSubredditAsync(subreddit, currentPollNumber, maxPollsPerWindow, stoppingToken);
            requestCount += requestsPerSubreddit;
            pollsExecuted++;
            _pollTracker.MarkPolled(subreddit);
            
            _logger.LogInformation("Polled r/{Subreddit}; {CurrentPoll}/{TotalPolls} polls in window at {Spacing}s spacing", 
                subreddit, currentPollNumber, maxPollsPerWindow, spacing);
        }

        await _seenPostsStore.SaveAsync();
        _logger.LogInformation("RssTracker Worker shutting down");
    }

    private async Task PollSubredditAsync(string subreddit, int currentPollNumber, int totalPollsThisWindow, CancellationToken stoppingToken)
    {
        try
        {
            var postItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Post);
            var commentItems = await _rssFeedService.FetchFeedAsync(subreddit, RssFeedItemType.Comment);

            var allItems = postItems.Concat(commentItems).ToList();
            _logger.LogDebug("Fetched {Count} total items from r/{Subreddit} (Poll {CurrentPoll}/{TotalPolls})", 
                allItems.Count, subreddit, currentPollNumber, totalPollsThisWindow);

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
                    _logger.LogInformation("Match found in {Type} by {Author} on r/{Subreddit} - Pattern: {Pattern} (Poll {CurrentPoll}/{TotalPolls})",
                        item.Type, item.Author, subreddit, matchedPattern, currentPollNumber, totalPollsThisWindow);
                    await _discordNotifier.SendNotificationAsync(item, matchedPattern);
                    _seenPostsStore.MarkAsSeen(item.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error polling feeds for r/{Subreddit} (Poll {CurrentPoll}/{TotalPolls})", 
                subreddit, currentPollNumber, totalPollsThisWindow);
        }
    }
}
