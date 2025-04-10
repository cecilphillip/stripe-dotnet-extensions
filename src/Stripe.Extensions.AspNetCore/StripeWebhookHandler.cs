using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Stripe.Extensions.AspNetCore;

public abstract partial class StripeWebhookHandler<T>(StripeWebhookContext context)
{
    protected StripeWebhookContext Context { get; } = context;
    protected ILogger<T> Logger => context.LoggerFactory.CreateLogger<T>();
    
    public async Task<IResult> ExecuteAsync()
    {
        var httpContext = Context.HttpContext;
        var response = httpContext.Response;
        
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

                Logger.WebhookSecretValidationFailed("Webhook Secret Validation Failed!", ex);
                throw ex;
            }
            
            using var stream = new StreamReader(httpContext.Request.Body);
            var request = httpContext.Request;
            var body = await stream.ReadToEndAsync();

            stripeEvent = EventUtility.ConstructEvent(
                body,
                request.Headers["Stripe-Signature"],
                options.WebhookSecret,
                300, // default tolerance
                options.ThrowOnWebhookApiVersionMismatch);
        }
        catch (Exception e)
        {
            Logger.EventParsingError(e);
            response.StatusCode = 400;
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
            response.StatusCode = 500;
            return Results.BadRequest();
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