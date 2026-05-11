namespace Flyer.Services;

using System.Text.Json;

using Flyer.Services.Models;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads a flyer image using a Foundry chat model and returns parsed product/price entries.
/// </summary>
public sealed class FlyerImageReader
{
    private const string SystemPrompt =
        "あなたはチラシ画像から販売商品の商品名と価格を抽出するOCRアシスタントです。" +
        "余分な説明・マークダウン・コードフェンスを一切含めず、以下のスキーマのJSONオブジェクトのみを返してください: " +
        "{\"items\":[{\"name\":\"<商品名>\",\"price\":<税込整数円>}]}. " +
        "抽出ルール: " +
        "'name'はチラシに印刷されている具体的な商品名をそのまま記載してください。" +
        "'price'は記号・カンマ・小数点を含まない日本円の整数値にしてください。税抜き価格の場合は1.1倍して整数に丸めてください。" +
        "除外ルール（以下は絶対に含めないこと）: " +
        "・クーポン・ポイント還元・割引券などのクーポン告知。" +
        "・「お買い得」「自慢の逸品」「大特価」などのキャッチコピーや見出し。" +
        "・祝日名・イベント名・キャンペーン名（例: 昭和の日、感謝祭）。" +
        "・価格が0円または読み取れないもの。" +
        "・実在しない商品や画像に存在しない商品。";

    private readonly IChatClient chatClient;
    private readonly ILogger<FlyerImageReader> logger;

    public FlyerImageReader(IChatClient chatClient, ILogger<FlyerImageReader> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(logger);
        this.chatClient = chatClient;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<FlyerItem>> ReadAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(false);
        var mediaType = GuessMediaType(imagePath);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User,
            [
                new TextContent("Extract every product and price from this flyer image."),
                new DataContent(bytes, mediaType)
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
            return Array.Empty<FlyerItem>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<FlyerItem>();
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
            return Array.Empty<FlyerItem>();
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

    private static string GuessMediaType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/jpeg"
        };
}
