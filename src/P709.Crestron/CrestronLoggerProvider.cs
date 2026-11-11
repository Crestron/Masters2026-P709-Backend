using Crestron.SimplSharp;
using Microsoft.Extensions.Logging;

namespace P709.Crestron;

/// <summary>Writes log output to the Crestron console, and warnings and above to the processor error log.</summary>
internal sealed class CrestronLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CrestronLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class CrestronLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{DateTime.Now:HH:mm:ss} [{logLevel}] {category}: {formatter(state, exception)}";

            if (exception is not null)
            {
                message += Environment.NewLine + exception;
            }

            CrestronConsole.PrintLine(message);

            switch (logLevel)
            {
                // Remove Information if it gets too chatty
                case LogLevel.Information:
                    ErrorLog.Notice(message);
                    break;
                case LogLevel.Warning:
                    ErrorLog.Warn(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    ErrorLog.Error(message);
                    break;
            }
        }
    }
}
