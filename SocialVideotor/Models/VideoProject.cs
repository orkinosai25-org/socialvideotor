namespace SocialVideotor.Models;

public class VideoProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string VideoIndexerId { get; set; } = string.Empty;
    public List<VideoClip> Clips { get; set; } = new();
    public List<SocialPlatform> TargetPlatforms { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;
    public string Transcript { get; set; } = string.Empty;
    public double VideoDuration { get; set; }
}

public enum ProjectStatus
{
    Draft,
    Analyzing,
    Ready,
    Exporting,
    Completed
}
