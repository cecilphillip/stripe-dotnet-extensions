using Microsoft.AspNetCore.Http;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// What the endpoint did with a notification.
/// </summary>
public enum StripeEventNotificationOutcome
{
    /// <summary>The request was rejected before dispatch. The endpoint returned 400.</summary>
    Rejected,

    /// <summary>
    /// The notification parsed, but <see cref="StripeEventNotificationOptions.ShouldDispatchAsync"/>
    /// declined it. No subscriber ran and the endpoint returned 202.
    /// </summary>
    Skipped,

    /// <summary>Every subscriber completed successfully. The endpoint returned 202.</summary>
    Dispatched,

    /// <summary>At least one subscriber failed. The endpoint returned 500.</summary>
    Failed,
}

/// <summary>
/// The outcome of a single event notification request, published on
/// <see cref="HttpContext.Features"/> so that endpoint filters, middleware, and telemetry can
/// observe what the endpoint did.
/// </summary>
/// <remarks>
/// This is why the library has no "post-handle" callback. The endpoint returns an
/// <see cref="Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"/>, so
/// <c>AddEndpointFilter</c> already wraps it; publishing the result is the only thing a filter
/// could not work out for itself. Filters compose, resolve services, and can rewrite the response,
/// none of which a single callback property would offer.
/// <code>
/// app.MapStripeEventNotifications()
///    .AddEndpointFilter(async (context, next) =>
///    {
///        var response = await next(context);
///        var result = context.HttpContext.Features.Get&lt;StripeEventNotificationResult&gt;();
///        metrics.Record(result?.EventType, result?.Outcome);
///        return response;
///    });
/// </code>
/// The feature is set before the body is read, so it is always present after the endpoint runs,
/// even when the request is rejected. <see cref="EventType"/> and <see cref="EventId"/> stay
/// <see langword="null"/> when the payload could not be parsed.
/// </remarks>
public sealed class StripeEventNotificationResult
{
    /// <summary>The notification's event type, once parsed.</summary>
    public string? EventType { get; internal set; }

    /// <summary>The notification's event id, once parsed.</summary>
    public string? EventId { get; internal set; }

    /// <summary>What the endpoint did with the notification.</summary>
    public StripeEventNotificationOutcome Outcome { get; internal set; }
        = StripeEventNotificationOutcome.Rejected;

    /// <summary>
    /// The failure that produced the status code, when there was one. For
    /// <see cref="StripeEventNotificationOutcome.Failed"/> this is an
    /// <see cref="AggregateException"/> holding every subscriber failure, not just the first.
    /// </summary>
    public Exception? Exception { get; internal set; }
}
