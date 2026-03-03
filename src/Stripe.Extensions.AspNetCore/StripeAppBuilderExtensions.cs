using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe.Extensions.DependencyInjection;

namespace Stripe.Extensions.AspNetCore;

public static class StripeAppBuilderExtensions
{
    public static IEndpointRouteBuilder MapStripeWebhookHandler<T>(this IEndpointRouteBuilder endpointRouteBuilder,
        string pattern = "/stripe/webhook")
        where T : StripeWebhookHandler<T>
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return endpointRouteBuilder.MapStripeWebhookHandler<T>(pattern,
            StripeOptions.DefaultClientConfigurationSectionName);
    }

    public static IEndpointRouteBuilder MapStripeWebhookHandler<T>(this IEndpointRouteBuilder endpointRouteBuilder,
        string pattern, string namedConfiguration)
        where T : StripeWebhookHandler<T>
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(namedConfiguration);

        var handlerFactory = ActivatorUtilities.CreateFactory(typeof(T), [typeof(StripeWebhookContext)]);
        var requestDelegate = CreateWebhookDelegate<T>(namedConfiguration, handlerFactory);
        endpointRouteBuilder.MapPost(pattern, requestDelegate);

        return endpointRouteBuilder;
    }

    /// <summary>
    /// Maps a thin event webhook handler to the specified route pattern.
    /// </summary>
    /// <typeparam name="T">The thin event handler type.</typeparam>
    /// <param name="endpointRouteBuilder">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern. Defaults to "/stripe/thin-event".</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapStripeThinEventHandler<T>(this IEndpointRouteBuilder endpointRouteBuilder,
        string pattern = "/stripe/thin-event")
        where T : StripeThinEventHandler<T>
    {
        ArgumentNullException.ThrowIfNull(pattern);

        return endpointRouteBuilder.MapStripeThinEventHandler<T>(pattern,
            StripeOptions.DefaultClientConfigurationSectionName);
    }

    /// <summary>
    /// Maps a thin event webhook handler to the specified route pattern with a named configuration.
    /// </summary>
    /// <typeparam name="T">The thin event handler type.</typeparam>
    /// <param name="endpointRouteBuilder">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern.</param>
    /// <param name="namedConfiguration">The named Stripe configuration to use.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapStripeThinEventHandler<T>(this IEndpointRouteBuilder endpointRouteBuilder,
        string pattern, string namedConfiguration)
        where T : StripeThinEventHandler<T>
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(namedConfiguration);

        var handlerFactory = ActivatorUtilities.CreateFactory(typeof(T), [typeof(StripeWebhookContext)]);
        var requestDelegate = CreateWebhookDelegate<T>(namedConfiguration, handlerFactory);
        endpointRouteBuilder.MapPost(pattern, requestDelegate);

        return endpointRouteBuilder;
    }

    private static Delegate CreateWebhookDelegate<T>(
        string namedConfiguration,
        ObjectFactory handlerFactory)
        where T : class, IStripeWebhookExecutor
    {
        return async (HttpContext context) =>
        {
            var stripeClient = context.RequestServices.GetRequiredKeyedService<StripeClient>(namedConfiguration);
            var options = context.RequestServices.GetRequiredService<IOptionsSnapshot<StripeOptions>>()
                .Get(namedConfiguration);
            var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();

            var stripeWebhookContext = new StripeWebhookContext(context, options, stripeClient, loggerFactory);
            var handler = (IStripeWebhookExecutor)handlerFactory(context.RequestServices, [stripeWebhookContext]);
            var result = await handler.ExecuteAsync().ConfigureAwait(false);
            return result;
        };
    }
}
