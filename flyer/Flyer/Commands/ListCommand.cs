namespace Flyer.Commands;

using Flyer.Services;

using Smart.CommandLine.Hosting;

[Command("list", "List products detected in a flyer image.")]
public sealed class ListCommand : ICommandHandler
{
    private readonly FlyerImageReader imageReader;

    public ListCommand(FlyerImageReader imageReader)
    {
        ArgumentNullException.ThrowIfNull(imageReader);
        this.imageReader = imageReader;
    }

    [Option("--file", "-f", Description = "Flyer image file path (jpg/png/...)")]
    public string FilePath { get; set; } = string.Empty;

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

        Console.WriteLine($"{"Name",-32} {"Price",6}");
        Console.WriteLine(new string('-', 40));

        foreach (var item in items)
        {
            Console.WriteLine($"{item.Name,-32} {item.Price,6}");
        }
    }
}
