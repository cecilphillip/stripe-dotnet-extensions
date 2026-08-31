using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stripe;
using Stripe.Extensions.AspNetCore;
using Stripe.V2.Core;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration helpers for <see cref="IStripeEventSubscriber{TNotification}"/>.
/// </summary>
public static class StripeEventSubscriberServiceCollectionExtensions
{
    /// <summary>
    /// Registers a subscriber for one or more Stripe v2 event notification types, for notifications
    /// no typed subscriber claims, or both.
    /// </summary>
    /// <remarks>
    /// The subscriber is registered as a scoped service, so it participates in normal request-scoped
    /// dependency injection. Registering several subscribers for the same notification type is
    /// supported and results in fan-out.
    /// <para>
    /// <typeparamref name="TSubscriber"/> must implement
    /// <see cref="IStripeEventSubscriber{TNotification}"/> for at least one concrete notification
    /// type, or <see cref="IStripeUnhandledEventSubscriber"/>, or both.
    /// </para>
    /// </remarks>
    /// <typeparam name="TSubscriber">The subscriber implementation.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// <typeparamref name="TSubscriber"/> implements neither subscriber interface, targets the
    /// non-specific <see cref="EventNotification"/> or <c>UnknownEventNotification</c> types, or
    /// targets a notification the installed Stripe.net version does not expose an event for.
    /// </exception>
    public static IServiceCollection AddStripeEventSubscriber<
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        TSubscriber>(
        this IServiceCollection services)
        where TSubscriber : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var notificationTypes = typeof(TSubscriber)
            .GetInterfaces()
            .Where(i => i.IsGenericType
                        && i.GetGenericTypeDefinition() == typeof(IStripeEventSubscriber<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToArray();

        var handlesUnhandled = typeof(IStripeUnhandledEventSubscriber).IsAssignableFrom(typeof(TSubscriber));

        if (notificationTypes.Length == 0 && !handlesUnhandled)
        {
            throw new InvalidOperationException(
                $"'{typeof(TSubscriber).FullName}' does not implement " +
                $"'{typeof(IStripeEventSubscriber<>).Name}'. Implement it for at least one " +
                "Stripe notification type, for example " +
                "IStripeEventSubscriber<V2CoreAccountCreatedEventNotification>, or implement " +
                $"'{nameof(IStripeUnhandledEventSubscriber)}' to observe notifications no typed " +
                "subscriber claims.");
        }

        services.TryAddScoped<TSubscriber>();

        var registry = GetOrAddRegistry(services);

        if (handlesUnhandled)
        {
            registry.AddUnhandled(typeof(TSubscriber));
        }

        foreach (var notificationType in notificationTypes)
        {
            // Neither the base type nor the SDK's catch-all has a per-event CLR event, so a
            // subscriber for either could never be dispatched. Reject it rather than silently
            // redefining what the type parameter means: IStripeEventSubscriber<EventNotification>
            // reads as "every event", which is not a behaviour this library offers.
            if (notificationType == typeof(EventNotification)
                || notificationType == typeof(Stripe.Events.UnknownEventNotification))
            {
                throw new InvalidOperationException(
                    $"'{typeof(TSubscriber).FullName}' subscribes to '{notificationType.Name}', which is not " +
                    "a specific event type. Subscribers are dispatched per event type, so this would never " +
                    $"be invoked. Implement '{nameof(IStripeUnhandledEventSubscriber)}' to handle " +
                    "notifications that no typed subscriber claims" +
                    (notificationType == typeof(Stripe.Events.UnknownEventNotification)
                        ? ", and branch on Details.IsKnownEventType to act only on event types this " +
                          "Stripe.net version does not recognise."
                        : "."));
            }

            // Fail at startup rather than on the first matching webhook delivery.
            var eventName = StripeNotificationBinder.EventNameFor(notificationType);
            if (typeof(StripeEventNotificationHandlerBase).GetEvent(eventName) is null)
            {
                throw new InvalidOperationException(
                    $"The installed Stripe.net version does not expose an event named '{eventName}' for " +
                    $"notification type '{notificationType.FullName}'. Upgrade Stripe.net or remove the " +
                    $"subscriber '{typeof(TSubscriber).FullName}'.");
            }

            registry.Add(notificationType, typeof(TSubscriber));
        }

        return services;
    }

    private static StripeEventNotificationSubscriberRegistry GetOrAddRegistry(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(StripeEventNotificationSubscriberRegistry)
                && descriptor.ImplementationInstance is StripeEventNotificationSubscriberRegistry existing)
            {
                return existing;
            }
        }

        var registry = new StripeEventNotificationSubscriberRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
