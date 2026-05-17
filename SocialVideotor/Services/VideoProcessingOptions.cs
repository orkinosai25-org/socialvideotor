namespace SocialVideotor.Services;

public class VideoProcessingOptions
{
    public string StorageProvider { get; set; } = "Local";
    public string StorageRootPath { get; set; } = "data/uploads";
    public string TempDirectory { get; set; } = "data/tmp";
    public long MaxUploadSizeBytes { get; set; } = 500L * 1024 * 1024;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";
    public int MediaToolTimeoutSeconds { get; set; } = 120;
    public int WorkerConcurrency { get; set; } = 1;
}
