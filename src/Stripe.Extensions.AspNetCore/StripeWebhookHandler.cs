using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Stripe.Extensions.DependencyInjection;

namespace Stripe.Extensions.AspNetCore;

public abstract partial class StripeWebhookHandler<T>(StripeWebhookContext context) : StripeWebhookHandlerBase<T>(context)
{
    protected override async Task<IResult> ParseAndDispatchAsync(string body, HttpRequest request, StripeOptions options)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                body,
                request.Headers["Stripe-Signature"],
                options.WebhookSecret,
                options.WebhookTimestampTolerance,
                options.ThrowOnWebhookApiVersionMismatch);
        }
        catch (Exception e)
        {
            Logger.EventParsingError(e);
            return Results.BadRequest();
        }

        try
        {
            await ExecuteAsync(stripeEvent).ConfigureAwait(false);
            return Results.Accepted();
        }
        catch (Exception e)
        {
            Logger.ExecutionError(stripeEvent.Type, e);
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private Task UnhandledEventAsync(Event e,
        [CallerMemberName] string? handlerMethod = null)
    {
        Logger.UnhandledEvent(e.Type, handlerMethod ?? "<unknown>", null);
        return Task.CompletedTask;
    }

    protected virtual Task UnknownEventAsync(Event e)
    {
        Logger.UnknownEvent(e.Type, null);
        return Task.CompletedTask;
    }
}