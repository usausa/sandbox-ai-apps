namespace FlyerChecker;

using Microsoft.Extensions.Logging;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check UI started.")]
    public static partial void InfoCheckStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check UI completed. count=[{Count}]")]
    public static partial void InfoCheckCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flyer check cancelled by user.")]
    public static partial void InfoCheckCancelled(this ILogger log);

    [LoggerMessage(Level = LogLevel.Error, Message = "Flyer check failed. message=[{Message}]")]
    public static partial void ErrorCheckFailed(this ILogger log, Exception ex, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV load started.")]
    public static partial void InfoCsvLoadStarted(this ILogger log);

    [LoggerMessage(Level = LogLevel.Information, Message = "CSV load completed. count=[{Count}]")]
    public static partial void InfoCsvLoadCompleted(this ILogger log, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "CSV load failed. message=[{Message}]")]
    public static partial void ErrorCsvLoadFailed(this ILogger log, Exception ex, string message);
}
