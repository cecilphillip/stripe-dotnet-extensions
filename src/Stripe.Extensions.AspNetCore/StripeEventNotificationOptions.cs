namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Options for an event notification endpoint mapped with
/// <c>MapStripeEventNotifications</c>.
/// </summary>
/// <remarks>
/// There is deliberately no "post-handle" callback here. Observing the outcome of a request is
/// what endpoint filters and middleware are for, and the endpoint publishes a
/// <see cref="StripeEventNotificationResult"/> on <c>HttpContext.Features</c> so they have the
/// event type and outcome to work with. Only the pre-dispatch decision needs to live on the
/// endpoint, because it is the one thing that requires the parsed notification before any
/// subscriber runs.
/// </remarks>
public sealed class StripeEventNotificationOptions
{
    /// <summary>
    /// Optional gate invoked after the notification is parsed but before any subscriber runs.
    /// Return <see langword="true"/> to dispatch and <see langword="false"/> to skip; a skipped
    /// notification still returns 202 Accepted, so Stripe does not retry it.
    /// </summary>
    /// <remarks>
    /// The canonical use is a duplicate-delivery check, which is almost always I/O, so this is
    /// asynchronous:
    /// <code>
    /// options.ShouldDispatchAsync = async (context, cancellationToken) =>
    ///     await store.TryMarkSeenAsync(context.Notification.Id, cancellationToken);
    /// </code>
    /// Subscribers wait on this decision before running, and a failure here is reported as a
    /// subscriber failure (500) rather than a parse failure (400): the payload was valid, so
    /// letting Stripe retry it is the correct outcome.
    /// </remarks>
    public Func<StripeEventNotificationFilterContext, CancellationToken, ValueTask<bool>>?
        ShouldDispatchAsync
    { get; set; }
}
