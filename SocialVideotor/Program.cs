using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.Extensions.Options;
using SocialVideotor.Components;
using SocialVideotor.Services;

var builder = WebApplication.CreateBuilder(args);

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
