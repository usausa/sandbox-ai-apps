namespace InspectorChecker.Components.Pages;

using System.Globalization;

using InspectorChecker.Models;
using InspectorChecker.Services;
using InspectorChecker.Settings;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

public sealed partial class Check : ComponentBase, IDisposable
{
    private static readonly SampleFile[] SampleFiles =
    [
        new("normal-organic-variance.csv", "正常: 巡回11日・198件の自然変動", "正常パターン", "2026年4月の調査11日、1日18顧客を巡回。設備ごとに漏れ電流が自然に散らばる正常データ。", "normal", "/samples/normal-organic-variance.csv"),
        new("normal-route-weather-shift.csv", "正常: 湿度変動つき巡回データ", "正常パターン", "巡回11日・198件。日単位で全体の漏れ電流水準が上下しても、日内のばらつきは保たれる正常データ。", "normal", "/samples/normal-route-weather-shift.csv"),
        new("fraud-default-097-template.csv", "不正: 0.97mA既定値の大量流用", "不正パターン", "巡回11日・198件。毎日18顧客を回った体裁だが、1mA直下の0.97mA前後を繰り返すデータ。", "fraud", "/samples/fraud-default-097-template.csv"),
        new("fraud-repeated-daily-template.csv", "不正: 日次テンプレートの反復入力", "不正パターン", "巡回11日・198件。顧客は日替わりなのに、同じ電流値の並びを毎日そのまま使い回す階段状データ。", "fraud", "/samples/fraud-repeated-daily-template.csv")
    ];

    [Inject]
    private InspectorCheckerService CheckerService { get; set; } = default!;

    [Inject]
    private InspectorCheckerSettings Settings { get; set; } = default!;

    [Inject]
    private ILogger<Check> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private int inputFileKey;
    private bool isBusy;
    private string progressMessage = string.Empty;
    private string? errorMessage;
    private InspectorCheckResult? result;
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

            var savedName = $"inspection-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv";
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
        result?.FeatureSummary.DailySummaries.FirstOrDefault(x => x.InvestigationDate == date);

    private static string GetScoreClass(int score) => score switch
    {
        >= 70 => "risk-high",
        >= 40 => "risk-medium",
        _ => "risk-low"
    };

    private static string GetScoreCaption(int score) => score switch
    {
        >= 70 => "入力テンプレートや固定値乱用の疑いが強い状態です。",
        >= 40 => "いくつか不自然な日があり、重点確認が必要です。",
        _ => "現状は自然なばらつきの範囲に見えます。"
    };

    private static string GetRowClass(int score) => score switch
    {
        >= 70 => "ic-row-high",
        >= 40 => "ic-row-medium",
        _ => string.Empty
    };

    private static string FormatPercent(double value) => $"{value * 100:0}%";

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

    private static bool IsNearDefault(double current) => Math.Abs(current - 0.97) <= 0.02 + 1e-9;

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
