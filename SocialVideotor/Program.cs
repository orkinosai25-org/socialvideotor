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
app.UseStaticFiles(); // serves runtime-generated files (e.g., /uploads/...)
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

