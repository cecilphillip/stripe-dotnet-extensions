using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe.Extensions.DependencyInjection;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Maps endpoints that dispatch Stripe v2 event notifications to registered
/// <see cref="IStripeEventSubscriber{TNotification}"/> implementations.
/// </summary>
public static class StripeEventNotificationEndpointExtensions
{
    private const string LoggerCategory = "Stripe.Extensions.AspNetCore.StripeEventNotifications";

    // Minimal API's MapPost(IEndpointRouteBuilder, string, Delegate) is itself annotated
    // [RequiresUnreferencedCode] and [RequiresDynamicCode], so no endpoint mapped this way can be
    // trim- or AOT-safe regardless of what this library does. Subscriber dispatch additionally
    // reflects over the SDK's events and closes a generic type per notification. Declaring that
    // here turns a runtime failure in a trimmed or AOT-published app into a build-time warning.
    private const string TrimmingMessage =
        "Stripe event notification endpoints use reflection over subscriber types and the Stripe " +
        "SDK's notification events, and Minimal API endpoint mapping itself requires unreferenced " +
        "code. This API is not compatible with trimming.";

    private const string AotMessage =
        "Stripe event notification endpoints construct a generic binder per notification type at " +
        "runtime, and Minimal API endpoint mapping itself requires dynamic code. This API is not " +
        "compatible with native AOT.";

    private sealed record Binding(StripeNotificationBinder Binder, IReadOnlyList<Type> SubscriberTypes);

    /// <summary>
    /// Maps a signature-verified event notification endpoint using the default Stripe configuration.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The route pattern. Defaults to <c>/stripe/thin-events</c>.</param>
    /// <param name="configure">Optional endpoint options.</param>
    [RequiresDynamicCode(AotMessage)]
    public static IEndpointConventionBuilder MapStripeEventNotifications(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/stripe/thin-events",
        Action<StripeEventNotificationOptions>? configure = null)
        => endpoints.MapStripeEventNotifications(
            pattern,
            StripeOptions.DefaultClientConfigurationSectionName,
            configure);

    /// <summary>
    /// Maps a signature-verified event notification endpoint using a named Stripe configuration.
    /// </summary>
    [RequiresDynamicCode(AotMessage)]
    public static IEndpointConventionBuilder MapStripeEventNotifications(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string namedConfiguration,
        Action<StripeEventNotificationOptions>? configure = null)
        => MapCore(endpoints, pattern, namedConfiguration, configure, verifySignature: true);

    /// <summary>
    /// Maps an event notification endpoint that does <b>not</b> verify Stripe signatures.
    /// </summary>
    /// <remarks>
    /// Intended for pre-authenticated cloud transports. The request body must be an
    /// <b>AWS EventBridge</b> envelope (a <c>detail</c> property wrapping the thin event) or a
    /// <b>CloudEvents 1.0</b> envelope (a <c>specversion</c> property alongside <c>data</c>), which
    /// is the schema Azure Event Grid delivers. A raw Stripe webhook body is rejected here; use
    /// <see cref="MapStripeEventNotifications(IEndpointRouteBuilder, string, string, Action{StripeEventNotificationOptions})"/>
    /// for deliveries that come directly from Stripe.
    /// <para>
    /// <b>Security warning:</b> this endpoint accepts any well-formed envelope. Never expose it on a
    /// publicly reachable route without independent authentication, for example
    /// <c>.RequireAuthorization(...)</c> or network-level restrictions. There is deliberately no
    /// default route and no boolean switch on the verified overload, so this cannot be enabled by
    /// accident.
    /// </para>
    /// </remarks>
    [RequiresDynamicCode(AotMessage)]
    public static IEndpointConventionBuilder MapStripeEventNotificationsWithoutSignatureVerification(
        this IEndpointRouteBuilder endpoints,
        string pattern,
        string namedConfiguration = StripeOptions.DefaultClientConfigurationSectionName,
        Action<StripeEventNotificationOptions>? configure = null)
        => MapCore(endpoints, pattern, namedConfiguration, configure, verifySignature: false);

    [RequiresDynamicCode(AotMessage)]
    private static IEndpointConventionBuilder MapCore(
        IEndpointRouteBuilder endpoints,
        string pattern,
        string namedConfiguration,
        Action<StripeEventNotificationOptions>? configure,
        bool verifySignature)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(namedConfiguration);
        StripeSerializationGuard.EnsureReflectionSerializationEnabled();

