using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stripe.Extensions.DependencyInjection;

namespace Stripe.Extensions.AspNetCore;

public class StripeWebhookContext(HttpContext httpContext, StripeOptions stripeOptions, StripeClient? stripeClient, ILoggerFactory loggerFactory)
{
    public HttpContext HttpContext { get; } = httpContext;
    public StripeOptions StripeOptions { get; } = stripeOptions;
    public StripeClient? Client { get; } = stripeClient;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
}
