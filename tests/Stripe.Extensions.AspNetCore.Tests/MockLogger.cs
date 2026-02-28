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
    public ILogger CreateLogger(string categoryName) => logger;
    public void Dispose(){}
}
