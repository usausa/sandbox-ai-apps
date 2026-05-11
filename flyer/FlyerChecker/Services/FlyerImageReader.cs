namespace FlyerChecker.Services;

using System.Text.Json;

using FlyerChecker.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

/// <summary>チラシ画像から商品名と価格を抽出するサービス。</summary>
public sealed class FlyerImageReader
{
    private readonly string systemPrompt;
    private readonly IChatClient chatClient;
    private readonly ILogger<FlyerImageReader> logger;

    public FlyerImageReader(
        IChatClient chatClient,
        ILogger<FlyerImageReader> logger,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);
        this.chatClient = chatClient;
        this.logger = logger;

        var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "flyer_reader.txt");
        systemPrompt = File.ReadAllText(promptPath).Trim();
    }

    /// <summary>サーバー上のファイルパスから読み込む。</summary>
    public async Task<IReadOnlyList<FlyerItem>> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        var mediaType = GuessMediaType(filePath);

        return await ReadCoreAsync(bytes, mediaType, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<FlyerItem>> ReadCoreAsync(
        byte[] imageBytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User,
            [
                new TextContent("Extract every product and price from this flyer image."),
                new DataContent(imageBytes, mediaType)
            ])
        };

        var options = new ChatOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await chatClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        var text = response.Text ?? string.Empty;
        logger.LogDebug("Flyer LLM response: {Response}", text);

        return ParseItems(text);
    }

    private static IReadOnlyList<FlyerItem> ParseItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<FlyerItem>(items.GetArrayLength());
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameProp) ||
                    !item.TryGetProperty("price", out var priceProp))
                {
                    continue;
                }

                var name = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!TryReadInt(priceProp, out var price) || price <= 0)
                {
                    continue;
                }

                result.Add(new FlyerItem(name.Trim(), price));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt32(out value):
                return true;
            case JsonValueKind.String when int.TryParse(element.GetString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public static string GuessMediaType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
}
