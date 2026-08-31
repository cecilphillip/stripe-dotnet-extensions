using Microsoft.AspNetCore.Http;
using Stripe.Extensions.DependencyInjection;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Request and Stripe client state supplied to an
/// <see cref="IStripeEventSubscriber{TNotification}"/>.
/// </summary>
public sealed class StripeEventNotificationContext(
    HttpContext httpContext,
    StripeOptions stripeOptions,
    StripeClient client)
{
    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; } = httpContext;

    /// <summary>The resolved Stripe options for the endpoint's named configuration.</summary>
    public StripeOptions StripeOptions { get; } = stripeOptions;

    /// <summary>
    /// The Stripe client for this notification. This is the per-event client produced by the
    /// Stripe SDK, already bound to the notification's <see cref="StripeContext"/>, which makes it
    /// correct for Stripe Connect and v2 account-scoped calls.
    /// </summary>
    public StripeClient Client { get; } = client;

    /// <summary>Services scoped to the current request.</summary>
    public IServiceProvider RequestServices => HttpContext.RequestServices;
}

/// <summary>
/// State supplied to <see cref="StripeEventNotificationOptions.ShouldDispatchAsync"/>.
/// </summary>
public sealed class StripeEventNotificationFilterContext(
    HttpContext httpContext,
    EventNotification notification,
    StripeClient client)
{
    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; } = httpContext;

    /// <summary>The parsed notification, before dispatch.</summary>
    public EventNotification Notification { get; } = notification;

    /// <summary>The Stripe client associated with the notification.</summary>
    public StripeClient Client { get; } = client;

    /// <summary>Services scoped to the current request.</summary>
    public IServiceProvider RequestServices => HttpContext.RequestServices;
}

/// <summary>
/// State supplied when a notification arrives that no subscriber handles.
/// </summary>
public sealed class StripeUnhandledEventNotificationContext(
    HttpContext httpContext,
    EventNotification notification,
    StripeClient client,
    UnhandledNotificationDetails details)
{
    /// <summary>The current HTTP context.</summary>
    public HttpContext HttpContext { get; } = httpContext;

    /// <summary>The unhandled notification.</summary>
    public EventNotification Notification { get; } = notification;

    /// <summary>The Stripe client associated with the notification.</summary>
    public StripeClient Client { get; } = client;

    /// <summary>
    /// Details from the SDK. <see cref="UnhandledNotificationDetails.IsKnownEventType"/>
    /// distinguishes a known type that simply has no subscriber from an event type this SDK
    /// version does not recognise.
    /// </summary>
    public UnhandledNotificationDetails Details { get; } = details;

    /// <summary>Services scoped to the current request.</summary>
    public IServiceProvider RequestServices => HttpContext.RequestServices;
}
