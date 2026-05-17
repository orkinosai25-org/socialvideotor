using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Options;
using SocialVideotor.Models;
using SocialVideotor.Components;
using SocialVideotor.Services;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);
const int MinimumFfmpegTimeoutSeconds = 5;
const int ExportTimeoutMultiplier = 2;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 500 * 1024 * 1024;
    });

builder.Services.AddFluentUIComponents();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<VideoProcessingOptions>(builder.Configuration.GetSection("VideoProcessing"));
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddScoped<IVideoIndexerService, VideoIndexerService>();
builder.Services.AddScoped<IVideoAIService, VideoAIService>();
builder.Services.AddSingleton<IClipStorage, LocalClipStorage>();
builder.Services.AddSingleton<RawClipService>();
builder.Services.AddSingleton<IRawClipService>(sp => sp.GetRequiredService<RawClipService>());
builder.Services.AddHostedService<RawClipProcessingWorker>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

var safeJobIdPattern = new System.Text.RegularExpressions.Regex(
    @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
    System.Text.RegularExpressions.RegexOptions.Compiled);
var safeFilenamePattern = new System.Text.RegularExpressions.Regex(
    @"^(source|clip-\d{1,3})\.mp4$",
    System.Text.RegularExpressions.RegexOptions.Compiled);

app.MapGet("/api/clips/{jobId}/{filename}", (
    HttpContext httpContext,
    string jobId,
    string filename,
    IRawClipService rawClipService,
    IClipStorage storage) =>
{
    if (!safeJobIdPattern.IsMatch(jobId)) return Results.BadRequest();
    if (!safeFilenamePattern.IsMatch(filename)) return Results.BadRequest();

    var userId = UserIdentityResolver.ResolveForRequest(httpContext);
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();
    if (!string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();

    var blobPath = filename.Equals("source.mp4", StringComparison.OrdinalIgnoreCase)
        ? job.SourceBlobPath
        : $"jobs/{jobId}/{filename}";

    var filePath = storage.GetAbsolutePath(blobPath);
    if (!File.Exists(filePath)) return Results.NotFound();

    return Results.File(filePath, "video/mp4", filename, enableRangeProcessing: true);
});

app.MapPost("/api/jobs/upload", async (
    HttpRequest request,
    IRawClipService rawClipService,
    IOptions<VideoProcessingOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Multipart form data is required.");

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file == null)
        return Results.BadRequest("Missing file field named 'file'.");
    if (file.Length <= 0)
        return Results.BadRequest("Uploaded file is empty.");
    if (file.Length > options.Value.MaxUploadSizeBytes)
        return Results.BadRequest($"File exceeds maximum allowed size of {options.Value.MaxUploadSizeBytes} bytes.");
    if (!string.IsNullOrWhiteSpace(file.ContentType) && !UserIdentityResolver.SupportedUploadContentTypes.Contains(file.ContentType))
        return Results.BadRequest("Unsupported content type. Allowed: video/mp4, video/quicktime, video/x-m4v.");

    await using var stream = file.OpenReadStream();
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);

    var job = await rawClipService.StartProcessingAsync(stream, file.FileName, file.Length, file.ContentType, userId, cancellationToken);
    return Results.Accepted($"/api/jobs/{job.Id}", job);
});

app.MapGet("/api/jobs", (HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);
    var jobs = rawClipService.GetAllJobs(userId);
    return Results.Ok(jobs);
});

app.MapGet("/api/jobs/{jobId}", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();
    if (!string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();
    return Results.Ok(job);
});

app.MapGet("/api/jobs/{jobId}/clips", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);
    var clips = rawClipService.GetClips(jobId, userId);
    return Results.Ok(clips);
});

app.MapGet("/api/jobs/{jobId}/status", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);
    var status = rawClipService.GetJobStatus(jobId, userId);
    return status.HasValue ? Results.Ok(status.Value) : Results.NotFound();
});

