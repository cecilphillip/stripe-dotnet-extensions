using Stripe.Events;
using Stripe.Extensions.AspNetCore;

namespace SampleEventNotifications.Subscribers;

/// <summary>
/// Handles the ping Stripe sends to test an event destination.
/// </summary>
/// <remarks>
/// <para>
/// Subscribing to this event is <b>optional</b>. The endpoint already answers 202 for events no
/// subscriber claims, so a ping succeeds whether or not this class exists. It is here to show the
/// smallest possible subscriber, and to make endpoint reachability visible in the logs.
/// </para>
/// <para>
/// The ping is never sent by normal account activity. It only arrives when explicitly requested —
/// by pinging the event destination from the Stripe Dashboard, or via the
/// <c>/v2/core/event_destinations/:id/ping</c> API.
/// </para>
/// </remarks>
public sealed class EventDestinationPingSubscriber(
    ILogger<EventDestinationPingSubscriber> logger)
    : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
{
    public ValueTask HandleAsync(
        V2CoreEventDestinationPingEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Ping received from event destination. Endpoint is reachable.");
        return ValueTask.CompletedTask;
    }
}
