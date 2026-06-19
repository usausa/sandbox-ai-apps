namespace PosChecker.Components.Pages;

using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using PosChecker.Models;
using PosChecker.Services;
using PosChecker.Settings;

public sealed partial class Check : ComponentBase, IDisposable
{
    private static readonly SampleSet[] SampleSets =
    [
        new("正常", "正常パターン", "normal", "複数店舗・複数担当者。返品/打直は低率、ポイント/クーポンは自然な利用。", "/samples/normal"),
        new("不正: ポイント不正付与", "不正パターン", "fraud", "特定担当者が特定会員へ偏ってポイント付与、同一会員の短時間連続会計。", "/samples/fraud-point"),
        new("不正: かご抜け", "不正パターン", "fraud", "特定担当者が同一JAN/用途を複数購入する会計を頻発。", "/samples/fraud-cart"),
        new("不正: クーポン不正利用", "不正パターン", "fraud", "特定会員が会員限定/アプリクーポンを反復スキャン。", "/samples/fraud-coupon"),
        new("不正: フリー返品不正", "不正パターン", "fraud", "特定担当者の返品率が突出、時間帯に偏る。", "/samples/fraud-return"),
        new("不正: 打ち直し不正", "不正パターン", "fraud", "特定担当者の打直率が突出、時間帯に偏る。", "/samples/fraud-rekey")
    ];

    private static readonly SampleFile[] SampleFiles =
    [
        new("SalesHeader.csv", "売上ヘッダ"),
        new("SalesDetail.csv", "売上明細"),
        new("Promotion.csv", "販促")
    ];

    [Inject]
    private PosCheckerService CheckerService { get; set; } = default!;

    [Inject]
    private PosCheckerSettings Settings { get; set; } = default!;

    [Inject]
    private ILogger<Check> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private int inputFileKey;
    private bool isBusy;
    private string progressMessage = string.Empty;
    private string? errorMessage;
    private PosCheckResult? result;
    private CancellationTokenSource? cts;
    private ElementReference dropZoneRef;
    private bool isDropZoneInitialized;

