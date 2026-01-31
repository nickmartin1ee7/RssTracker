using System.Threading;
using RssTracker.Models;

namespace RssTracker.Services;

public class RateLimitMonitor
{
    private RateLimitSnapshot? _snapshot;
    private readonly object _lock = new();

    public void Update(RateLimitSnapshot? snapshot)
    {
        if (snapshot == null) return;
        lock (_lock)
        {
            _snapshot = snapshot;
        }
    }

    public RateLimitSnapshot? GetCurrent()
    {
        lock (_lock)
        {
            return _snapshot;
        }
    }

    public TimeSpan ComputeSpacing(int requestsPerSubreddit, int maxRequestsPerMinute)
    {
        // Calculate user-configured minimum spacing
        var configuredPollsPerMinute = maxRequestsPerMinute / (double)requestsPerSubreddit;
        var configuredSpacingSeconds = configuredPollsPerMinute > 0 ? 60.0 / configuredPollsPerMinute : 60.0;

        var snap = GetCurrent();
        if (snap == null || snap.ResetSeconds <= 0 || snap.Remaining <= 0)
        {
            return TimeSpan.FromSeconds(Math.Max(configuredSpacingSeconds, 1));
        }

        var pollsRemaining = snap.Remaining / requestsPerSubreddit;
        if (pollsRemaining <= 0)
        {
            // Wait until reset, but respect configured minimum
            return TimeSpan.FromSeconds(Math.Max(snap.ResetSeconds, configuredSpacingSeconds));
        }

        var rateLimitSpacingSeconds = snap.ResetSeconds / Math.Max(1.0, pollsRemaining);
        
        // Use the SLOWER of: user-configured limit OR Reddit's rate limit
        var spacingSeconds = Math.Max(rateLimitSpacingSeconds, configuredSpacingSeconds);
        if (spacingSeconds < 1) spacingSeconds = 1; // floor
        return TimeSpan.FromSeconds(spacingSeconds);
    }

    public bool CanSchedule(int requestsNeeded)
    {
        var snap = GetCurrent();
        if (snap == null) return true; // optimistic until first headers
        return snap.Remaining >= requestsNeeded;
    }
}
