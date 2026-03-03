using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Base class for handling Stripe thin event notifications (v2 events).
/// Inherit from this class and override the generated On*Async methods to handle specific event types.
/// </summary>
/// <typeparam name="T">The derived handler type (for logging purposes).</typeparam>
public abstract partial class StripeThinEventHandler<T>(StripeWebhookContext context) : IStripeWebhookExecutor
{
    protected StripeWebhookContext Context => context;
    protected ILogger<T> Logger { get; } = context.LoggerFactory.CreateLogger<T>();

    /// <summary>
    /// Parses and executes the thin event notification from the incoming HTTP request.
    /// </summary>
    /// <returns>An IResult indicating success (202 Accepted) or failure (400/500).</returns>
    public async Task<IResult> ExecuteAsync()
    {
        var httpContext = Context.HttpContext;
        EventNotification eventNotification;

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
