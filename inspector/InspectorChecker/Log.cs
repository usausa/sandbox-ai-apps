namespace InspectorChecker;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    // Check UI

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspector check UI started.")]
    public static partial void InfoCheckStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspector check UI completed. suspiciousDayCount=[{count}]")]
    public static partial void InfoCheckCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspector check cancelled by user.")]
    public static partial void InfoCheckCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Inspector check failed. message=[{message}]")]
    public static partial void ErrorCheckFailed(this ILogger log, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection CSV uploaded. path=[{path}]")]
    public static partial void InfoCsvUploaded(this ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Inspection LLM response: {response}")]
    public static partial void DebugInspectionLlmResponse(this ILogger log, string response);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection analysis started. filePath=[{filePath}]")]
    public static partial void InfoInspectionAnalysisStarted(this ILogger log, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection CSV rows loaded. rowCount=[{rowCount}], customerCount=[{customerCount}]")]
    public static partial void InfoInspectionRowsLoaded(this ILogger log, int rowCount, int customerCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection feature summary built. dayCount=[{dayCount}], repeatedTemplateCount=[{repeatedTemplateCount}]")]
    public static partial void InfoFeatureSummaryBuilt(this ILogger log, int dayCount, int repeatedTemplateCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Inspection analysis completed. overallScore=[{overallScore}]")]
    public static partial void InfoInspectionAnalysisCompleted(this ILogger log, int overallScore);

    [LoggerMessage(Level = LogLevel.Information, Message = "Token usage. input=[{input}], output=[{output}], total=[{total}]")]
    public static partial void InfoTokenUsage(this ILogger log, long input, long output, long total);
}
