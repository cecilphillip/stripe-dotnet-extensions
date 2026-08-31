using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Stripe.Extensions.DependencyInjection;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Base class for handling Stripe thin event notifications (v2 events).
/// Inherit from this class and override the generated On*Async methods to handle specific event types.
/// </summary>
/// <typeparam name="T">The derived handler type (for logging purposes).</typeparam>
/// <remarks>
/// Obsolete. Use <c>AddStripeEventSubscriber</c> with <c>IStripeEventSubscriber&lt;TNotification&gt;</c>
/// and map the endpoint with <c>MapStripeEventNotifications</c> instead.
/// <para>
/// This type dispatches from a vendored OpenAPI spec, which covers 20 thin event types. The
/// subscriber model dispatches from the Stripe.net SDK's own notification types, which cover 24 —
/// including the <c>v2.core.commerce.product_catalog.imports.*</c> events that are unreachable here.
/// Subscribers are also individually injectable and testable, rather than collecting every event
/// on one class.
/// </para>
/// </remarks>
[Obsolete("Use IStripeEventSubscriber<TNotification> with MapStripeEventNotifications instead. " +
          "StripeThinEventHandler<T> covers fewer event types and puts every event on one class. " +
          "See the 'Event subscribers (v2 thin events)' section of the README.")]
public abstract partial class StripeThinEventHandler<T>(StripeWebhookContext context) : StripeWebhookHandlerBase<T>(context)
{
    /// <summary>
    /// Parses and executes the thin event notification from the incoming HTTP request.
    /// </summary>
    /// <returns>An IResult indicating success (202 Accepted) or failure (400/500).</returns>
    protected override async Task<IResult> ParseAndDispatchAsync(string body, HttpRequest request, StripeOptions options)
    {
        EventNotification eventNotification;
        try
        {
            var signatureHeader = request.Headers["Stripe-Signature"].ToString();
            if (string.IsNullOrEmpty(signatureHeader))
            {
                throw new StripeException("Missing Stripe-Signature header");
            }

            eventNotification = Context.Client.ParseEventNotification(
                body,
                signatureHeader,
                options.WebhookSecret);
        }
        catch (Exception e)
        {
            Logger.EventParsingError(e);
            return Results.BadRequest();
        }

        try
        {
            await ExecuteAsync(eventNotification).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (Exception e)
        {
            Logger.ExecutionError(eventNotification.Type, e);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private Task UnhandledEventAsync(EventNotification notification,
        [CallerMemberName] string? handlerMethod = null)
    {
        Logger.UnhandledEvent(notification.Type, handlerMethod ?? "<unknown>", null);
        return Task.CompletedTask;
    }

    protected virtual Task UnknownEventAsync(EventNotification notification)
    {
        Logger.UnknownEvent(notification.Type, null);
        return Task.CompletedTask;
    }
}
