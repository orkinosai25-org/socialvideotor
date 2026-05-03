using Microsoft.FluentUI.AspNetCore.Components;
using SocialVideotor.Components;
using SocialVideotor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        // Allow large file uploads (up to 500 MB) streamed through SignalR
        options.MaximumReceiveMessageSize = 500 * 1024 * 1024;
    });

builder.Services.AddFluentUIComponents();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddScoped<IVideoIndexerService, VideoIndexerService>();
builder.Services.AddScoped<IVideoAIService, VideoAIService>();
builder.Services.AddSingleton<IRawClipService, RawClipService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

// ── Controlled clip download endpoint ────────────────────────────────────────
// Clips are stored outside wwwroot (data/uploads/) so they are not publicly
// accessible via static file middleware.  Only known jobs can be downloaded.

// Compiled patterns reused across requests
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
    IWebHostEnvironment env) =>
{
    // Validate jobId is a well-formed GUID before using it in a path
    if (!safeJobIdPattern.IsMatch(jobId)) return Results.BadRequest();

    // Allow only safe filenames: source.mp4 or clip-N.mp4
    if (!safeFilenamePattern.IsMatch(filename)) return Results.BadRequest();

    // Validate job exists in memory
    var job = rawClipService.GetJob(jobId);
    if (job == null) return Results.NotFound();

    // Resolve and verify the path stays inside the data root (path-traversal guard)
    var dataRoot = Path.GetFullPath(Path.Combine(env.ContentRootPath, "data", "uploads"));
    // Normalise the root with a trailing separator so StartsWith works correctly
    // even if dataRoot itself is a drive root (e.g., C:\) on Windows.
    var dataRootWithSep = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
    var filePath = Path.GetFullPath(Path.Combine(dataRoot, jobId, filename));
    if (!filePath.StartsWith(dataRootWithSep, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest();

    if (!File.Exists(filePath)) return Results.NotFound();

    return Results.File(filePath, "video/mp4", filename, enableRangeProcessing: true);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

