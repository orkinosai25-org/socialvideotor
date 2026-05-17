using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SocialVideotor.Models;

namespace SocialVideotor.Services;

public class RawClipService : IRawClipService, IDisposable
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v"
    };
    private static readonly JsonSerializerOptions PersistedJobSerializerOptions = new()
    {
        WriteIndented = true
    };
    private const int MinProcessTimeoutSeconds = 5;
    private const int MaxErrorLogLength = 500;

    private readonly IClipStorage _storage;
    private readonly ILogger<RawClipService> _logger;
    private readonly IOptions<VideoProcessingOptions> _options;
    private readonly Dictionary<string, RawClipJob> _jobs = new();
    private readonly object _jobsLock = new();
    private readonly Timer _cleanupTimer;
    private readonly string _jobsStatePath;

    public RawClipService(
        IWebHostEnvironment env,
        IClipStorage storage,
        IOptions<VideoProcessingOptions> options,
        ILogger<RawClipService> logger)
    {
        _storage = storage;
        _options = options;
        _logger = logger;

        _jobsStatePath = Path.Combine(env.ContentRootPath, "data", "jobs", "raw-clip-jobs.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_jobsStatePath)!);

        LoadJobs();
        RequeueInProgressJobs();

        _cleanupTimer = new Timer(_ => CleanupExpiredJobs(), null, TimeSpan.Zero, TimeSpan.FromHours(1));
    }

    public async Task<RawClipJob> StartProcessingAsync(Stream videoStream, string filename, long fileSize, string? contentType = null, string userId = "anonymous", CancellationToken cancellationToken = default)
    {
        ValidateUpload(filename, fileSize, contentType);

        var jobId = Guid.NewGuid().ToString("D");

        var job = new RawClipJob
        {
            Id = jobId,
            UserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId,
            SourceFileName = filename,
            SourceBlobPath = $"jobs/{jobId}/source.mp4",
            Status = RawClipJobStatus.Uploading,
            StatusMessage = "Saving uploaded video…",
            FfmpegAvailable = IsFfmpegAvailable(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };

        UpdateJob(job, j =>
        {
            j.History.Add(new RawClipJobHistoryEntry
            {
                Status = j.Status,
                ProgressPercent = j.ProgressPercent,
                Message = j.StatusMessage
            });
        }, persist: false);

        lock (_jobsLock)
        {
            _jobs[job.Id] = job;
            SaveJobsUnsafe();
        }

        _logger.LogInformation("Stage=UploadStart Job={JobId} User={UserId} File={File}", job.Id, job.UserId, filename);

        var sourcePath = _storage.GetAbsolutePath(job.SourceBlobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        await SaveFileAsync(job, videoStream, sourcePath, fileSize, _options.Value.MaxUploadSizeBytes, cancellationToken);

        UpdateJob(job, j =>
        {
            j.ProgressPercent = 0;
            j.Status = RawClipJobStatus.Queued;
            j.StatusMessage = "Queued for background processing";
            j.ErrorMessage = string.Empty;
            j.CompletedAtUtc = null;
            j.History.Add(new RawClipJobHistoryEntry
            {
                Status = j.Status,
                ProgressPercent = j.ProgressPercent,
                Message = j.StatusMessage
            });
        });

        _logger.LogInformation("Stage=UploadComplete Job={JobId} Path={BlobPath}", job.Id, job.SourceBlobPath);

        return Clone(job);
    }

    public RawClipJob? GetJob(string jobId)
    {
        lock (_jobsLock)
            return _jobs.TryGetValue(jobId, out var job) ? Clone(job) : null;
    }

    public IEnumerable<RawClipJob> GetAllJobs(string? userId = null)
    {
        lock (_jobsLock)
        {
            return _jobs.Values
                .Where(j => string.IsNullOrWhiteSpace(userId) || string.Equals(j.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(j => j.CreatedAtUtc)
                .Select(Clone)
                .ToList();
        }
    }

    public IEnumerable<RawClip> GetClips(string jobId, string? userId = null)
    {
        var job = GetJob(jobId);
        if (job == null) return Enumerable.Empty<RawClip>();
        if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
            return Enumerable.Empty<RawClip>();
        return job.Clips.Select(Clone).ToList();
    }

    public RawClipJobStatus? GetJobStatus(string jobId, string? userId = null)
    {
        var job = GetJob(jobId);
        if (job == null) return null;
        if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
            return null;
        return job.Status;
    }

    public Task<bool> RetryJobAsync(string jobId, string? userId = null, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId))
            return Task.FromResult(false);

        var safeJobId = parsedJobId.ToString("D");

        lock (_jobsLock)
        {
            if (!_jobs.TryGetValue(safeJobId, out var job)) return Task.FromResult(false);
            if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);
            if (job.Status != RawClipJobStatus.Failed) return Task.FromResult(false);

            job.Status = RawClipJobStatus.Queued;
            job.StatusMessage = "Retry queued";
            job.ProgressPercent = 0;
            job.ErrorMessage = string.Empty;
            job.CompletedAtUtc = null;
            job.UpdatedAtUtc = DateTime.UtcNow;
            job.History.Add(new RawClipJobHistoryEntry
            {
                Status = job.Status,
                ProgressPercent = job.ProgressPercent,
                Message = job.StatusMessage
            });
            SaveJobsUnsafe();
        }

        _logger.LogInformation("Stage=RetryQueued Job={JobId}", safeJobId);
        return Task.FromResult(true);
    }

    public async Task ProcessQueuedJobsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RawClipJob? job = null;
            lock (_jobsLock)
            {
                job = _jobs.Values
                    .Where(j => j.Status == RawClipJobStatus.Queued)
                    .OrderBy(j => j.CreatedAtUtc)
                    .FirstOrDefault();

                if (job != null)
                {
                    job.Status = RawClipJobStatus.Processing;
                    job.StatusMessage = "Starting background processing…";
                    job.ProgressPercent = Math.Max(job.ProgressPercent, 1);
                    job.UpdatedAtUtc = DateTime.UtcNow;
                    job.History.Add(new RawClipJobHistoryEntry
                    {
                        Status = job.Status,
                        ProgressPercent = job.ProgressPercent,
                        Message = job.StatusMessage
                    });
                    SaveJobsUnsafe();
                }
            }

            if (job == null)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            await ProcessJobAsync(job, cancellationToken);
        }
    }

    public void DeleteJob(string jobId)
    {
        lock (_jobsLock)
        {
            if (!_jobs.Remove(jobId))
                return;
            SaveJobsUnsafe();
        }

        try
        {
            _storage.DeletePrefix($"jobs/{jobId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete files for job {JobId}", jobId);
        }
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private async Task ProcessJobAsync(RawClipJob job, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Stage=ProcessStart Job={JobId}", job.Id);

            var sourcePath = _storage.GetAbsolutePath(job.SourceBlobPath);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source video not found.", sourcePath);

            UpdateJob(job, j =>
            {
                j.StatusMessage = "Analysing video duration…";
                j.ProgressPercent = 5;
            });

            var duration = job.FfmpegAvailable
                ? await GetVideoDurationAsync(sourcePath, cancellationToken)
                : 0;

            if (duration <= 0)
                duration = 60;

            UpdateJob(job, j => j.VideoDurationSeconds = duration);

            var (clipCount, clipDuration) = CalculateClipStrategy(duration);
            var clips = new List<RawClip>();
            var spacing = clipCount > 1 ? (duration - clipDuration) / (clipCount - 1) : 0;

            for (var i = 0; i < clipCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startTime = i * spacing;
                var endTime = Math.Min(startTime + clipDuration, duration);
                var clipBlobPath = $"jobs/{job.Id}/clip-{i + 1}.mp4";
                var clipPath = _storage.GetAbsolutePath(clipBlobPath);

                UpdateJob(job, j =>
                {
                    j.StatusMessage = $"Generating clip {i + 1} of {clipCount}…";
                    j.ProgressPercent = 10 + (int)((double)i / clipCount * 85);
                });

                var extracted = false;
                if (job.FfmpegAvailable)
                    extracted = await ExtractClipAsync(sourcePath, clipPath, startTime, endTime - startTime, cancellationToken);

                var sourceUrl = _storage.GetPublicPath(job.SourceBlobPath);
                var clipUrl = _storage.GetPublicPath(clipBlobPath);

                clips.Add(new RawClip
                {
                    JobId = job.Id,
                    ClipNumber = i + 1,
                    BlobPath = clipBlobPath,
                    StartTimeSeconds = startTime,
                    EndTimeSeconds = endTime,
                    Title = $"Clip {i + 1}",
                    PurposeLabel = "Heuristic",
                    PreviewUrl = extracted ? clipUrl : $"{sourceUrl}#t={startTime:F0},{endTime:F0}",
                    DownloadUrl = extracted ? clipUrl : sourceUrl
                });
            }

            UpdateJob(job, j =>
            {
                j.Clips = clips;
                j.Status = RawClipJobStatus.Ready;
                j.StatusMessage = $"{clips.Count} clips ready";
                j.ProgressPercent = 100;
                j.CompletedAtUtc = DateTime.UtcNow;
                j.History.Add(new RawClipJobHistoryEntry
                {
                    Status = j.Status,
                    ProgressPercent = j.ProgressPercent,
                    Message = j.StatusMessage
                });
            });

            _logger.LogInformation("Stage=ProcessComplete Job={JobId} Clips={ClipCount}", job.Id, clips.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage=ProcessFailed Job={JobId}", job.Id);
            UpdateJob(job, j =>
            {
                j.Status = RawClipJobStatus.Failed;
                j.ErrorMessage = ex.Message;
                j.StatusMessage = "Processing failed";
                j.CompletedAtUtc = DateTime.UtcNow;
                j.History.Add(new RawClipJobHistoryEntry
                {
                    Status = j.Status,
                    ProgressPercent = j.ProgressPercent,
                    Message = j.StatusMessage,
                    ErrorMessage = ex.Message
                });
            });
        }
    }

    private static async Task SaveFileAsync(
        RawClipJob job,
        Stream source,
        string destPath,
        long totalSize,
        long maxUploadSizeBytes,
        CancellationToken cancellationToken = default)
    {
        const int bufferSize = 81920;
        await using var dest = File.Create(destPath);
        var buffer = new byte[bufferSize];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;

            if (written > maxUploadSizeBytes)
                throw new InvalidOperationException($"File exceeds the configured max size of {maxUploadSizeBytes} bytes.");

            if (totalSize <= 0) continue;
            var pct = (int)Math.Clamp((double)written / totalSize * 95, 0, 95);
            lock (job)
            {
                if (pct != job.ProgressPercent)
                {
                    job.ProgressPercent = pct;
                    job.StatusMessage = $"Uploading… {pct}%";
                    job.UpdatedAtUtc = DateTime.UtcNow;
                }
            }
        }
    }

    private static (int count, double clipDuration) CalculateClipStrategy(double totalDuration)
    {
        var count = (int)Math.Clamp(Math.Floor(totalDuration / 30), 5, 10);
        var clipDuration = Math.Clamp(totalDuration / count, 15, 60);
        return (count, clipDuration);
    }

    private async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(_options.Value.FfprobePath,
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitCode = await WaitForProcessExitAsync(process, _options.Value.MediaToolTimeoutSeconds, cancellationToken);
            if (exitCode == null)
            {
                _logger.LogWarning("ffprobe timed out after {TimeoutSeconds}s for {VideoPath}", _options.Value.MediaToolTimeoutSeconds, videoPath);
                return 0;
            }

            var output = await outputTask;
            var error = await errorTask;
            if (exitCode != 0)
            {
                _logger.LogWarning("ffprobe failed for {VideoPath}. ExitCode={ExitCode}. Error={Error}", videoPath, exitCode, TrimForLog(error));
                return 0;
            }

            return double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed; falling back to default duration");
            return 0;
        }
    }

    private async Task<bool> ExtractClipAsync(string inputPath, string outputPath, double start, double duration, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var args = string.Format(CultureInfo.InvariantCulture,
                "-y -ss {0} -i \"{1}\" -t {2} " +
                "-vf \"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920\" " +
                "-c:v libx264 -preset fast -crf 23 -movflags +faststart " +
                "-c:a aac -b:a 128k \"{3}\"",
                start, inputPath, duration, outputPath);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(_options.Value.FfmpegPath, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exitCode = await WaitForProcessExitAsync(process, _options.Value.MediaToolTimeoutSeconds, cancellationToken);
            _ = await outputTask;
            var error = await errorTask;
            if (exitCode == null)
            {
                _logger.LogWarning("ffmpeg clip extraction timed out after {TimeoutSeconds}s for {Output}", _options.Value.MediaToolTimeoutSeconds, outputPath);
                return false;
            }

            if (exitCode != 0)
            {
                _logger.LogWarning("ffmpeg clip extraction failed for {Output}. ExitCode={ExitCode}. Error={Error}", outputPath, exitCode, TrimForLog(error));
                return false;
            }

            return File.Exists(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffmpeg clip extraction failed for {Output}", outputPath);
            return false;
        }
    }

    private bool IsFfmpegAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(_options.Value.FfmpegPath, "-version")
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

    private void ValidateUpload(string filename, long fileSize, string? contentType)
    {
        var extension = Path.GetExtension(filename);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedExtensions.Contains(extension))
            throw new InvalidOperationException("Unsupported file type. Allowed: .mp4, .mov, .m4v");

        if (!string.IsNullOrWhiteSpace(contentType) && !UserIdentityResolver.SupportedUploadContentTypes.Contains(contentType))
            throw new InvalidOperationException("Unsupported content type. Allowed: video/mp4, video/quicktime, video/x-m4v.");

        if (fileSize <= 0)
            throw new InvalidOperationException("The uploaded file is empty.");

        if (fileSize > _options.Value.MaxUploadSizeBytes)
            throw new InvalidOperationException($"File exceeds the configured max size of {_options.Value.MaxUploadSizeBytes} bytes.");
    }

    private void UpdateJob(RawClipJob job, Action<RawClipJob> update, bool persist = true)
    {
        lock (job)
        {
            update(job);
            job.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (!persist) return;
        lock (_jobsLock)
            SaveJobsUnsafe();
    }

    private void LoadJobs()
    {
        if (!File.Exists(_jobsStatePath))
        {
            _logger.LogInformation("No persisted raw clip jobs found at startup (first run or clean state).");
            return;
        }

        try
        {
            var json = File.ReadAllText(_jobsStatePath);
            var jobs = JsonSerializer.Deserialize<List<RawClipJob>>(json) ?? new List<RawClipJob>();
            lock (_jobsLock)
            {
                _jobs.Clear();
                foreach (var job in jobs)
                    _jobs[job.Id] = job;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load persisted raw clip jobs.");
        }
    }

    private void RequeueInProgressJobs()
    {
        lock (_jobsLock)
        {
            foreach (var job in _jobs.Values.Where(j => j.Status is RawClipJobStatus.Uploading or RawClipJobStatus.Processing))
            {
                job.Status = RawClipJobStatus.Queued;
                job.StatusMessage = "Recovered after restart; queued";
                job.ErrorMessage = string.Empty;
                job.CompletedAtUtc = null;
                job.UpdatedAtUtc = DateTime.UtcNow;
                job.History.Add(new RawClipJobHistoryEntry
                {
                    Status = job.Status,
                    ProgressPercent = job.ProgressPercent,
                    Message = job.StatusMessage
                });
            }

            SaveJobsUnsafe();
        }
    }

    private void CleanupExpiredJobs()
    {
        List<string> expired;
        lock (_jobsLock)
        {
            expired = _jobs.Values
                .Where(j => j.ExpiresAtUtc <= DateTime.UtcNow)
                .Select(j => j.Id)
                .ToList();
        }

        foreach (var id in expired)
        {
            _logger.LogInformation("Auto-deleting expired clip job {JobId}", id);
            DeleteJob(id);
        }
    }

    private void SaveJobsUnsafe()
    {
        var json = JsonSerializer.Serialize(_jobs.Values.OrderBy(j => j.CreatedAtUtc).ToList(), PersistedJobSerializerOptions);

        var tempPath = _jobsStatePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _jobsStatePath, true);
    }

    private static RawClipJob Clone(RawClipJob job)
    {
        return new RawClipJob
        {
            Id = job.Id,
            UserId = job.UserId,
            SourceFileName = job.SourceFileName,
            SourceBlobPath = job.SourceBlobPath,
            Status = job.Status,
            StatusMessage = job.StatusMessage,
            ProgressPercent = job.ProgressPercent,
            Clips = job.Clips.Select(Clone).ToList(),
            History = job.History.Select(h => new RawClipJobHistoryEntry
            {
                CreatedAtUtc = h.CreatedAtUtc,
                Status = h.Status,
                ProgressPercent = h.ProgressPercent,
                Message = h.Message,
                ErrorMessage = h.ErrorMessage
            }).ToList(),
            CreatedAtUtc = job.CreatedAtUtc,
            UpdatedAtUtc = job.UpdatedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            ExpiresAtUtc = job.ExpiresAtUtc,
            ErrorMessage = job.ErrorMessage,
            VideoDurationSeconds = job.VideoDurationSeconds,
            FfmpegAvailable = job.FfmpegAvailable
        };
    }

    private static RawClip Clone(RawClip clip)
    {
        return new RawClip
        {
            Id = clip.Id,
            JobId = clip.JobId,
            ClipNumber = clip.ClipNumber,
            BlobPath = clip.BlobPath,
            PreviewUrl = clip.PreviewUrl,
            DownloadUrl = clip.DownloadUrl,
            StartTimeSeconds = clip.StartTimeSeconds,
            EndTimeSeconds = clip.EndTimeSeconds,
            Format = clip.Format,
            AspectRatio = clip.AspectRatio,
            Title = clip.Title,
            PurposeLabel = clip.PurposeLabel,
            CreatedAtUtc = clip.CreatedAtUtc
        };
    }

    private static async Task<int?> WaitForProcessExitAsync(Process process, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(MinProcessTimeoutSeconds, timeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static string TrimForLog(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= MaxErrorLogLength ? value : value[..MaxErrorLogLength] + "...";
    }
}
