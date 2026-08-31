using Microsoft.Extensions.Logging;

namespace Stripe.Extensions.AspNetCore;

public static partial class StripeWebhookHandlerLogger
{
    public static readonly EventId EventParsingErrorId = new(1, nameof(EventParsingError));
    public static readonly EventId ExecutionErrorId = new(2, nameof(ExecutionError));
    public static readonly EventId UnknownEventId = new(3, nameof(UnknownEvent));
    public static readonly EventId UnhandledEventId = new(4, nameof(UnhandledEvent));
    public static readonly EventId WebhookSecretValidationFailedId = new(5, nameof(WebhookSecretValidationFailed));
    public static readonly EventId UnhandledNotificationId = new(6, nameof(UnhandledNotification));

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Exception occurred while parsing the Stripe WebHook event payload.")]
    public static partial void EventParsingError(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Exception occurred while executing event handler for {EventType}")]
    public static partial void ExecutionError(this ILogger logger, string eventType, Exception? ex);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Warning,
        Message = "Event type {EventType} is not supported by this version of the library, consider upgrading. " +
                  "You can override the UnknownEventAsync method to suppress this log message."
    )]
    public static partial void UnknownEvent(this ILogger logger, string eventType, Exception? ex);

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 4,
        Message =
            "Event type {EventType} does not have a handler. Override the {MethodName} method to handle the event.")]
    public static partial void UnhandledEvent(this ILogger logger, string eventType, string methodName, Exception? ex);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Webhook secret validation failed.")]
    public static partial void WebhookSecretValidationFailed(this ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Warning,
        Message = "Event notification {EventType} has no registered subscriber (known event type: {IsKnownEventType}). " +
                  "Register an IStripeEventSubscriber<T> for this event, or an " +
                  "IStripeUnhandledEventSubscriber to take over this log message.")]
    public static partial void UnhandledNotification(this ILogger logger, string eventType, bool isKnownEventType, Exception? ex);
}
