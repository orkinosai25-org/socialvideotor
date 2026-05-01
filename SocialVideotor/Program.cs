using Microsoft.FluentUI.AspNetCore.Components;
using SocialVideotor.Components;
using SocialVideotor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IProjectService, ProjectService>();
builder.Services.AddScoped<IVideoIndexerService, VideoIndexerService>();
builder.Services.AddScoped<IVideoAIService, VideoAIService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

