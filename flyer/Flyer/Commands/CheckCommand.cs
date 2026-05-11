namespace Flyer.Commands;

using Flyer.Services;

using Smart.CommandLine.Hosting;

[Command("check", "Check flyer prices against the master vector database.")]
public sealed class CheckCommand : ICommandHandler
{
    private readonly FlyerImageReader imageReader;
    private readonly ProductService productService;
    private readonly PriceDifferenceAnalyzer analyzer;

    public CheckCommand(
        FlyerImageReader imageReader,
        ProductService productService,
        PriceDifferenceAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(imageReader);
        ArgumentNullException.ThrowIfNull(productService);
        ArgumentNullException.ThrowIfNull(analyzer);
        this.imageReader = imageReader;
        this.productService = productService;
        this.analyzer = analyzer;
    }

    [Option("--file", "-f", Description = "Flyer image file path (jpg/png/...)")]
    public string FilePath { get; set; } = string.Empty;

    [Option("--top", "-t", Description = "Number of master candidates to retrieve per item")]
    public int Top { get; set; } = 3;

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            await Console.Error.WriteLineAsync($"Image file not found: {FilePath}").ConfigureAwait(false);
            context.ExitCode = 1;
            return;
        }

        var ct = context.CancellationToken;

        Console.WriteLine($"Reading flyer image '{FilePath}'...");
        var items = await imageReader.ReadAsync(FilePath, ct).ConfigureAwait(false);
        Console.WriteLine($"Detected {items.Count} item(s).");
        Console.WriteLine();

        if (items.Count == 0)
        {
            return;
        }

        Console.WriteLine($"{"Flyer",-24} {"Price",6}  {"Master",-24} {"Price",6}  {"Diff",6}  Comment");
        Console.WriteLine(new string('-', 100));

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            var candidates = await productService.SearchAsync(item.Name, Top, ct).ConfigureAwait(false);
            var result = await analyzer.AnalyzeAsync(item, candidates, ct).ConfigureAwait(false);

            Console.WriteLine(
                $"{Truncate(result.FlyerName, 24),-24} {result.FlyerPrice,6}  " +
                $"{Truncate(result.MasterName, 24),-24} {result.MasterPrice,6}  " +
                $"{result.Difference,6}  {result.Comment}");
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty :
        value.Length <= max ? value : value[..max];
}
