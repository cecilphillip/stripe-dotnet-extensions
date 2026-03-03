using Microsoft.Extensions.Logging;

namespace Stripe.Extensions.AspNetCore.Tests;

internal static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddPassThrough(this ILoggingBuilder builder, ILogger? logger)
    {
        if (logger is not null)
            builder.AddProvider(new PassThroughLoggerProvider(logger));

        return builder;
    }
}

internal class PassThroughLoggerProvider(ILogger logger) : ILoggerProvider
{
    private bool _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return logger;
    }

    public void Dispose() => _disposed = true;
}