    private async Task OnCsvFilesChangedAsync(InputFileChangeEventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        errorMessage = null;
        result = null;
        progressMessage = "CSVを保存中...";
        StateHasChanged();

        cts = new CancellationTokenSource();
        var progress = new Progress<string>(message =>
        {
            progressMessage = message;
            _ = InvokeAsync(StateHasChanged);
        });

#pragma warning disable CA1031
        try
        {
            var uploadDir = Path.GetFullPath(Settings.UploadPath);
            Directory.CreateDirectory(uploadDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string? headerPath = null;
            string? detailPath = null;
            string? promotionPath = null;

            foreach (var file in e.GetMultipleFiles(maximumFileCount: 10))
            {
                if (!string.Equals(Path.GetExtension(file.Name), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var savedPath = Path.Combine(uploadDir, $"pos-{stamp}-{file.Name}");
                await using (var dest = File.Create(savedPath))
                await using (var src = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024))
                {
                    await src.CopyToAsync(dest, cts.Token);
                }

                switch (await ClassifyAsync(savedPath, cts.Token))
                {
                    case CsvKind.Header:
                        headerPath = savedPath;
                        break;
                    case CsvKind.Detail:
                        detailPath = savedPath;
                        break;
                    case CsvKind.Promotion:
                        promotionPath = savedPath;
                        break;
                    default:
                        break;
                }
            }

            var missing = new List<string>();
            if (headerPath is null)
            {
                missing.Add("SalesHeader");
            }

            if (detailPath is null)
            {
                missing.Add("SalesDetail");
            }

            if (promotionPath is null)
            {
                missing.Add("Promotion");
            }

            if (missing.Count > 0)
            {
                errorMessage = $"3種類のCSVをまとめて選択してください（不足: {string.Join(", ", missing)}）。";
                return;
            }

            Logger.InfoCsvUploaded(headerPath!);
            Logger.InfoCheckStarted();

            result = await CheckerService.AnalyzeAsync(headerPath!, detailPath!, promotionPath!, progress, cts.Token);

            var suspiciousCashierCount = result.Analysis.CashierResults.Count(x => x.Score >= 70);
            Logger.InfoCheckCompleted(suspiciousCashierCount);
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
        {
            Logger.InfoCheckCancelled();
        }
        catch (IOException ex)
        {
            Logger.ErrorCheckFailed(ex, ex.Message);
            errorMessage = $"ファイル保存エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            Logger.ErrorCheckFailed(ex, ex.Message);
            errorMessage = $"分析エラー: {ex.Message}";
        }
        finally
        {
            isBusy = false;
            progressMessage = string.Empty;
            cts?.Dispose();
            cts = null;
            await InvokeAsync(StateHasChanged);
        }
#pragma warning restore CA1031
    }

    // 先頭行のヘッダ列名で3ビューを判別する。
    private static async Task<CsvKind> ClassifyAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        if (headerLine.Contains("Jancode", StringComparison.OrdinalIgnoreCase))
        {
            return CsvKind.Detail;
        }

        if (headerLine.Contains("CouponCode", StringComparison.OrdinalIgnoreCase) ||
            headerLine.Contains("SlipNo", StringComparison.OrdinalIgnoreCase))
        {
            return CsvKind.Promotion;
        }

        if (headerLine.Contains("TransactionType", StringComparison.OrdinalIgnoreCase))
        {
            return CsvKind.Header;
        }

        return CsvKind.Unknown;
    }

    private Task CancelAsync()
    {
        cts?.Cancel();
        return Task.CompletedTask;
    }

    private Task ClearAsync()
    {
        result = null;
        errorMessage = null;
        inputFileKey++;
        return Task.CompletedTask;
    }

    private CashierFeatureSummary? GetCashierFeature(string storeCode, string cashierCode) =>
        result?.FeatureSummary.CashierSummaries
            .FirstOrDefault(x =>
                string.Equals(x.StoreCode, storeCode, StringComparison.Ordinal) &&
                string.Equals(x.CashierCode, cashierCode, StringComparison.Ordinal));

    private static string GetScoreClass(int score) => score switch
    {
        >= 70 => "risk-high",
        >= 40 => "risk-medium",
        _ => "risk-low"
    };

    private static string GetScoreCaption(int score) => score switch
    {
        >= 70 => "担当者または会員に不正操作の疑いが強い状態です。",
        >= 40 => "いくつか不自然な担当者があり、重点確認が必要です。",
        _ => "現状は通常営業の範囲に見えます。"
    };

    private static string GetRowClass(int score) => score switch
    {
        >= 70 => "ic-row-high",
        >= 40 => "ic-row-medium",
        _ => string.Empty
    };

    private static string FormatPercent(double value) => $"{value * 100:0.#}%";

    private static string FormatYen(long value) => $"¥{value:N0}";

    private static string FormatTokens(TokenUsageResult usage) =>
        $"入力 {usage.InputTokens.ToString("N0", CultureInfo.CurrentCulture)} / 出力 {usage.OutputTokens.ToString("N0", CultureInfo.CurrentCulture)} / 合計 {usage.TotalTokens.ToString("N0", CultureInfo.CurrentCulture)}";

    private static string FormatCost(TokenUsageResult usage)
    {
        if (usage.EstimatedCostJpy is not { } cost)
        {
            return "単価未設定";
        }

        return $"約 {cost.ToString("0.######", CultureInfo.CurrentCulture)} 円";
    }

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Sale => "売上",
        TransactionType.Return => "返品",
        _ => type.ToString()
    };

    private static string TenderLabel(TenderType tender) => tender switch
    {
        TenderType.Cash => "現金",
        TenderType.Credit => "クレジット",
        TenderType.GiftCard => "商品券",
        _ => tender.ToString()
    };

    private static string ScenarioLabel(string scenario) => scenario switch
    {
        "PointAbuse" => "ポイント不正付与",
        "CartBypass" => "かご抜け",
        "CouponAbuse" => "クーポン不正",
        "ReturnFraud" => "フリー返品不正",
        "RekeyFraud" => "打ち直し不正",
        _ => scenario
    };

    private static string SignalLabel(string kind) => kind switch
    {
        "CashierMemberConcentration" => "担当者の会員偏り",
        "SameItemMultiBuy" => "同一商品の複数購入会計",
        "ShortIntervalSameMember" => "同一会員の短時間連続会計",
        "RepeatedCouponByMember" => "同一会員のクーポン反復",
        "HighReturnCashier" => "返品率が高い担当者",
        "HighRekeyCashier" => "打直率が高い担当者",
        _ => kind
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (isDropZoneInitialized)
        {
            return;
        }

        await JsRuntime.InvokeVoidAsync("initDropZone", dropZoneRef);
        isDropZoneInitialized = true;
    }

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private enum CsvKind
    {
        Unknown,
        Header,
        Detail,
        Promotion
    }

    private sealed record SampleSet(
        string Title,
        string KindLabel,
        string KindClass,
        string Description,
        string BaseUrl);

    private sealed record SampleFile(string FileName, string Label);
}
