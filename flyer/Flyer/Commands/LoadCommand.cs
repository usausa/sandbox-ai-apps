namespace Flyer.Commands;

using Flyer.Services;

using Smart.CommandLine.Hosting;

[Command("load", "Load the master product data into the vector database.")]
public sealed class LoadCommand : ICommandHandler
{
    private readonly MasterCsvLoader csvLoader;
    private readonly ProductVectorStore vectorStore;

    public LoadCommand(MasterCsvLoader csvLoader, ProductVectorStore vectorStore)
    {
        ArgumentNullException.ThrowIfNull(csvLoader);
        ArgumentNullException.ThrowIfNull(vectorStore);
        this.csvLoader = csvLoader;
        this.vectorStore = vectorStore;
    }

    [Option("--file", "-f", Description = "Master CSV file path (columns: Id,Name,Price)")]
    public string FilePath { get; set; } = string.Empty;

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            await Console.Error.WriteLineAsync($"CSV file not found: {FilePath}").ConfigureAwait(false);
            context.ExitCode = 1;
            return;
        }

        var ct = context.CancellationToken;

        Console.WriteLine($"Loading master data from '{FilePath}'...");
        await vectorStore.EnsureCreatedAsync(ct).ConfigureAwait(false);

        var records = csvLoader.LoadAsync(FilePath, ct);
        var count = await vectorStore.UpsertAsync(records, ct).ConfigureAwait(false);

        Console.WriteLine($"Loaded {count} record(s) into the vector database.");
    }
}
