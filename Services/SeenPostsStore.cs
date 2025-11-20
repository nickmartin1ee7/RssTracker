using System.Collections.Concurrent;
using System.Text.Json;

namespace RssTracker.Services;

public class SeenPostsStore
{
    private readonly string _filePath;
    private readonly long _maxFileSizeBytes;
    private readonly ConcurrentDictionary<string, DateTime> _seenPosts;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<SeenPostsStore> _logger;

    public SeenPostsStore(string filePath, long maxFileSizeBytes, ILogger<SeenPostsStore> logger)
    {
        _filePath = filePath;
        _maxFileSizeBytes = maxFileSizeBytes;
        _logger = logger;
        _seenPosts = new ConcurrentDictionary<string, DateTime>();
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Seen posts file does not exist, starting fresh");
            return;
        }

        try
        {
            await _fileLock.WaitAsync();
            var json = await File.ReadAllTextAsync(_filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
            
            if (data != null)
            {
                foreach (var kvp in data)
                {
                    _seenPosts.TryAdd(kvp.Key, kvp.Value);
                }
                _logger.LogInformation("Loaded {Count} seen posts from {FilePath}", _seenPosts.Count, _filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading seen posts from {FilePath}", _filePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            await _fileLock.WaitAsync();

            var data = _seenPosts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            // Check if we need to prune
            if (jsonBytes.Length > _maxFileSizeBytes)
            {
                _logger.LogWarning("Seen posts file size ({Size} bytes) exceeds max ({Max} bytes), pruning oldest entries", 
                    jsonBytes.Length, _maxFileSizeBytes);
                
                // Remove oldest 25% of entries
                var sortedByDate = data.OrderBy(kvp => kvp.Value).ToList();
                var removeCount = sortedByDate.Count / 4;
                
                foreach (var entry in sortedByDate.Take(removeCount))
                {
                    _seenPosts.TryRemove(entry.Key, out _);
                }

                // Re-serialize after pruning
                data = _seenPosts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                
                _logger.LogInformation("Pruned {Count} entries, new size: {Size} bytes", removeCount, jsonBytes.Length);
            }

            await File.WriteAllBytesAsync(_filePath, jsonBytes);
            _logger.LogDebug("Saved {Count} seen posts to {FilePath}", _seenPosts.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving seen posts to {FilePath}", _filePath);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public bool IsSeenPost(string id) => _seenPosts.ContainsKey(id);

    public void MarkAsSeen(string id)
    {
        _seenPosts.TryAdd(id, DateTime.UtcNow);
    }
}
