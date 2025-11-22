namespace RssTracker.Models;

public record RateLimitSnapshot(int Used, int Remaining, int ResetSeconds, DateTime CapturedAtUtc);
