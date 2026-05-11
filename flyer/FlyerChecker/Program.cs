using System.ClientModel;

using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Indexes;

using FlyerChecker.Components;
using FlyerChecker.Models;
using FlyerChecker.Services;
using FlyerChecker.Settings;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;

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
var flyerCheckerSettings = builder.Configuration.GetSection("FlyerChecker").Get<FlyerCheckerSettings>() ?? new FlyerCheckerSettings();
var foundrySettings = builder.Configuration.GetSection("Foundry").Get<FoundrySettings>() ?? new FoundrySettings();
var searchSettings = builder.Configuration.GetSection("AzureAISearch").Get<AzureAISearchSettings>() ?? new AzureAISearchSettings();
builder.Services.AddSingleton(flyerCheckerSettings);
builder.Services.AddSingleton(foundrySettings);
builder.Services.AddSingleton(searchSettings);

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

// Embedding generator
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    return client.GetEmbeddingClient(foundrySettings.EmbeddingDeployment).AsIEmbeddingGenerator();
});

// Azure AI Search
builder.Services.AddSingleton<SearchIndexClient>(_ =>
{
    var endpoint = new Uri(searchSettings.Endpoint);
    return string.IsNullOrWhiteSpace(searchSettings.ApiKey)
        ? new SearchIndexClient(endpoint, new DefaultAzureCredential())
        : new SearchIndexClient(endpoint, new AzureKeyCredential(searchSettings.ApiKey));
});

// Vector collection
builder.Services.AddSingleton<VectorStoreCollection<string, ProductRecord>>(sp =>
{
    var indexClient = sp.GetRequiredService<SearchIndexClient>();
    return new AzureAISearchCollection<string, ProductRecord>(indexClient, searchSettings.IndexName);
});

// Application services
builder.Services.AddSingleton<MasterCsvLoader>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<FlyerImageReader>();
builder.Services.AddSingleton<PriceDifferenceAnalyzer>();
builder.Services.AddSingleton<FlyerCheckerService>();

//--------------------------------------------------------------------------------
// Configure request pipeline
//--------------------------------------------------------------------------------

var app = builder.Build();

// Ensure upload directory exists
var uploadDir = Path.GetFullPath(flyerCheckerSettings.UploadPath);
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

app.Run();
