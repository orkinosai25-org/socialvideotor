using Microsoft.FluentUI.AspNetCore.Components;
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
    string jobId,
    string filename,
    IRawClipService rawClipService,
    IClipStorage storage) =>
{
    if (!safeJobIdPattern.IsMatch(jobId)) return Results.BadRequest();
    if (!safeFilenamePattern.IsMatch(filename)) return Results.BadRequest();

    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();

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
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Multipart form data is required.");

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file == null)
        return Results.BadRequest("Missing file field named 'file'.");

    await using var stream = file.OpenReadStream();
    var userId = request.Headers.TryGetValue("X-User-Id", out var headerUserId)
        ? headerUserId.ToString()
        : "anonymous";

    var job = await rawClipService.StartProcessingAsync(stream, file.FileName, file.Length, userId, cancellationToken);
    return Results.Accepted($"/api/jobs/{job.Id}", job);
});

app.MapGet("/api/jobs", (HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId) && request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        userId = headerUserId.ToString();

    var jobs = rawClipService.GetAllJobs(userId);
    return Results.Ok(jobs);
});

app.MapGet("/api/jobs/{jobId}", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = request.Query["userId"].ToString();
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();
    if (!string.IsNullOrWhiteSpace(userId) && !string.Equals(job.UserId, userId, StringComparison.OrdinalIgnoreCase))
        return Results.NotFound();
    return Results.Ok(job);
});

app.MapGet("/api/jobs/{jobId}/clips", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = request.Query["userId"].ToString();
    var clips = rawClipService.GetClips(jobId, userId);
    return Results.Ok(clips);
});

app.MapGet("/api/jobs/{jobId}/status", (string jobId, HttpRequest request, IRawClipService rawClipService) =>
{
    var userId = request.Query["userId"].ToString();
    var status = rawClipService.GetJobStatus(jobId, userId);
    return status.HasValue ? Results.Ok(status.Value) : Results.NotFound();
});

app.MapPost("/api/jobs/{jobId}/retry", async (string jobId, HttpRequest request, IRawClipService rawClipService, CancellationToken cancellationToken) =>
{
    var userId = request.Query["userId"].ToString();
    var retried = await rawClipService.RetryJobAsync(jobId, userId, cancellationToken);
    return retried ? Results.Accepted($"/api/jobs/{jobId}") : Results.BadRequest();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
