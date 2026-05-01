using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IVideoIndexerService
{
    Task<string> UploadVideoAsync(string videoUrl, string name, CancellationToken cancellationToken = default);
    Task<VideoAnalysisResult> GetVideoIndexAsync(string videoId, CancellationToken cancellationToken = default);
    Task<bool> IsVideoReadyAsync(string videoId, CancellationToken cancellationToken = default);
}

public class VideoAnalysisResult
{
    public string VideoId { get; set; } = string.Empty;
    public string Transcript { get; set; } = string.Empty;
    public double Duration { get; set; }
    public List<VideoClip> SuggestedClips { get; set; } = new();
    public string State { get; set; } = string.Empty;
}
