using System.Text.RegularExpressions;

namespace RssTracker.Services;

public class KeywordMatcher
{
    private readonly List<Regex> _compiledPatterns;
    private readonly string[] _originalPatterns;
    private readonly ILogger<KeywordMatcher> _logger;

    public KeywordMatcher(string[] patterns, ILogger<KeywordMatcher> logger)
    {
        _originalPatterns = patterns;
        _logger = logger;
        _compiledPatterns = new List<Regex>();

        foreach (var pattern in patterns)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                _compiledPatterns.Add(regex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to compile regex pattern: {Pattern}", pattern);
                throw new InvalidOperationException($"Invalid regex pattern: {pattern}", ex);
            }
        }

        _logger.LogInformation("Successfully compiled {Count} regex patterns", _compiledPatterns.Count);
    }

    public (bool hasMatch, string? matchedPattern) FindMatch(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (false, null);
        }

        for (int i = 0; i < _compiledPatterns.Count; i++)
        {
            try
            {
                if (_compiledPatterns[i].IsMatch(content))
                {
                    return (true, _originalPatterns[i]);
                }
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("Regex match timeout for pattern: {Pattern}", _originalPatterns[i]);
            }
        }

        return (false, null);
    }
}
