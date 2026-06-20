using System.ClientModel;

using Azure.AI.OpenAI;
using Azure.Identity;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;

using RoutePlanner.Components;
using RoutePlanner.Services;
using RoutePlanner.Settings;

using Serilog;

//--------------------------------------------------------------------------------
// Configure builder
//--------------------------------------------------------------------------------

Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

// Path
builder.Configuration.SetBasePath(AppContext.BaseDirectory);

// Service
builder.Host
    .UseWindowsService()
    .UseSystemd();

// Logging
builder.Logging.ClearProviders();
builder.Services.AddSerilog(options => options.ReadFrom.Configuration(builder.Configuration));

// App services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Settings
var routePlannerSettings = builder.Configuration.GetSection("RoutePlanner").Get<RoutePlannerSettings>() ?? new RoutePlannerSettings();
var foundrySettings = builder.Configuration.GetSection("Foundry").Get<FoundrySettings>() ?? new FoundrySettings();
builder.Services.AddSingleton(routePlannerSettings);
builder.Services.AddSingleton(foundrySettings);

// Azure OpenAI client
builder.Services.AddSingleton<AzureOpenAIClient>(_ =>
{
    var endpoint = new Uri(foundrySettings.Endpoint);
    return string.IsNullOrWhiteSpace(foundrySettings.ApiKey)
        ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
        : new AzureOpenAIClient(endpoint, new ApiKeyCredential(foundrySettings.ApiKey));
});

// Chat client
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    return client.GetChatClient(foundrySettings.ChatDeployment).AsIChatClient();
});

// Application services
builder.Services.AddSingleton<RouteOptimizer>();
builder.Services.AddSingleton<RouteReviewAnalyzer>();
builder.Services.AddSingleton<RoutePlannerService>();

//--------------------------------------------------------------------------------
// Configure request pipeline
//--------------------------------------------------------------------------------

var app = builder.Build();

// Ensure upload directory exists
var uploadDir = Path.GetFullPath(routePlannerSettings.UploadPath);
Directory.CreateDirectory(uploadDir);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadDir),
    RequestPath = "/uploads"
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
