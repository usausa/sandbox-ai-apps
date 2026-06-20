namespace FlyerChecker.Components.Pages;

using FlyerChecker.Models;
using FlyerChecker.Services;
using FlyerChecker.Settings;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

public sealed partial class Check : ComponentBase, IDisposable
{
    private static readonly string[] SupportedContentTypes = ["image/png", "image/jpeg", "image/webp"];

    [Inject]
    private FlyerCheckerService CheckerService { get; set; } = default!;

    [Inject]
    private ProductService ProductService { get; set; } = default!;

    [Inject]
    private MasterCsvLoader CsvLoader { get; set; } = default!;

    [Inject]
    private FlyerCheckerSettings Settings { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILogger<Check> Logger { get; set; } = default!;

    private int inputFileKey;
    private ElementReference dropZoneRef;
    private bool isDragOver;
    private bool isBusy;
    private string progressMessage = string.Empty;
    private string? errorMessage;
    private string? previewUrl;
    private readonly List<PriceCheckResult> results = [];
    private CancellationTokenSource? cts;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await JS.InvokeVoidAsync("initDropZone", dropZoneRef);
    }

    private void OnDragEnter() => isDragOver = true;
    private void OnDragLeave() => isDragOver = false;
    private void OnDrop(DragEventArgs args) => isDragOver = false;

    private async Task OnImageFileChangedAsync(InputFileChangeEventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        var file = e.File;
        var contentType = NormalizeContentType(file.ContentType, file.Name);
        if (contentType is null)
        {
            errorMessage = $"サポートされていないファイル形式です: {file.Name}";
            return;
        }

        cts = new CancellationTokenSource();

#pragma warning disable CA1031
        try
        {
            // ファイルをサーバーに保存 (StateHasChanged前に完了させる)
            var uploadDir = Path.GetFullPath(Settings.UploadPath);
            Directory.CreateDirectory(uploadDir);

            var ext = ExtensionFromContentType(contentType);
            var savedName = $"flyer-{DateTime.Now:yyyyMMdd-HHmmss-fff}{ext}";
            var savedPath = Path.Combine(uploadDir, savedName);

            await using (var dest = File.Create(savedPath))
            await using (var src = file.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024))
            {
                await src.CopyToAsync(dest, cts.Token);
            }

            Logger.InfoImageUploaded(savedPath);

            // 保存完了後にUIを更新
            isBusy = true;
            errorMessage = null;
            results.Clear();
            previewUrl = $"/uploads/{savedName}";
            progressMessage = "チラシを解析中...";
            StateHasChanged();

            progressMessage = "チラシを解析中...";
            StateHasChanged();

            Logger.InfoCheckStarted();

            await foreach (var result in CheckerService.CheckAsync(savedPath, cancellationToken: cts.Token))
            {
                results.Add(result);
                progressMessage = $"チェック中... {results.Count} 件処理済み";
                await InvokeAsync(StateHasChanged);
            }

            Logger.InfoCheckCompleted(results.Count);
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
            errorMessage = $"エラーが発生しました: {ex.Message}";
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

    private async Task OnCsvFileChangedAsync(InputFileChangeEventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        errorMessage = null;
        progressMessage = "CSVを読み込み中...";
        StateHasChanged();

        cts = new CancellationTokenSource();

#pragma warning disable CA1031
        try
        {
            Logger.InfoCsvLoadStarted();

            using var ms = new MemoryStream();
            await using (var stream = e.File.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024))
            {
                await stream.CopyToAsync(ms, cts.Token);
            }

            ms.Position = 0;

            progressMessage = "AI Searchに登録中...";
            await InvokeAsync(StateHasChanged);

            await ProductService.RecreateIndexAsync(cts.Token);
            var count = await ProductService.RegisterProductsAsync(CsvLoader.LoadAsync(ms, cts.Token), cts.Token);

            Logger.InfoCsvLoadCompleted(count);
            progressMessage = $"登録完了: {count} 件";
            await InvokeAsync(StateHasChanged);
            await Task.Delay(2000, cts.Token);
        }
        catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
        {
            Logger.InfoCheckCancelled();
        }
        catch (Exception ex)
        {
            Logger.ErrorCsvLoadFailed(ex, ex.Message);
            errorMessage = $"CSV登録エラー: {ex.Message}";
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
        previewUrl = null;
        results.Clear();
        errorMessage = null;
        inputFileKey++;
        return Task.CompletedTask;
    }

    private static string? NormalizeContentType(string contentType, string fileName)
    {
        if (SupportedContentTypes.Contains(contentType))
        {
            return contentType;
        }

        var ext = Path.GetExtension(fileName).ToUpperInvariant();
        return ext switch
        {
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".WEBP" => "image/webp",
            _ => null
        };
    }

    private static string ExtensionFromContentType(string contentType) => contentType switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg"
    };

    public void Dispose()
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
