namespace RoutePlanner.Components.Pages;

using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using RoutePlanner.Models;
using RoutePlanner.Services;
using RoutePlanner.Settings;

public sealed partial class Check : ComponentBase, IDisposable
{
    private static readonly SampleFile[] SampleFiles =
    [
        new("kofu-dense-90.csv", "標準: 狭域・戸建中心 約90件", "標準", "甲府市中心部の狭い範囲で、一定地域の隣家を順に回る戸建中心の約90件（集合住宅は少数）。1件あたりの調査は短時間で、戸別点検型の基本的な足順生成を確認できます。", "normal", "/samples/kofu-dense-90.csv"),
        new("kofu-window-100.csv", "時間帯指定: 狭域 約100件", "時間帯指定", "狭域・戸建中心の約100件で午前中・14:00-16:00などの厳守/希望の時間帯指定が多く、枠の取り合いと希望超過（違反）・未割当を検証できます。", "normal", "/samples/kofu-window-100.csv"),
        new("kofu-tight-120.csv", "残業注意: 狭域 約120件", "残業注意", "狭域に戸建を多数含む約120件。1日の処理能力を超える件数で、未割当や勤務終了間際の残業が発生しやすい境界ケースです。", "fraud", "/samples/kofu-tight-120.csv")
    ];

    [Inject]
    private RoutePlannerService PlannerService { get; set; } = default!;

    [Inject]
    private RoutePlannerSettings Settings { get; set; } = default!;

    [Inject]
    private ILogger<Check> Logger { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    private int inputFileKey;
    private bool isBusy;
    private string progressMessage = string.Empty;
    private string? errorMessage;
    private RoutePlanResult? result;
    private CancellationTokenSource? cts;
    private ElementReference dropZoneRef;
    private bool isDropZoneInitialized;
    private bool isMapRendered;

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
        isMapRendered = false;
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

            var savedName = $"visits-{DateTime.Now:yyyyMMdd-HHmmss-fff}.csv";
            var savedPath = Path.Combine(uploadDir, savedName);

            await using (var dest = File.Create(savedPath))
            await using (var src = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024))
            {
                await src.CopyToAsync(dest, cts.Token);
            }

            Logger.InfoCsvUploaded(savedPath);
            Logger.InfoCheckStarted();

            result = await PlannerService.PlanAsync(savedPath, progress, cts.Token);

            Logger.InfoCheckCompleted(result.Plan.VisitCount);
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
            errorMessage = $"足順生成エラー: {ex.Message}";
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
        isMapRendered = false;
        inputFileKey++;
        return Task.CompletedTask;
    }

    private static List<MapPoint> BuildMapPoints(RoutePlanResult planResult)
    {
        var points = new List<MapPoint>();
        foreach (var stop in planResult.Plan.Stops)
        {
            if (stop.Kind == StopKind.Lunch)
            {
                continue;
            }

            double latitude;
            double longitude;
            if (stop.Kind == StopKind.Office)
            {
                latitude = planResult.Common.OfficeLatitude;
                longitude = planResult.Common.OfficeLongitude;
            }
            else if (stop.Visit is not null)
            {
                latitude = stop.Visit.Latitude;
                longitude = stop.Visit.Longitude;
            }
            else
            {
                continue;
            }

            var hasWindow = stop.Visit is { } v && (v.WindowStart is not null || v.WindowEnd is not null);

            points.Add(new MapPoint(
                stop.Order,
                stop.Label,
                latitude,
                longitude,
                stop.Kind.ToString(),
                stop.WindowViolation,
                FormatTime(stop.Arrival),
                FormatTime(stop.Departure),
                hasWindow,
                hasWindow ? GetWindowLabel(stop.Visit) : string.Empty));
        }

        return points;
    }

    private static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string GetFeasibilityClass(int score) => score switch
    {
        >= 80 => "risk-low",
        >= 50 => "risk-medium",
        _ => "risk-high"
    };

    private static string GetFeasibilityCaption(int score) => score switch
    {
        >= 80 => "無理のない実行可能な足順です。",
        >= 50 => "いくつか調整した方がよい点があります。",
        _ => "時間帯や残業の制約に無理があり、再検討が必要です。"
    };

    private static string GetSeverityClass(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH" => "risk-high",
        "MEDIUM" => "risk-medium",
        _ => "risk-low"
    };

    private static string GetSeverityLabel(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH" => "高",
        "MEDIUM" => "中",
        _ => "低"
    };

    private static string GetStopRowClass(RouteStop stop)
    {
        if (stop.WindowViolation)
        {
            return "ic-row-high";
        }

        return stop.Kind == StopKind.Visit ? string.Empty : "ic-row-medium";
    }

    private static string GetKindLabel(StopKind kind) => kind switch
    {
        StopKind.Office => "拠点",
        StopKind.Lunch => "休憩",
        _ => "訪問"
    };

    private static string GetCategoryLabel(VisitCategory? category) => category switch
    {
        VisitCategory.Business => "業務",
        VisitCategory.General => "一般",
        _ => "-"
    };

    private static string GetPriorityLabel(VisitPriority priority) => priority switch
    {
        VisitPriority.High => "高",
        VisitPriority.Low => "低",
        _ => "中"
    };

    private static string GetWindowLabel(VisitTarget? visit)
    {
        if (visit is null || (visit.WindowStart is null && visit.WindowEnd is null))
        {
            return "指定なし";
        }

        var start = visit.WindowStart?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
        var end = visit.WindowEnd?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
        var strict = visit.WindowStrict == TimeWindowStrictness.Strict ? "厳守" : "希望";
        return $"{start}–{end}（{strict}）";
    }

    private static string GetActionLabel(string action) => action.ToUpperInvariant() switch
    {
        "REORDER" => "並べ替え",
        "MOVE_EARLIER" => "前倒し",
        "MOVE_LATER" => "後ろ倒し",
        "CALL_AHEAD" => "事前連絡",
        "SPLIT_DAY" => "日跨ぎ分割",
        _ => action
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!isDropZoneInitialized)
        {
            await JsRuntime.InvokeVoidAsync("initDropZone", dropZoneRef);
            isDropZoneInitialized = true;
        }

        if (result is not null && !isMapRendered)
        {
            await JsRuntime.InvokeVoidAsync("routeMap.render", "route-map", BuildMapPoints(result));
            isMapRendered = true;
        }
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

    // 地図JS(routemap.js)へ JSON 連携する地点。各プロパティは JS 側で参照する（camelCase で送信）。
    // ReSharper disable NotAccessedPositionalProperty.Local
    private sealed record MapPoint(
        int Order,
        string Label,
        double Lat,
        double Lng,
        string Kind,
        bool Violation,
        string Arrival,
        string Departure,
        bool HasWindow,
        string Window);
    // ReSharper restore NotAccessedPositionalProperty.Local
}