        var options = new StripeEventNotificationOptions();
        configure?.Invoke(options);

        var registry = endpoints.ServiceProvider.GetService<StripeEventNotificationSubscriberRegistry>();
        if (registry is null || registry.IsEmpty)
        {
            throw new InvalidOperationException(
                "No Stripe event notification subscribers are registered. Call " +
                "services.AddStripeEventSubscriber<TSubscriber>() before mapping the endpoint.");
        }

        // Binders are resolved once, at startup, and reused for every request.
        var bindings = registry.Entries
            .Select(entry => new Binding(StripeNotificationBinder.For(entry.NotificationType), entry.SubscriberTypes))
            .ToList();

        return endpoints.MapPost(
            pattern,
            CreateDelegate(namedConfiguration, options, bindings, registry.UnhandledSubscribers, verifySignature));
    }

    // Returns a RequestDelegate rather than a Delegate on purpose. MapPost's Delegate overload
    // binds parameters and results reflectively and is annotated
    // RequiresUnreferencedCode/RequiresDynamicCode; the RequestDelegate overload is neither, and
    // endpoint filters still run against it.
    private static RequestDelegate CreateDelegate(
        string namedConfiguration,
        StripeEventNotificationOptions options,
        IReadOnlyList<Binding> bindings,
        IReadOnlyList<Type> unhandledSubscribers,
        bool verifySignature)
    {
        return HandleAsync;

        async Task HandleAsync(HttpContext http)
        {
            var services = http.RequestServices;
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
            var client = services.GetRequiredKeyedService<StripeClient>(namedConfiguration);
            var stripeOptions = services.GetRequiredService<IOptionsSnapshot<StripeOptions>>().Get(namedConfiguration);
            var cancellationToken = http.RequestAborted;

            // Published before anything can fail so filters and middleware always find it.
            var result = new StripeEventNotificationResult();
            http.Features.Set(result);

            string signatureHeader = string.Empty;
            if (verifySignature)
            {
                if (string.IsNullOrEmpty(stripeOptions.WebhookSecret))
                {
                    var missingSecret = new InvalidOperationException(
                        "WebhookSecret is required to validate events. " +
                        "You can set it using the Stripe:WebhookSecret configuration section or " +
                        "by passing the value to .AddStripe(o => o.WebhookSecret = \"your_secret\") call");
                    logger.WebhookSecretValidationFailed(missingSecret);
                    result.Exception = missingSecret;
                    await Results.BadRequest().ExecuteAsync(http).ConfigureAwait(false);
                    return;
                }

                signatureHeader = http.Request.Headers["Stripe-Signature"].ToString();
                if (string.IsNullOrEmpty(signatureHeader))
                {
                    var missingHeader = new StripeException("Missing Stripe-Signature header");
                    logger.EventParsingError(missingHeader);
                    result.Exception = missingHeader;
                    await Results.BadRequest().ExecuteAsync(http).ConfigureAwait(false);
                    return;
                }
            }

            string body;
            try
            {
                http.Request.EnableBuffering();
                using var reader = new StreamReader(http.Request.Body, leaveOpen: true);
                body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                http.Request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                logger.EventParsingError(ex);
                result.Exception = ex;
                await Results.BadRequest().ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            // Every callback records its work here instead of running as `async void`, so the
            // response is not sent until all subscribers complete and their failures stay observable.
            var sink = new AsyncCallbackSink();

            // The SDK dispatches synchronously, so an asynchronous pre-dispatch decision cannot be
            // awaited before dispatch. It is started during PreHandle and every dispatch path
            // awaits it instead, which is equivalent: no subscriber observes the notification
            // until the gate resolves.
            Task<bool>? gate = null;

            static async Task GatedAsync(Task<bool>? gate, Func<Task> work)
            {
                if (gate is not null)
                {
                    bool dispatch;
                    try
                    {
                        dispatch = await gate.ConfigureAwait(false);
                    }
                    catch
                    {
                        // The gate task is recorded in the sink in its own right; rethrowing here
                        // would repeat the same failure once per subscriber in the aggregate.
                        return;
                    }

                    if (!dispatch)
                    {
                        return;
                    }
                }

                await work().ConfigureAwait(false);
            }

            void Fallback(object _, StripeUnhandledEventNotificationEventArgs args)
            {
                if (unhandledSubscribers.Count == 0)
                {
                    var type = args.EventNotification.Type;
                    var isKnown = args.Details.IsKnownEventType;
                    sink.Run(() => GatedAsync(gate, () =>
                    {
                        logger.UnhandledNotification(type, isKnown, null);
                        return Task.CompletedTask;
                    }));
                    return;
                }

                var unhandledContext = new StripeUnhandledEventNotificationContext(
                    http, args.EventNotification, args.Client, args.Details);

                // One sink entry per subscriber, matching the typed path: a failure in one does not
                // prevent the others from running, and every failure is preserved in the aggregate.
                foreach (var subscriberType in unhandledSubscribers)
                {
                    var type = subscriberType;
                    sink.Run(() => GatedAsync(gate, async () =>
                    {
                        var subscriber = (IStripeUnhandledEventSubscriber)
                            http.RequestServices.GetRequiredService(type);
                        await subscriber
                            .HandleAsync(unhandledContext, cancellationToken)
                            .ConfigureAwait(false);
                    }));
                }
            }

            // A fresh handler per request. The SDK forbids re-registering callbacks after its first
            // Handle call, forbids two callbacks for one event type, and forbids removal, so a
            // short-lived instance keeps those constraints unreachable.
            StripeEventNotificationHandlerBase handler = verifySignature
                ? client.NotificationHandler(stripeOptions.WebhookSecret!, Fallback)
                : client.NotificationHandlerWithoutVerification(Fallback);

            // Always attached, even with no user gate, so the event type is known for error logging.
            handler.PreHandle += (_, args) =>
            {
                result.EventType = args.EventNotification.Type;
                result.EventId = args.EventNotification.Id;

                if (options.ShouldDispatchAsync is null)
                {
                    return;
                }

                var filterContext = new StripeEventNotificationFilterContext(
                    http, args.EventNotification, args.Client);
                try
                {
                    gate = options.ShouldDispatchAsync(filterContext, cancellationToken).AsTask();
                }
                catch (Exception ex)
                {
                    gate = Task.FromException<bool>(ex);
                }
            };

            foreach (var binding in bindings)
            {
                var current = binding;
                current.Binder.Attach(handler, (notification, eventClient) =>
                {
                    var context = new StripeEventNotificationContext(http, stripeOptions, eventClient);

                    // One sink entry per subscriber so a failure in one does not prevent the others
                    // from running, and every failure is preserved in the aggregate.
                    foreach (var subscriberType in current.SubscriberTypes)
                    {
                        var type = subscriberType;
                        sink.Run(() => GatedAsync(gate, async () =>
                        {
                            var subscriber = http.RequestServices.GetRequiredService(type);
                            await current.Binder
                                .InvokeAsync(subscriber, notification, context, cancellationToken)
                                .ConfigureAwait(false);
                        }));
                    }
                });
            }

            try
            {
                switch (handler)
                {
                    case StripeEventNotificationHandler verified:
                        verified.Handle(body, signatureHeader);
                        break;
                    case StripeEventNotificationHandlerWithoutVerification unverified:
                        unverified.Handle(body);
                        break;
                }
            }
            catch (Exception ex)
            {
                // Signature failures raise StripeException, but malformed JSON raises
                // JsonReaderException and a v1 "fat" event raises ArgumentException. All of these
                // are bad input and must map to 400: a 500 would make Stripe retry indefinitely.
                logger.EventParsingError(ex);
                result.Exception = ex;
                await Results.BadRequest().ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            // Recorded in its own right so a gate that fails is observed even when nothing was
            // dispatched, which is the case when no subscriber matched the event.
            if (gate is not null)
            {
                sink.Run(() => gate);
            }

            try
            {
                await sink.DrainAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.ExecutionError(result.EventType ?? "unknown", ex);
                result.Outcome = StripeEventNotificationOutcome.Failed;
                result.Exception = ex;
                await Results.StatusCode(StatusCodes.Status500InternalServerError)
                    .ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            result.Outcome = gate is { IsCompletedSuccessfully: true, Result: false }
                ? StripeEventNotificationOutcome.Skipped
                : StripeEventNotificationOutcome.Dispatched;

            await Results.Accepted().ExecuteAsync(http).ConfigureAwait(false);
        }
    }
}
