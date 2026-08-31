using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Handles a single strongly-typed Stripe v2 event notification.
/// </summary>
/// <remarks>
/// Implementations are resolved from the request's service scope, so ordinary constructor
/// injection works. Register them with
/// <c>services.AddStripeEventSubscriber&lt;TSubscriber&gt;()</c>.
/// <para>
/// Multiple subscribers may target the same notification type; they are invoked as a fan-out and
/// every failure is reported. Invocation order between subscribers is not specified.
/// </para>
/// </remarks>
/// <typeparam name="TNotification">The Stripe notification type this subscriber handles.</typeparam>
public interface IStripeEventSubscriber<in TNotification>
    where TNotification : EventNotification
{
    /// <summary>
    /// Handles the notification.
    /// </summary>
    /// <param name="notification">The strongly-typed notification.</param>
    /// <param name="context">Request and Stripe client state for this notification.</param>
    /// <param name="cancellationToken">Tied to the incoming HTTP request.</param>
    ValueTask HandleAsync(
        TNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken);
}
