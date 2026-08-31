using Stripe.Extensions.AspNetCore;

namespace SampleEventNotifications.Subscribers;

/// <summary>
/// Handles notifications that no typed subscriber claimed.
/// </summary>
/// <remarks>
/// This is not a catch-all — anything the typed subscribers handled never reaches it. Recording an
/// audit row here means a brand-new Stripe event type is captured on day one, before anyone has
/// written a subscriber for it.
/// </remarks>
public sealed class UnhandledEventAuditSubscriber(ILogger<UnhandledEventAuditSubscriber> logger)
    : IStripeUnhandledEventSubscriber
{
    public ValueTask HandleAsync(
        StripeUnhandledEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        // IsKnownEventType is false only when this Stripe.net version cannot type the event at all,
        // which is a precise signal that the SDK is behind the API.
        if (!context.Details.IsKnownEventType)
        {
            logger.LogWarning(
                "Received {EventType}, which this Stripe.net version does not recognise. Consider upgrading",
                context.Notification.Type);
        }

        logger.LogInformation(
            "Audit: {EventType} ({NotificationId}) had no typed subscriber; persisting for replay",
            context.Notification.Type,
            context.Notification.Id);

        return ValueTask.CompletedTask;
    }
}
