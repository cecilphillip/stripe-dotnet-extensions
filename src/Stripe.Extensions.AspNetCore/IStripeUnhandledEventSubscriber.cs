namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Handles notifications that no <see cref="IStripeEventSubscriber{TNotification}"/> claimed.
/// </summary>
/// <remarks>
/// <para>
/// This is not a catch-all. A notification handled by a typed subscriber never reaches an
/// implementation of this interface. It is the place for work that must happen for events you have
/// not written a subscriber for: audit trails, replay queues, or alerting when Stripe starts sending
/// something new.
/// </para>
/// <para>
/// Implementations are resolved per request from dependency injection, and several may be registered.
/// Each runs independently, so one failing does not prevent the others; any failure results in a
/// 500 response so Stripe retries.
/// </para>
/// <para>
/// To act only on events the installed Stripe.net version cannot type at all, branch on
/// <see cref="UnhandledNotificationDetails.IsKnownEventType"/>. That flag is exactly equivalent to
/// the notification being an <c>UnknownEventNotification</c>.
/// </para>
/// </remarks>
public interface IStripeUnhandledEventSubscriber
{
    /// <summary>Handles a notification that no typed subscriber claimed.</summary>
    ValueTask HandleAsync(
        StripeUnhandledEventNotificationContext context,
        CancellationToken cancellationToken);
}
