using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using RssTracker.Models;

namespace RssTracker.Services;

public partial class DiscordNotifier
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
        var (originalPost, comment) = ParseOriginalAndComment(item.Content);

        // Truncate each part to 1000 characters for Discord embed
        static string? Truncate(string? s) =>
            s?.Length > 1000
                ? string.Concat(s.AsSpan(0, 997), "...")
                : s;

        var originalTruncated = Truncate(originalPost);
        var commentTruncated = Truncate(comment);

        var titleType = item.Type == RssFeedItemType.Post ? "Post" : item.Type == RssFeedItemType.Comment ? "Comment" : "Item";
        var color = item.Type == RssFeedItemType.Post ? 0x5865F2 : item.Type == RssFeedItemType.Comment ? 0xEB459E : 0x2F3136;

        var embeds = new List<object>();

        if (!string.IsNullOrWhiteSpace(originalTruncated))
        {
            embeds.Add(new
            {
                title = $"🔔 Keyword Match: {titleType}",
                description = originalTruncated,
                url = item.Link,
                color,
                fields = new[]
                {
                    new { name = "Comment", value = $"{commentTruncated ?? "N/A"}", inline = false},
                    new { name = "Author", value = item.Author, inline = true },
                    new { name = "Matched Pattern", value = $"`{matchedPattern}`", inline = true },
                    new { name = "Timestamp", value = item.Timestamp == DateTime.MinValue
                        ? $"<t:{item.Timestamp.ToUnixTimeSeconds()}:R>"
                        : "N/A", inline = true }
                },
                footer = new { text = "RssTracker" },
                timestamp = item.Timestamp.ToString("o")
            });
        }

        return new { embeds = embeds.ToArray() };
    }

    private static (string originalPost, string? comment) ParseOriginalAndComment(string content)
    {
        const string SC_OFF = "<!-- SC_OFF -->";
        const string SC_ON = "<!-- SC_ON -->";

        var index = content.IndexOf(SC_OFF, StringComparison.Ordinal);
        if (index < 0)
        {
            // No marker; treat entire content as one section
            return (content.Trim(), string.Empty);
        }

        var original = content[..index].Trim();
        var after = content[(index + SC_OFF.Length)..];
        var endIndex = after.IndexOf(SC_ON, StringComparison.Ordinal);
        var commentSection = endIndex >= 0 ? after[..endIndex] : after;

        // Strip HTML tags and decode entities
        var withoutTags = ComentHtmlRegex().Replace(commentSection, string.Empty).Trim();
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return (original, decoded);
    }

    private static int CalculateExponentialBackoff(int attempt, int maxDelaySeconds)
    {
        // Exponential backoff: 2^attempt seconds, capped at maxDelaySeconds
        var delay = Math.Pow(2, attempt);
        return (int)Math.Min(delay, maxDelaySeconds);
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex ComentHtmlRegex();
}
