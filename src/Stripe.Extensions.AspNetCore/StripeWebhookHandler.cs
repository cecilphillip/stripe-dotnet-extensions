using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Stripe.Extensions.AspNetCore;

public abstract partial class StripeWebhookHandler<T>(StripeWebhookContext context) : IStripeWebhookExecutor
{
    protected StripeWebhookContext Context => context;
    protected ILogger<T> Logger { get; } = context.LoggerFactory.CreateLogger<T>();
    
    public async Task<IResult> ExecuteAsync()
    {
        var httpContext = Context.HttpContext;
        Event stripeEvent;
        try
        {
            var options = Context.StripeOptions;
            if (string.IsNullOrEmpty(options.WebhookSecret))
            {
                var ex = new InvalidOperationException(
                    "WebhookSecret is required to validate events. " +
                    "You can set it using Stripe:WebhookSecret configuration section or " +
                    "by passing the value to .AddStripe(o => o.WebhookSecret = \"your_secret\") call");

                Logger.WebhookSecretValidationFailed(ex);
                throw ex;
            }

            httpContext.Request.EnableBuffering();
            using var stream = new StreamReader(httpContext.Request.Body, leaveOpen: true);
            var request = httpContext.Request;
            var body = await stream.ReadToEndAsync().ConfigureAwait(false);
            httpContext.Request.Body.Position = 0;

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