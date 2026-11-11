namespace P709.Web.Infrastructure;

/// <summary>Forwards all log output to a caller-owned <see cref="ILoggerFactory"/> without taking ownership of it.</summary>
internal sealed class ForwardingLoggerProvider(ILoggerFactory loggerFactory) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => loggerFactory.CreateLogger(categoryName);

    public void Dispose()
    {
        // The factory belongs to the caller, so it is intentionally not disposed here.
    }
}
