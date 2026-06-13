namespace PosChecker.Components.Pages;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using PosChecker.Models;
using PosChecker.Services;
using PosChecker.Settings;

public sealed partial class Check : ComponentBase, IDisposable
{
    private static readonly SampleFile[] SampleFiles =
    [
        new("normal-typical.csv", "正常: 通常営業14日", "正常パターン", "従業員1名・14営業日。取消/返品が低率で自然にばらつき、ポイントは会員が通常利用する正常データ。", "normal", "/samples/normal-typical.csv"),
        new("normal-busy-weekend.csv", "正常: 週末繁忙あり", "正常パターン", "14営業日。週末に取引・返品が増えるが、レシート有で比率は正常域に収まる正常データ。", "normal", "/samples/normal-busy-weekend.csv"),
        new("fraud-void-skim.csv", "不正: 取消抜き取り", "不正パターン", "売上直後に同額を取り消すVoidが複数日で反復し、現金取引に偏る不正データ。", "fraud", "/samples/fraud-void-skim.csv"),
        new("fraud-refund-no-receipt.csv", "不正: 返品抜き取り", "不正パターン", "レシート無し・高額・同額の現金返品が複数日に集中する不正データ。", "fraud", "/samples/fraud-refund-no-receipt.csv"),
        new("fraud-point-abuse.csv", "不正: ポイント不正", "不正パターン", "非会員売上に特定会員IDでポイントを付与/利用し、現金売上へ集中させる不正データ。", "fraud", "/samples/fraud-point-abuse.csv")
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

    private async Task OnCsvFileChangedAsync(InputFileChangeEventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        var file = e.File;
        if (!string.Equals(Path.GetExtension(file.Name), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"CSVファイルを選択してください: {file.Name}";
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

            var savedName = $"pos-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv";
            var savedPath = Path.Combine(uploadDir, savedName);

            await using (var dest = File.Create(savedPath))
            await using (var src = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024))
            {
                await src.CopyToAsync(dest, cts.Token);
            }

            Logger.InfoCsvUploaded(savedPath);
            Logger.InfoCheckStarted();

            result = await CheckerService.AnalyzeAsync(savedPath, progress, cts.Token);

            var suspiciousDayCount = result.Analysis.DailyResults.Count(x => x.Score >= 70);
            Logger.InfoCheckCompleted(suspiciousDayCount);
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

    private DailyFeatureSummary? GetDailyFeature(DateOnly date) =>
        result?.FeatureSummary.DailySummaries.FirstOrDefault(x => x.BusinessDate == date);

    private static string GetScoreClass(int score) => score switch
    {
        >= 70 => "risk-high",
        >= 40 => "risk-medium",
        _ => "risk-low"
    };

    private static string GetScoreCaption(int score) => score switch
    {
        >= 70 => "取消・返品・ポイントの不正操作の疑いが強い状態です。",
        >= 40 => "いくつか不自然な営業日があり、重点確認が必要です。",
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

    private static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Sale => "売上",
        TransactionType.Void => "取消",
        TransactionType.Return => "返品",
        TransactionType.NoSale => "レジ開放",
        _ => type.ToString()
    };

    private static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "現金",
        PaymentMethod.Credit => "クレジット",
        PaymentMethod.QR => "QR",
        PaymentMethod.GiftCard => "商品券",
        PaymentMethod.Other => "その他",
        _ => method.ToString()
    };

    private static string AnomalyLabel(string kind) => kind switch
    {
        "SaleThenVoid" => "売上直後の取消",
        "PointsRedeemThenVoid" => "ポイント利用直後の取消",
        "RepeatedRefundAmount" => "同額返品の反復",
        _ => kind
    };

    private static string AnomalyDetail(SequenceAnomaly anomaly) => anomaly.Kind switch
    {
        "RepeatedRefundAmount" => $"{FormatYen(anomaly.Amount)} の返品が {anomaly.Occurrences} 回",
        "PointsRedeemThenVoid" => $"取引 {anomaly.TransactionId}（元 {anomaly.OriginalTransactionId}） / {anomaly.Amount}pt / {anomaly.SecondsApart} 秒後に取消",
        _ => $"取引 {anomaly.TransactionId}（元 {anomaly.OriginalTransactionId}） / {FormatYen(anomaly.Amount)} / {anomaly.SecondsApart} 秒後に取消"
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

    private sealed record SampleFile(
        string FileName,
        string Title,
        string KindLabel,
        string Description,
        string KindClass,
        string Url);
}
