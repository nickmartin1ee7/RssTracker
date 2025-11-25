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

    public TimeSpan ComputeSpacing(int requestsPerSubreddit, int fallbackMaxRequestsPerMinute)
    {
        var snap = GetCurrent();
        if (snap == null || snap.ResetSeconds <= 0 || snap.Remaining <= 0)
        {
            var pollsPerMinute = fallbackMaxRequestsPerMinute / requestsPerSubreddit;
            if (pollsPerMinute <= 0) return TimeSpan.FromSeconds(60);
            var spacingSecondsFallback = 60.0 / pollsPerMinute;
            return TimeSpan.FromSeconds(spacingSecondsFallback);
        }

        var pollsRemaining = snap.Remaining / requestsPerSubreddit;
        if (pollsRemaining <= 0)
        {
            // Previously returned possibly 0s; enforce a minimum 1s to avoid tight log loop
            return TimeSpan.FromSeconds(Math.Max(fallbackMaxRequestsPerMinute / 2 , snap.ResetSeconds));
        }
        var spacingSeconds = snap.ResetSeconds / Math.Max(1.0, pollsRemaining);
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
