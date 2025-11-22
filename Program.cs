using RssTracker;
using RssTracker.Services;
using System.Text.RegularExpressions;

var builder = Host.CreateApplicationBuilder(args);

Settings? settings = LoadAndValidateSettings(builder);
ValidateRegex(settings);
ConfigureServices(builder, settings);

var host = builder.Build();
host.Run();

static Settings LoadAndValidateSettings(HostApplicationBuilder builder)
{
    var settings = builder.Configuration.GetSection(Settings.Key).Get<Settings>();
    if (settings == null)
    {
        throw new InvalidOperationException($"{nameof(Settings.Key)}section is missing from appsettings.json");
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

    return settings;
}

static void ValidateRegex(Settings settings)
{
    foreach (var pattern in settings.KeywordPatterns)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid regex pattern: {pattern}", ex);
        }
    }
}

static void ConfigureServices(HostApplicationBuilder builder, Settings settings)
{
    // Bind settings
    builder.Services.Configure<Settings>(builder.Configuration.GetSection(Settings.Key));

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

    builder.Services.AddSingleton<SubredditPollTracker>();

    // Worker service
    builder.Services.AddHostedService<Worker>();
}