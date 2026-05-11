namespace Flyer.Commands;

using Flyer.Services;

using Smart.CommandLine.Hosting;

[Command("load", "Master CSV からベクトル DB に商品マスタデータをロードする。")]
public sealed class LoadCommand : ICommandHandler
{
    private readonly MasterCsvLoader csvLoader;
    private readonly ProductService productService;

    public LoadCommand(MasterCsvLoader csvLoader, ProductService productService)
    {
        ArgumentNullException.ThrowIfNull(csvLoader);
        ArgumentNullException.ThrowIfNull(productService);
        this.csvLoader = csvLoader;
        this.productService = productService;
    }

    [Option("--file", "-f", Description = "マスタ CSV ファイルパス (列: Id,Name,Price,Category)")]
    public string FilePath { get; set; } = string.Empty;

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            await Console.Error.WriteLineAsync($"CSV ファイルが見つかりません: {FilePath}").ConfigureAwait(false);
            context.ExitCode = 1;
            return;
        }

        var ct = context.CancellationToken;

        Console.WriteLine($"マスタデータを '{FilePath}' からロード中...");
        await productService.EnsureIndexAsync(ct).ConfigureAwait(false);

        var count = 0;
        await foreach (var record in csvLoader.LoadAsync(FilePath, ct).ConfigureAwait(false))
        {
            await productService.RegisterProductAsync(record.Id, record.Name, record.Price, record.Category, ct).ConfigureAwait(false);
            count++;
        }

        Console.WriteLine($"{count} 件をベクトル DB に登録しました。");
    }
}