app.MapGet("/api/jobs/{jobId}/clips/{clipNumber:int}/export", async (
    HttpContext httpContext,
    string jobId,
    int clipNumber,
    string? format,
    IRawClipService rawClipService,
    IClipStorage storage,
    IOptions<VideoProcessingOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!safeJobIdPattern.IsMatch(jobId)) return Results.BadRequest();
    if (clipNumber <= 0) return Results.BadRequest();

    var userId = UserIdentityResolver.ResolveForRequest(httpContext);
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();
    if (!string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();

    var clip = job.Clips.FirstOrDefault(c => c.ClipNumber == clipNumber);
    if (clip == null) return Results.NotFound();

    if (!TryNormalizeExportFormat(format, out var normalizedFormat, out var aspectLabel))
        return Results.BadRequest("Unsupported format. Use 'vertical' or 'square'.");

    var export = await BuildClipExportAsync(job, clip, normalizedFormat, options.Value, storage, cancellationToken);
    if (!export.Success) return Results.BadRequest(export.ErrorMessage);

    var fileName = BuildExportFileName(job.SourceFileName, clip.ClipNumber, aspectLabel);
    return Results.File(export.Content, "video/mp4", fileName);
});

app.MapGet("/api/jobs/{jobId}/export", async (
    HttpContext httpContext,
    string jobId,
    string? format,
    string? clipNumbers,
    IRawClipService rawClipService,
    IClipStorage storage,
    IOptions<VideoProcessingOptions> options,
    CancellationToken cancellationToken) =>
{
    if (!safeJobIdPattern.IsMatch(jobId)) return Results.BadRequest();

    var userId = UserIdentityResolver.ResolveForRequest(httpContext);
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();
    if (!string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();

    if (!TryNormalizeExportFormat(format, out var normalizedFormat, out var aspectLabel))
        return Results.BadRequest("Unsupported format. Use 'vertical' or 'square'.");

    var selectedClipNumbers = ParseClipNumbers(clipNumbers);
    if (!selectedClipNumbers.Any())
        return Results.BadRequest("Select at least one clip.");

    var clips = job.Clips
        .Where(c => selectedClipNumbers.Contains(c.ClipNumber))
        .OrderBy(c => c.ClipNumber)
        .ToList();
    if (!clips.Any()) return Results.BadRequest("No matching clips.");

    await using var zipStream = new MemoryStream();
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var clip in clips)
        {
            var export = await BuildClipExportAsync(job, clip, normalizedFormat, options.Value, storage, cancellationToken);
            if (!export.Success) return Results.BadRequest(export.ErrorMessage);

            var fileName = BuildExportFileName(job.SourceFileName, clip.ClipNumber, aspectLabel);
            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(export.Content, cancellationToken);
        }
    }

    var zipFileName = BuildSelectedZipFileName(job.SourceFileName, aspectLabel);
    return Results.File(zipStream.ToArray(), "application/zip", zipFileName);
});

