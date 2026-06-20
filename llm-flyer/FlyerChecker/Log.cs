namespace FlyerChecker;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    // Check UI

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check UI started.")]
    public static partial void InfoCheckStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check UI completed. count=[{count}]")]
    public static partial void InfoCheckCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check cancelled by user.")]
    public static partial void InfoCheckCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Flyer check failed. message=[{message}]")]
    public static partial void ErrorCheckFailed(this ILogger log, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer image uploaded. path=[{path}]")]
    public static partial void InfoImageUploaded(this ILogger log, string path);

    // CSV

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV load started.")]
    public static partial void InfoCsvLoadStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV load completed. count=[{count}]")]
    public static partial void InfoCsvLoadCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "CSV load failed. message=[{message}]")]
    public static partial void ErrorCsvLoadFailed(this ILogger log, Exception ex, string message);

    // FlyerCheckerService

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check started. filePath=[{filePath}]")]
    public static partial void InfoFlyerCheckStarted(this ILogger log, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer items extracted. count=[{count}]")]
    public static partial void InfoFlyerItemsExtracted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Checked item=[{item}], flyerPrice=[{flyerPrice}], masterName=[{masterName}], masterPrice=[{masterPrice}], diff=[{diff}]")]
    public static partial void InfoCheckedItem(
        this ILogger log,
        string item,
        int flyerPrice,
        string masterName,
        int masterPrice,
        int diff);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check completed. itemCount=[{count}]")]
    public static partial void InfoFlyerCheckCompleted(this ILogger log, int count);

    // FlyerImageReader

    [LoggerMessage(Level = LogLevel.Debug, Message = "Flyer LLM response: {response}")]
    public static partial void DebugFlyerLlmResponse(this ILogger log, string response);

    // PriceDifferenceAnalyzer

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diff LLM response: {response}")]
    public static partial void DebugDiffLlmResponse(this ILogger log, string response);

    // ProductService

    [LoggerMessage(Level = LogLevel.Information, Message = "Product registered. id=[{id}], name=[{name}]")]
    public static partial void InfoProductRegistered(this ILogger log, string id, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Index recreated.")]
    public static partial void InfoIndexRecreated(this ILogger log);
}
