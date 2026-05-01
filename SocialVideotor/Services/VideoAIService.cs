using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using SocialVideotor.Models;
using System.Text.Json;

namespace SocialVideotor.Services;

public class VideoAIService : IVideoAIService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<VideoAIService> _logger;
    private AzureOpenAIClient? _client;

    public VideoAIService(IConfiguration configuration, ILogger<VideoAIService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        InitializeClient();
    }

    private void InitializeClient()
    {
        var endpoint = _configuration["AzureOpenAI:Endpoint"];
        var apiKey = _configuration["AzureOpenAI:ApiKey"];

        if (!string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(apiKey))
        {
            _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }
    }

    public async Task<List<GenerationSuggestion>> GenerateSuggestionsAsync(string transcript, List<SocialPlatform> platforms, CancellationToken cancellationToken = default)
    {
        if (_client == null || string.IsNullOrWhiteSpace(transcript))
        {
            return CreateMockSuggestions(platforms);
        }

        try
        {
            var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
            var chatClient = _client.GetChatClient(deploymentName);

            var platformNames = string.Join(", ", platforms.Select(p => p.ToString()));
            var systemPrompt = "You are a social media video expert. Analyze video transcripts and suggest the best clip segments for social media platforms.";
            var userPrompt = $@"Analyze this video transcript and suggest 3-5 engaging clip segments for {platformNames}.
For each suggestion provide:
- title: short title
- description: why this segment is engaging
- startTime: estimated start time in seconds
- endTime: estimated end time in seconds
- hashtags: 5 relevant hashtags (without #)
- suggestedCaption: a short engaging caption
- engagementScore: score 0-1

Transcript: {transcript}

Respond with a JSON array of suggestions.";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var content = response.Value.Content[0].Text;
            return ParseSuggestions(content, platforms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating suggestions from OpenAI");
            return CreateMockSuggestions(platforms);
        }
    }

    public async Task<List<string>> GenerateHashtagsAsync(string content, SocialPlatform platform, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            return new List<string> { "viral", "trending", "socialmedia", platform.ToString().ToLower(), "video" };
        }

        try
        {
            var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
            var chatClient = _client.GetChatClient(deploymentName);

            var messages = new List<ChatMessage>
            {
                new UserChatMessage($"Generate 10 relevant hashtags for {platform} for this content: {content}. Return only a JSON array of strings without # symbols.")
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var text = response.Value.Content[0].Text;
            var cleaned = text.Trim().TrimStart('[').TrimEnd(']');
            return cleaned.Split(',').Select(h => h.Trim().Trim('"')).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating hashtags");
            return new List<string> { "viral", "trending", platform.ToString().ToLower() };
        }
    }

    public async Task<string> GenerateCaptionAsync(string clipDescription, SocialPlatform platform, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            return $"🎬 Amazing content for {platform}! Don't miss this! #viral #trending";
        }

        try
        {
            var deploymentName = _configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";
            var chatClient = _client.GetChatClient(deploymentName);

            var messages = new List<ChatMessage>
            {
                new UserChatMessage($"Write an engaging {platform} caption for this video clip: {clipDescription}. Keep it under 150 characters with emojis.")
            };

            var response = await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating caption");
            return $"🎬 Check out this amazing clip! Perfect for {platform}!";
        }
    }

    private List<GenerationSuggestion> ParseSuggestions(string json, List<SocialPlatform> platforms)
    {
        try
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                json = json.Substring(start, end - start + 1);
            }

            var suggestions = JsonSerializer.Deserialize<List<JsonElement>>(json) ?? new();
            return suggestions.Select(s => new GenerationSuggestion
            {
                Title = GetStringProp(s, "title"),
                Description = GetStringProp(s, "description"),
                StartTime = GetDoubleProp(s, "startTime"),
                EndTime = GetDoubleProp(s, "endTime"),
                SuggestedCaption = GetStringProp(s, "suggestedCaption"),
                EngagementScore = GetDoubleProp(s, "engagementScore"),
                Hashtags = GetStringList(s, "hashtags"),
                RecommendedPlatforms = platforms
            }).ToList();
        }
        catch
        {
            return CreateMockSuggestions(platforms);
        }
    }

    private string GetStringProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private double GetDoubleProp(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDouble(out var d) ? d : 0;

    private List<string> GetStringList(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var arr)) return new();
        try { return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList(); }
        catch { return new(); }
    }

    private List<GenerationSuggestion> CreateMockSuggestions(List<SocialPlatform> platforms)
    {
        return new List<GenerationSuggestion>
        {
            new GenerationSuggestion
            {
                Title = "Viral Hook Moment",
                Description = "The opening seconds have high energy and are perfect for stopping the scroll",
                StartTime = 0,
                EndTime = 15,
                EngagementScore = 0.92,
                Hashtags = new List<string> { "viral", "trending", "reels", "fyp", "socialmedia" },
                RecommendedPlatforms = platforms,
                SuggestedCaption = "🔥 You won't believe this! Watch till the end! #viral #trending"
            },
            new GenerationSuggestion
            {
                Title = "Key Takeaway",
                Description = "The most informative segment that provides clear value to viewers",
                StartTime = 45,
                EndTime = 90,
                EngagementScore = 0.85,
                Hashtags = new List<string> { "tips", "howto", "learn", "knowledge", "educational" },
                RecommendedPlatforms = platforms,
                SuggestedCaption = "💡 Here's what you need to know! Save this for later! #tips #howto"
            },
            new GenerationSuggestion
            {
                Title = "Emotional Peak",
                Description = "High emotional resonance segment that drives shares and saves",
                StartTime = 120,
                EndTime = 150,
                EngagementScore = 0.88,
                Hashtags = new List<string> { "inspiring", "emotional", "mustwatch", "share", "relatable" },
                RecommendedPlatforms = platforms,
                SuggestedCaption = "❤️ This hit different... Share if you felt this! #inspiring"
            }
        };
    }
}
