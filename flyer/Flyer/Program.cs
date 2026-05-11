using System.ClientModel;

using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Indexes;

using Flyer.Commands;
using Flyer.Options;
using Flyer.Services;
using Flyer.Services.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;

using Smart.CommandLine.Hosting;

var builder = CommandHost.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables();

var foundryOptions = builder.Configuration.GetSection("Foundry").Get<FoundryOptions>() ?? new FoundryOptions();
var searchOptions = builder.Configuration.GetSection("AzureAISearch").Get<AzureAISearchOptions>() ?? new AzureAISearchOptions();
var productServiceOptions = builder.Configuration.GetSection("ProductService").Get<ProductServiceOptions>() ?? new ProductServiceOptions();

builder.Services.AddSingleton(foundryOptions);
builder.Services.AddSingleton(searchOptions);
builder.Services.AddSingleton(productServiceOptions);

// Microsoft Foundry (Azure OpenAI v1) chat & embeddings clients.
builder.Services.AddSingleton<AzureOpenAIClient>(_ =>
{
    var endpoint = new Uri(foundryOptions.Endpoint);
    return string.IsNullOrWhiteSpace(foundryOptions.ApiKey)
        ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
        : new AzureOpenAIClient(endpoint, new ApiKeyCredential(foundryOptions.ApiKey));
});

builder.Services.AddSingleton<IChatClient>(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    return client.GetChatClient(foundryOptions.ChatDeployment).AsIChatClient();
});

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var client = sp.GetRequiredService<AzureOpenAIClient>();
    return client.GetEmbeddingClient(foundryOptions.EmbeddingDeployment).AsIEmbeddingGenerator();
});

// Azure AI Search vector store.
builder.Services.AddSingleton<SearchIndexClient>(_ =>
{
    var endpoint = new Uri(searchOptions.Endpoint);
    return string.IsNullOrWhiteSpace(searchOptions.ApiKey)
        ? new SearchIndexClient(endpoint, new DefaultAzureCredential())
        : new SearchIndexClient(endpoint, new AzureKeyCredential(searchOptions.ApiKey));
});

builder.Services.AddSingleton<VectorStoreCollection<string, ProductRecord>>(sp =>
{
    var indexClient = sp.GetRequiredService<SearchIndexClient>();
    return new AzureAISearchCollection<string, ProductRecord>(
        indexClient,
        productServiceOptions.IndexName);
});

builder.Services.AddSingleton<MasterCsvLoader>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<FlyerImageReader>();
builder.Services.AddSingleton<PriceDifferenceAnalyzer>();

builder.ConfigureCommands(commands =>
{
    commands.ConfigureRootCommand(root =>
    {
        root.WithDescription("Flyer tool");
    });

    commands.AddCommands();
});

var host = builder.Build();
return await host.RunAsync();

