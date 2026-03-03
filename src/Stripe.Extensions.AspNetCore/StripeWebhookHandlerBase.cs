using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stripe.Extensions.DependencyInjection;

namespace Stripe.Extensions.AspNetCore;

public abstract class StripeWebhookHandlerBase<T>(StripeWebhookContext context) : IStripeWebhookExecutor
{
    protected StripeWebhookContext Context => context;
    protected ILogger<T> Logger { get; } = context.LoggerFactory.CreateLogger<T>();

    public async Task<IResult> ExecuteAsync()
    {
        var httpContext = Context.HttpContext;
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
            var body = await stream.ReadToEndAsync().ConfigureAwait(false);
            httpContext.Request.Body.Position = 0;

            return await ParseAndDispatchAsync(body, httpContext.Request, options).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.EventParsingError(e);
            return Results.BadRequest();
        }
    }

    protected abstract Task<IResult> ParseAndDispatchAsync(string body, HttpRequest request, StripeOptions options);
}
