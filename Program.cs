using RssTracker;
using RssTracker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Load and validate settings
var settings = builder.Configuration.GetSection("RssTrackerSettings").Get<Settings>();
if (settings == null)
{
    throw new InvalidOperationException("RssTrackerSettings section is missing from appsettings.json");
}

// Validate required settings
if (settings.Subreddits.Length == 0)
{
    throw new InvalidOperationException("At least one subreddit must be configured");
}

if (settings.KeywordPatterns.Length == 0)
{
    throw new InvalidOperationException("At least one keyword pattern must be configured");
}

if (string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
{
    throw new InvalidOperationException("DiscordWebhookUrl must be configured");
}

// Validate regex patterns (fail-fast)
foreach (var pattern in settings.KeywordPatterns)
{
    try
    {
        _ = new System.Text.RegularExpressions.Regex(pattern);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Invalid regex pattern: {pattern}", ex);
    }
}

// Configure options
builder.Services.Configure<Settings>(builder.Configuration.GetSection("RssTrackerSettings"));

// Register services
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<SeenPostsStore>>();
    return new SeenPostsStore(settings.SeenPostsFilePath, settings.MaxSeenPostsFileSizeBytes, logger);
});

builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<KeywordMatcher>>();
    return new KeywordMatcher(settings.KeywordPatterns, logger);
});

builder.Services.AddHttpClient<RssFeedService>()
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddSingleton(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var logger = sp.GetRequiredService<ILogger<RssFeedService>>();
    return new RssFeedService(httpClient, settings.MaxRetryDelaySeconds, logger);
});

builder.Services.AddSingleton(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var logger = sp.GetRequiredService<ILogger<DiscordNotifier>>();
    return new DiscordNotifier(httpClient, settings.DiscordWebhookUrl, settings.MaxRetryDelaySeconds, logger);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
