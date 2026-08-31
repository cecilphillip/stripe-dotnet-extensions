using Stripe.Events;
using Stripe.Extensions.AspNetCore;

namespace SampleEventNotifications.Subscribers;

/// <summary>
/// A second, independent subscriber for the <em>same</em> notification type as
/// <see cref="AccountProvisioningSubscriber"/>.
/// </summary>
/// <remarks>
/// This is the fan-out case. The Stripe SDK refuses to register two callbacks for one event type,
/// so the library attaches a single adapter per event and multiplexes subscribers behind it. Both
/// subscribers run concurrently, and if both fail, both failures are reported rather than only the
/// first.
/// <para>
/// Practically, this lets unrelated concerns — provisioning and analytics — live in separate
/// classes owned by separate teams instead of accumulating in one giant handler method.
/// </para>
/// </remarks>
public sealed class AccountAnalyticsSubscriber(
    ILogger<AccountAnalyticsSubscriber> logger)
    : IStripeEventSubscriber<V2CoreAccountCreatedEventNotification>
{
    public ValueTask HandleAsync(
        V2CoreAccountCreatedEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Recording signup metric for {RelatedObjectId} (livemode: {Livemode}, created: {Created:O})",
            notification.RelatedObject?.Id ?? "(none)",
            notification.Livemode,
            notification.Created);

        return ValueTask.CompletedTask;
    }
}
