using System.Text.Json;
using SocialVideotor.Models;

namespace SocialVideotor.Services;

public class VideoIndexerService : IVideoIndexerService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<VideoIndexerService> _logger;

    private string AccountId => _configuration["AzureVideoIndexer:AccountId"] ?? string.Empty;
    private string Location => _configuration["AzureVideoIndexer:Location"] ?? "trial";

    public VideoIndexerService(HttpClient httpClient, IConfiguration configuration, ILogger<VideoIndexerService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> UploadVideoAsync(string videoUrl, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AccountId))
        {
            _logger.LogWarning("Azure Video Indexer not configured. Returning mock video ID.");
            return $"mock-{Guid.NewGuid()}";
        }

        try
        {
            var url = $"https://api.videoindexer.ai/{Location}/Accounts/{AccountId}/Videos?name={Uri.EscapeDataString(name)}&videoUrl={Uri.EscapeDataString(videoUrl)}&privacy=Private";
            var response = await _httpClient.PostAsync(url, null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(content);
            return json.RootElement.GetProperty("id").GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading video to Video Indexer");
            return $"mock-{Guid.NewGuid()}";
        }
    }

    public async Task<VideoAnalysisResult> GetVideoIndexAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AccountId) || videoId.StartsWith("mock-"))
        {
            return CreateMockAnalysisResult(videoId);
        }

        try
        {
            var url = $"https://api.videoindexer.ai/{Location}/Accounts/{AccountId}/Videos/{videoId}/Index";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseAnalysisResult(videoId, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting video index from Video Indexer");
            return CreateMockAnalysisResult(videoId);
        }
    }

    public async Task<bool> IsVideoReadyAsync(string videoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AccountId) || videoId.StartsWith("mock-"))
            return true;

        var result = await GetVideoIndexAsync(videoId, cancellationToken);
        return result.State == "Processed";
    }

    private VideoAnalysisResult CreateMockAnalysisResult(string videoId)
    {
        return new VideoAnalysisResult
        {
            VideoId = videoId,
            State = "Processed",
            Duration = 213,
            Transcript = "Welcome to this amazing video. Today we'll explore exciting content that will captivate your audience. The key highlights include incredible moments that are perfect for social media sharing.",
            SuggestedClips = new List<VideoClip>
            {
                new VideoClip { Title = "Opening Hook", StartTime = 0, EndTime = 15, Description = "Strong opening that grabs attention" },
                new VideoClip { Title = "Key Message", StartTime = 30, EndTime = 60, Description = "Core message delivery" },
                new VideoClip { Title = "Call to Action", StartTime = 195, EndTime = 213, Description = "Engaging call to action" }
            }
        };
    }

    private VideoAnalysisResult ParseAnalysisResult(string videoId, string json)
    {
        var result = new VideoAnalysisResult { VideoId = videoId };
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            result.State = root.TryGetProperty("state", out var stateEl) ? stateEl.GetString() ?? "" : "";

            if (root.TryGetProperty("videos", out var videos) && videos.GetArrayLength() > 0)
            {
                var video = videos[0];
                if (video.TryGetProperty("insights", out var insights))
                {
                    if (insights.TryGetProperty("duration", out var dur) && dur.TryGetProperty("seconds", out var sec))
                        result.Duration = sec.GetDouble();

                    var transcriptBuilder = new System.Text.StringBuilder();
                    if (insights.TryGetProperty("transcript", out var transcripts))
                    {
                        foreach (var t in transcripts.EnumerateArray())
                        {
                            if (t.TryGetProperty("text", out var text))
                                transcriptBuilder.Append(text.GetString()).Append(" ");
                        }
                    }
                    result.Transcript = transcriptBuilder.ToString().Trim();

                    if (insights.TryGetProperty("highlights", out var highlights))
                    {
                        foreach (var h in highlights.EnumerateArray())
                        {
                            var clip = new VideoClip();
                            if (h.TryGetProperty("text", out var ht)) clip.Title = ht.GetString() ?? "Highlight";
                            if (h.TryGetProperty("instances", out var instances) && instances.GetArrayLength() > 0)
                            {
                                var inst = instances[0];
                                if (inst.TryGetProperty("start", out var st)) clip.StartTime = ParseTimespan(st.GetString());
                                if (inst.TryGetProperty("end", out var et)) clip.EndTime = ParseTimespan(et.GetString());
                            }
                            result.SuggestedClips.Add(clip);
                        }
                    }
                }
            }
        }
        catch (Exception) { /* Return partial result */ }
        return result;
    }

    private double ParseTimespan(string? timeStr)
    {
        if (TimeSpan.TryParse(timeStr, out var ts)) return ts.TotalSeconds;
        return 0;
    }
}
