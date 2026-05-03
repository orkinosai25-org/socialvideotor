namespace SocialVideotor.Models;

public class RawClipJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalFilename { get; set; } = string.Empty;
    public RawClipJobStatus Status { get; set; } = RawClipJobStatus.Uploading;
    public string StatusMessage { get; set; } = "Uploading video…";
    public int ProgressPercent { get; set; }
    public List<RawClip> Clips { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Clips and job data are automatically deleted after this time (24 h from creation).</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public string ErrorMessage { get; set; } = string.Empty;
    public double VideoDuration { get; set; }
    public bool FfmpegAvailable { get; set; }
}

public enum RawClipJobStatus
{
    Uploading,
    Processing,
    Ready,
    Failed
}

public class RawClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public int ClipNumber { get; set; }
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Duration => EndTime - StartTime;
}
