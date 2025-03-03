using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace OverlayPDF;

public class CustomConsoleFormatter() : ConsoleFormatter("custom")
{
    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var logLevel = logEntry.LogLevel;
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        var logLevelColor = GetLogLevelColor(logLevel);

        var logBuilder = new StringBuilder();
        logBuilder.Append($"{logLevel}: {logLevelColor}{message}\e[0m");

        textWriter.WriteLine(logBuilder.ToString());
    }

    private static string GetLogLevelColor(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "\e[37m", // White
            LogLevel.Debug => "\e[36m", // Cyan
            LogLevel.Information => "\e[32m", // Green
            LogLevel.Warning => "\e[33m", // Yellow
            LogLevel.Error => "\e[31m", // Red
            LogLevel.Critical => "\e[35m", // Magenta
            _ => "\e[0m", // Reset
        };
    }
}
