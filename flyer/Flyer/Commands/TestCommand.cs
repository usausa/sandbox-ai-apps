namespace Flyer.Commands;

using System.Diagnostics;

using Flyer.Services;

using Smart.CommandLine.Hosting;

[Command("test", "Test.")]
public sealed class TestCommand : ICommandHandler
{
    private readonly ProductService productService;

    public TestCommand(ProductService productService)
    {
        ArgumentNullException.ThrowIfNull(productService);
        this.productService = productService;
    }

    public async ValueTask ExecuteAsync(CommandContext context)
    {
        var ct = context.CancellationToken;

        // --- インデックス作成（初回のみ） ---
        await productService.EnsureIndexAsync(ct).ConfigureAwait(false);

        // --- 商品登録 ---
        var count = await productService.RegisterProductsAsync(
        [
            ("1", "サントリー 天然水 550ml", 110, "飲料"),
            ("2", "コカ・コーラ 500ml ペットボトル", 150, "飲料"),
            ("3", "カルビー ポテトチップス うすしお味 60g", 128, "菓子"),
            ("4", "日清 カップヌードル しょうゆ味", 198, "食品"),
            ("5", "花王 アタック 洗濯洗剤 液体 900ml", 398, "日用品"),
            ("6", "サントリー 伊右衛門 緑茶 500ml", 140, "飲料"),
            ("7", "明治 おいしい牛乳 1000ml", 268, "乳製品"),
            ("8", "キリン 午後の紅茶 ストレートティー 500ml", 140, "飲料"),
            ("9", "ポテチ のりしお カルビー", 128, "菓子"),
            ("10", "アサヒ スーパードライ 350ml 6缶パック", 1180, "酒類"),
        ], ct).ConfigureAwait(false);
        Console.WriteLine($"{count} 件の商品を登録しました。");

        // 登録反映まで少し待つ
        await Task.Delay(2000, ct).ConfigureAwait(false);

        // --- あいまい検索 ---
        await RunSearchAsync("天然水", top: 3, ct).ConfigureAwait(false);
        await RunSearchAsync("ポテチ", top: 3, ct).ConfigureAwait(false);
        await RunSearchAsync("お茶のペットボトル", top: 3, ct).ConfigureAwait(false);
        await RunSearchAsync("洗剤", top: 3, ct).ConfigureAwait(false);
        await RunSearchAsync("カップ麺", top: 3, ct).ConfigureAwait(false);
    }

    private async Task RunSearchAsync(string query, int top, CancellationToken ct)
    {
        Console.WriteLine($"\n===== 検索: 「{query}」 =====");
        var results = await productService.SearchAsync(query, top, cancellationToken: ct).ConfigureAwait(false);
        foreach (var (r, i) in results.Select((item, idx) => (item, idx)))
        {
            Console.WriteLine($"  {i + 1}. {r.Name} | ¥{r.Price:N0} | カテゴリ: {r.Category ?? "-"} | スコア: {r.Score:F4}");
        }
    }
}

