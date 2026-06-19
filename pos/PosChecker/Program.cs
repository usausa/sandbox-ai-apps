using System.ClientModel;

using Azure.AI.OpenAI;
using Azure.Identity;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;

using PosChecker.Components;
using PosChecker.Services;
using PosChecker.Settings;

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
var posCheckerSettings = builder.Configuration.GetSection("PosChecker").Get<PosCheckerSettings>() ?? new PosCheckerSettings();
var foundrySettings = builder.Configuration.GetSection("Foundry").Get<FoundrySettings>() ?? new FoundrySettings();
builder.Services.AddSingleton(posCheckerSettings);
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
builder.Services.AddSingleton<PosDataLoader>();
builder.Services.AddSingleton<PosFeatureSummaryBuilder>();
builder.Services.AddSingleton<PosFraudAnalyzer>();
builder.Services.AddSingleton<PosCheckerService>();

//--------------------------------------------------------------------------------
// Configure request pipeline
//--------------------------------------------------------------------------------

var app = builder.Build();

// Ensure upload directory exists
var uploadDir = Path.GetFullPath(posCheckerSettings.UploadPath);
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
