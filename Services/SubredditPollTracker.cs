using System.Collections.Concurrent;

namespace RssTracker.Services;

public class SubredditPollTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _lastPollTimes = new();

    public void Initialize(IEnumerable<string> subreddits)
    {
        foreach (var s in subreddits)
        {
            _lastPollTimes.TryAdd(s, DateTime.MinValue);
        }
    }

    public void MarkPolled(string subreddit)
    {
        _lastPollTimes[subreddit] = DateTime.UtcNow;
    }

    public IReadOnlyList<string> GetOldestBatch(int remainingCapacity, int requestsPerSubreddit = 2)
    {
        if (remainingCapacity < requestsPerSubreddit)
        {
            return [];
        }

        var maxSubreddits = remainingCapacity / requestsPerSubreddit;
        return [.. _lastPollTimes
            .OrderBy(kvp => kvp.Value)
            .Take(maxSubreddits)
            .Select(kvp => kvp.Key)];
    }

    public bool IsEmpty => _lastPollTimes.IsEmpty;

    public string? GetOldest()
    {
        if (_lastPollTimes.IsEmpty) return null;
        return _lastPollTimes.OrderBy(kvp => kvp.Value).First().Key;
    }
}
