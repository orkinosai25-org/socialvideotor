namespace SocialVideotor.Models;

public class RawClipJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = "anonymous";
    public string SourceFileName { get; set; } = string.Empty;
    public string SourceBlobPath { get; set; } = string.Empty;
    public string? SourceIngressBlobName { get; set; }
    public RawClipJobStatus Status { get; set; } = RawClipJobStatus.Queued;
    public string StatusMessage { get; set; } = "Queued";
    public int ProgressPercent { get; set; }
    public List<RawClip> Clips { get; set; } = new();
    public List<RawClipJobHistoryEntry> History { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
    public string ErrorMessage { get; set; } = string.Empty;
    public double VideoDurationSeconds { get; set; }
    public bool FfmpegAvailable { get; set; }
}

public enum RawClipJobStatus
{
    Queued,
    Uploading,
    Processing,
    Ready,
    Failed
}

public class RawClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string JobId { get; set; } = string.Empty;
    public int ClipNumber { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public double StartTimeSeconds { get; set; }
    public double EndTimeSeconds { get; set; }
    public double DurationSeconds => EndTimeSeconds - StartTimeSeconds;
    public string Format { get; set; } = "mp4";
    public string AspectRatio { get; set; } = "9:16";
    public string? Title { get; set; }
    public string? PurposeLabel { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class RawClipJobHistoryEntry
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public RawClipJobStatus Status { get; set; }
    public int ProgressPercent { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
