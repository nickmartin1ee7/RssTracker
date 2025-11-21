using System.Text;
using System.Text.Json;
using RssTracker.Models;

namespace RssTracker.Services;

public class DiscordNotifier
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly int _maxRetryDelaySeconds;
    private readonly ILogger<DiscordNotifier> _logger;

    public DiscordNotifier(HttpClient httpClient, string webhookUrl, int maxRetryDelaySeconds, ILogger<DiscordNotifier> logger)
    {
        _httpClient = httpClient;
        _webhookUrl = webhookUrl;
        _maxRetryDelaySeconds = maxRetryDelaySeconds;
        _logger = logger;
    }

    public async Task SendNotificationAsync(RssFeedItem item, string matchedPattern)
    {
        var attempt = 0;
        var maxAttempts = 5;

        while (attempt < maxAttempts)
        {
            try
            {
                var payload = CreateWebhookPayload(item, matchedPattern);
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _logger.LogDebug("Sending Discord notification for {Type} by {Author} (attempt {Attempt})", 
                    item.Type, item.Author, attempt + 1);

                var response = await _httpClient.PostAsync(_webhookUrl, content);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Successfully sent Discord notification for {Type} by {Author}", item.Type, item.Author);
                return;
            }
            catch (HttpRequestException ex)
            {
                attempt++;
                var delay = CalculateExponentialBackoff(attempt, _maxRetryDelaySeconds);
                _logger.LogWarning(ex, "HTTP error sending Discord notification, attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s", 
                    attempt, maxAttempts, delay);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending Discord notification");
                break;
            }
        }

        _logger.LogError("Failed to send Discord notification after {MaxAttempts} attempts", maxAttempts);
    }

    private static object CreateWebhookPayload(RssFeedItem item, string matchedPattern)
    {
        // Truncate content to 1000 characters for Discord embed
        var contentPreview = item.Content.Length > 1000 
            ? string.Concat(item.Content.AsSpan(0, 997), "...")
            : item.Content;

        var titleType = item.Type == RssFeedItemType.Post ? "Post" : item.Type == RssFeedItemType.Comment ? "Comment" : "Item";
        var color = item.Type == RssFeedItemType.Post ? 0x5865F2 : item.Type == RssFeedItemType.Comment ? 0xEB459E : 0x2F3136;

        return new
        {
            embeds = new[]
            {
                new
                {
                    title = $"🔔 Keyword Match: {titleType}",
                    description = contentPreview,
                    url = item.Link,
                    color,
                    fields = new[]
                    {
                        new { name = "Author", value = item.Author, inline = true },
                        new { name = "Matched Pattern", value = $"`{matchedPattern}`", inline = true },
                        new { name = "Timestamp", value = $"<t:{item.Timestamp.ToUnixTimeSeconds()}:R>", inline = true }
                    },
                    footer = new
                    {
                        text = "RssTracker"
                    },
                    timestamp = item.Timestamp.ToString("o")
                }
            }
        };
    }

    private static int CalculateExponentialBackoff(int attempt, int maxDelaySeconds)
    {
        // Exponential backoff: 2^attempt seconds, capped at maxDelaySeconds
        var delay = Math.Pow(2, attempt);
        return (int)Math.Min(delay, maxDelaySeconds);
    }
}
