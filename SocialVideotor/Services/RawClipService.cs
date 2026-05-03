using System.Diagnostics;
using System.Globalization;
using SocialVideotor.Models;

namespace SocialVideotor.Services;

public class RawClipService : IRawClipService, IDisposable
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<RawClipService> _logger;
    private readonly Dictionary<string, RawClipJob> _jobs = new();
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// Files are stored outside wwwroot in a non-public directory and served
    /// only through the controlled /api/clips/{jobId}/{filename} endpoint.
    /// </summary>
    private string DataRoot => Path.Combine(_env.ContentRootPath, "data", "uploads");

    public RawClipService(IWebHostEnvironment env, ILogger<RawClipService> logger)
    {
        _env = env;
        _logger = logger;
        // Sweep expired jobs immediately on startup, then every hour
        _cleanupTimer = new Timer(_ => CleanupExpiredJobs(), null, TimeSpan.Zero, TimeSpan.FromHours(1));
    }

    public async Task<RawClipJob> StartProcessingAsync(Stream videoStream, string filename, long fileSize, CancellationToken cancellationToken = default)
    {
        var job = new RawClipJob
        {
            OriginalFilename = filename,
            Status = RawClipJobStatus.Uploading,
            StatusMessage = "Saving uploaded video…",
            FfmpegAvailable = IsFfmpegAvailable()
        };

        lock (_jobs)
            _jobs[job.Id] = job;

        // Save the stream while the caller's CancellationToken is still valid;
        // subsequent FFmpeg work runs unattended on a pool thread.
        _ = Task.Run(() => SaveAndProcessAsync(job, videoStream, fileSize, cancellationToken), CancellationToken.None);

        return job;
    }

    public RawClipJob? GetJob(string jobId)
    {
        lock (_jobs)
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    public IEnumerable<RawClipJob> GetAllJobs()
    {
        lock (_jobs)
            return _jobs.Values.ToList();
    }

    public void DeleteJob(string jobId)
    {
        string? dir = null;
        lock (_jobs)
        {
            if (_jobs.Remove(jobId))
                dir = GetJobDirectory(jobId);
        }

        if (dir == null) return; // already deleted or unknown
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete files for job {JobId}", jobId);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task SaveAndProcessAsync(RawClipJob job, Stream videoStream, long fileSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var jobDir = GetJobDirectory(job.Id);
            Directory.CreateDirectory(jobDir);
            var sourcePath = Path.Combine(jobDir, "source.mp4");

            // 1. Save uploaded file (honours caller's CancellationToken)
            await SaveFileAsync(job, videoStream, sourcePath, fileSize, cancellationToken);

            // 2. Get video duration
            UpdateJob(job, j =>
            {
                j.Status = RawClipJobStatus.Processing;
                j.StatusMessage = "Analysing video duration…";
                j.ProgressPercent = 5;
            });

            double duration = job.FfmpegAvailable
                ? await GetVideoDurationAsync(sourcePath)
                : 213; // mock duration when FFmpeg is unavailable

            if (duration <= 0) duration = 60;
            UpdateJob(job, j => j.VideoDuration = duration);

            // 3. Calculate clip strategy
            var (clipCount, clipDuration) = CalculateClipStrategy(duration);

            // 4. Generate clips
            var clips = new List<RawClip>();
            var spacing = clipCount > 1 ? (duration - clipDuration) / (clipCount - 1) : 0;

            for (int i = 0; i < clipCount; i++)
            {
                var startTime = i * spacing;
                var endTime = Math.Min(startTime + clipDuration, duration);
                var clipFilename = $"clip-{i + 1}.mp4";
                var clipPath = Path.Combine(jobDir, clipFilename);

                // Clips are served via the controlled download endpoint (not wwwroot)
                var clipApiUrl = $"/api/clips/{job.Id}/{clipFilename}";
                var sourceApiUrl = $"/api/clips/{job.Id}/source.mp4";

                UpdateJob(job, j =>
                {
                    j.StatusMessage = $"Generating clip {i + 1} of {clipCount}…";
                    j.ProgressPercent = 10 + (int)((double)i / clipCount * 85);
                });

                bool extracted = false;
                if (job.FfmpegAvailable)
                    extracted = await ExtractClipAsync(sourcePath, clipPath, startTime, endTime - startTime);

                clips.Add(new RawClip
                {
                    ClipNumber = i + 1,
                    StartTime = startTime,
                    EndTime = endTime,
                    // When FFmpeg is available point to the real clip; otherwise use
                    // the source video with a time fragment for in-browser preview.
                    PreviewUrl = extracted ? clipApiUrl : $"{sourceApiUrl}#t={startTime:F0},{endTime:F0}",
                    DownloadUrl = extracted ? clipApiUrl : sourceApiUrl
                });
            }

            UpdateJob(job, j =>
            {
                j.Clips = clips;
                j.Status = RawClipJobStatus.Ready;
                j.StatusMessage = $"{clips.Count} clips ready";
                j.ProgressPercent = 100;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing raw clip job {JobId}", job.Id);
            UpdateJob(job, j =>
            {
                j.Status = RawClipJobStatus.Failed;
                j.ErrorMessage = ex.Message;
                j.StatusMessage = "Processing failed";
            });
        }
    }

    private static async Task SaveFileAsync(RawClipJob job, Stream source, string destPath, long totalSize, CancellationToken cancellationToken = default)
    {
        const int bufferSize = 81920; // 80 KB
        await using var dest = File.Create(destPath);
        var buffer = new byte[bufferSize];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (totalSize > 0)
            {
                var pct = (int)((double)written / totalSize * 95);
                UpdateJob(job, j =>
                {
                    j.ProgressPercent = pct;
                    j.StatusMessage = $"Uploading… {pct}%";
                });
            }
        }
    }

    private static (int count, double clipDuration) CalculateClipStrategy(double totalDuration)
    {
        var count = (int)Math.Clamp(Math.Floor(totalDuration / 30), 5, 10);
        var clipDuration = Math.Clamp(totalDuration / count, 15, 60);
        return (count, clipDuration);
    }

    private async Task<double> GetVideoDurationAsync(string videoPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("ffprobe",
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed; falling back to mock duration");
            return 0;
        }
    }

    private async Task<bool> ExtractClipAsync(string inputPath, string outputPath, double start, double duration)
    {
        try
        {
            var args = string.Format(CultureInfo.InvariantCulture,
                "-y -ss {0} -i \"{1}\" -t {2} " +
                "-vf \"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920\" " +
                "-c:v libx264 -preset fast -crf 23 -movflags +faststart " +
                "-c:a aac -b:a 128k \"{3}\"",
                start, inputPath, duration, outputPath);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("ffmpeg", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg clip extraction failed for {Output}", outputPath);
            return false;
        }
    }

    private static bool IsFfmpegAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("ffmpeg", "-version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void CleanupExpiredJobs()
    {
        List<string> expired;
        lock (_jobs)
            expired = _jobs.Values
                .Where(j => j.ExpiresAt <= DateTime.UtcNow)
                .Select(j => j.Id)
                .ToList();

        foreach (var id in expired)
        {
            _logger.LogInformation("Auto-deleting expired clip job {JobId}", id);
            DeleteJob(id);
        }
    }

    private string GetJobDirectory(string jobId)
    {
        // Defence-in-depth: only allow well-formed GUIDs as directory names
        if (!Guid.TryParse(jobId, out _))
            throw new ArgumentException("Invalid job ID format.", nameof(jobId));
        return Path.Combine(DataRoot, jobId);
    }

    private static void UpdateJob(RawClipJob job, Action<RawClipJob> update)
    {
        lock (job)
            update(job);
    }
}