app.MapPost("/api/jobs/{jobId}/retry", async (string jobId, HttpRequest request, IRawClipService rawClipService, CancellationToken cancellationToken) =>
{
    var userId = UserIdentityResolver.ResolveForRequest(request.HttpContext);
    var retried = await rawClipService.RetryJobAsync(jobId, userId, cancellationToken);
    return retried ? Results.Accepted($"/api/jobs/{jobId}") : Results.BadRequest();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool TryNormalizeExportFormat(string? format, out string normalizedFormat, out string aspectLabel)
{
    normalizedFormat = (format ?? "vertical").Trim().ToLowerInvariant();
    aspectLabel = normalizedFormat switch
    {
        "vertical" => "9x16",
        "square" => "1x1",
        _ => string.Empty
    };

    return normalizedFormat is "vertical" or "square";
}

static HashSet<int> ParseClipNumbers(string? clipNumbers)
{
    if (string.IsNullOrWhiteSpace(clipNumbers))
        return new HashSet<int>();

    return clipNumbers
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1)
        .Where(n => n > 0)
        .ToHashSet();
}

static string BuildSelectedZipFileName(string sourceFileName, string aspectLabel)
{
    var sourceBase = Path.GetFileNameWithoutExtension(sourceFileName);
    var safeBase = MakeFileNameSafe(string.IsNullOrWhiteSpace(sourceBase) ? "clips" : sourceBase);
    return $"{safeBase}-selected-{aspectLabel}.zip";
}

static string BuildExportFileName(string sourceFileName, int clipNumber, string aspectLabel)
{
    var sourceBase = Path.GetFileNameWithoutExtension(sourceFileName);
    var safeBase = MakeFileNameSafe(string.IsNullOrWhiteSpace(sourceBase) ? "clip" : sourceBase);
    return $"{safeBase}-clip-{clipNumber:D2}-{aspectLabel}.mp4";
}

static string MakeFileNameSafe(string value)
{
    var invalidChars = Path.GetInvalidFileNameChars();
    var safeChars = value
        .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
        .ToArray();
    return new string(safeChars).Trim('-', '.', ' ');
}

static async Task<(bool Success, byte[] Content, string ErrorMessage)> BuildClipExportAsync(
    RawClipJob job,
    RawClip clip,
    string format,
    VideoProcessingOptions options,
    IClipStorage storage,
    CancellationToken cancellationToken)
{
    var extractedClipPath = storage.GetAbsolutePath(clip.BlobPath);
    if (format == "vertical" && File.Exists(extractedClipPath))
    {
        var existingBytes = await File.ReadAllBytesAsync(extractedClipPath, cancellationToken);
        return (true, existingBytes, string.Empty);
    }

    var ffmpegAvailable = await IsFfmpegAvailableAsync(options.FfmpegPath, cancellationToken);
    if (!ffmpegAvailable)
        return (false, Array.Empty<byte>(), "FFmpeg is required for this export format.");

    var sourcePath = storage.GetAbsolutePath(job.SourceBlobPath);
    if (!File.Exists(sourcePath))
        return (false, Array.Empty<byte>(), "Source video file was not found.");

    var tempFilePath = Path.Combine(Path.GetTempPath(), $"socialvideotor-export-{Guid.NewGuid():N}.mp4");
    try
    {
        var useExtractedClipAsInput = File.Exists(extractedClipPath);
        var inputPath = useExtractedClipAsInput ? extractedClipPath : sourcePath;
        var filter = format == "square"
            ? "scale=1080:1080:force_original_aspect_ratio=increase,crop=1080:1080"
            : "scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920";

        var args = useExtractedClipAsInput
            ? string.Format(CultureInfo.InvariantCulture,
                "-y -i \"{0}\" -vf \"{1}\" -c:v libx264 -preset fast -crf 23 -movflags +faststart -c:a aac -b:a 128k \"{2}\"",
                inputPath, filter, tempFilePath)
            : string.Format(CultureInfo.InvariantCulture,
                "-y -ss {0} -i \"{1}\" -t {2} -vf \"{3}\" -c:v libx264 -preset fast -crf 23 -movflags +faststart -c:a aac -b:a 128k \"{4}\"",
                clip.StartTimeSeconds, inputPath, clip.DurationSeconds, filter, tempFilePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(options.FfmpegPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exportTimeoutSeconds = Math.Max(MinimumFfmpegTimeoutSeconds, options.MediaToolTimeoutSeconds) * ExportTimeoutMultiplier;
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(exportTimeoutSeconds));
        await process.WaitForExitAsync(timeoutSource.Token);
        _ = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0 || !File.Exists(tempFilePath))
            return (false, Array.Empty<byte>(), string.IsNullOrWhiteSpace(error) ? "Clip export failed." : error);

        var bytes = await File.ReadAllBytesAsync(tempFilePath, cancellationToken);
        return (true, bytes, string.Empty);
    }
    catch (OperationCanceledException)
    {
        return (false, Array.Empty<byte>(), "Clip export timed out.");
    }
    finally
    {
        if (File.Exists(tempFilePath))
            File.Delete(tempFilePath);
    }
}

static async Task<bool> IsFfmpegAvailableAsync(string ffmpegPath, CancellationToken cancellationToken)
{
    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpegPath, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(MinimumFfmpegTimeoutSeconds));
        await process.WaitForExitAsync(timeoutSource.Token);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}
