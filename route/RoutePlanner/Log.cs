namespace RoutePlanner;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    // Check UI

    [LoggerMessage(Level = LogLevel.Information, Message = "Route planner UI started.")]
    public static partial void InfoCheckStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route planner UI completed. visitCount=[{count}]")]
    public static partial void InfoCheckCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route planner UI cancelled by user.")]
    public static partial void InfoCheckCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Route planning failed. message=[{message}]")]
    public static partial void ErrorCheckFailed(this ILogger log, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Visit CSV uploaded. path=[{path}]")]
    public static partial void InfoCsvUploaded(this ILogger log, string path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Route review LLM response: {response}")]
    public static partial void DebugRouteLlmResponse(this ILogger log, string response);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route planning started. filePath=[{filePath}]")]
    public static partial void InfoRouteAnalysisStarted(this ILogger log, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Visit rows loaded. visitCount=[{visitCount}]")]
    public static partial void InfoVisitsLoaded(this ILogger log, int visitCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route optimized. visitCount=[{visitCount}], overtimeMinutes=[{overtime}], windowViolations=[{violations}], unassigned=[{unassigned}]")]
    public static partial void InfoRouteOptimized(this ILogger log, int visitCount, int overtime, int violations, int unassigned);

    [LoggerMessage(Level = LogLevel.Information, Message = "Route review completed. feasibilityScore=[{score}]")]
    public static partial void InfoRouteReviewCompleted(this ILogger log, int score);
}
