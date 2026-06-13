namespace PosChecker;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    // Check UI

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos check UI started.")]
    public static partial void InfoCheckStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos check UI completed. suspiciousDayCount=[{count}]")]
    public static partial void InfoCheckCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos check cancelled by user.")]
    public static partial void InfoCheckCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pos check failed. message=[{message}]")]
    public static partial void ErrorCheckFailed(this ILogger log, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction CSV uploaded. path=[{path}]")]
    public static partial void InfoCsvUploaded(this ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pos LLM response: {response}")]
    public static partial void DebugPosLlmResponse(this ILogger log, string response);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos analysis started. filePath=[{filePath}]")]
    public static partial void InfoPosAnalysisStarted(this ILogger log, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transaction CSV rows loaded. rowCount=[{rowCount}], cashierCount=[{cashierCount}]")]
    public static partial void InfoPosRowsLoaded(this ILogger log, int rowCount, int cashierCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos feature summary built. dayCount=[{dayCount}], anomalyCount=[{anomalyCount}]")]
    public static partial void InfoFeatureSummaryBuilt(this ILogger log, int dayCount, int anomalyCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pos analysis completed. overallScore=[{overallScore}]")]
    public static partial void InfoPosAnalysisCompleted(this ILogger log, int overallScore);
}
