using Microsoft.EntityFrameworkCore;
using Noto.Server.Components;
using Noto.Server.Data;
using Noto.Server.Api;
using Noto.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Load local secrets (appsettings.Local.json is gitignored)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddDbContext<NotoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Noto"),
        o => o.UseVector()));

builder.Services.AddScoped<EntityService>();
builder.Services.AddScoped<EmbeddingService>();
builder.Services.AddScoped<OpenRouterClient>();
builder.Services.AddScoped<AiConversationService>();
builder.Services.AddHttpClient("Embeddings");
builder.Services.AddHttpClient("OpenRouter");
// EmbeddingWorker available but not auto-started — embed on demand via CLI or future UI trigger

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Auto-migrate + ensure embedding provider on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotoDbContext>();
    await db.Database.MigrateAsync();

    var embSvc = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
    var endpoint = builder.Configuration["Embeddings:Endpoint"] ?? "http://localhost:11434/v1/embeddings";
    await embSvc.EnsureProvider(
        name: "mxbai-embed-large",
        modelId: "mxbai-embed-large",
        provider: "ollama",
        endpoint: endpoint,
        dimensions: 1024);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.MapStaticAssets();

// Version endpoint
var gitHash = "dev";
try {
    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
        { RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = Directory.GetCurrentDirectory() });
    if (p != null) { gitHash = (await p.StandardOutput.ReadToEndAsync()).Trim(); p.WaitForExit(); }
} catch { }
app.MapGet("/api/version", () => Results.Ok(new { hash = gitHash }));

// No-cache for m.html so mobile always gets latest
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value?.Contains("m.html") == true)
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
    }
    await next();
});

// API endpoints for capture (photo/voice/text upload)
app.MapCaptureApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
