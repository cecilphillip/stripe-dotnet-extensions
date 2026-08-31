using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Binds a notification type to the corresponding strongly-typed event on
/// <see cref="StripeEventNotificationHandlerBase"/> and invokes subscribers for it.
/// </summary>
internal abstract class StripeNotificationBinder
{
    private static readonly ConcurrentDictionary<Type, StripeNotificationBinder> Cache = new();

    private const string NotificationSuffix = "EventNotification";

    /// <summary>Gets (or creates and caches) the binder for a notification type.</summary>
    // The constructor that must survive belongs to the closed StripeNotificationBinder<T>, not to
    // the notification type, so a DynamicallyAccessedMembers annotation on the parameter would
    // describe nothing. Only RequiresDynamicCode expresses the real constraint.
    [RequiresDynamicCode(
        "Constructs StripeNotificationBinder<TNotification> at runtime via MakeGenericType.")]
    public static StripeNotificationBinder For(Type notificationType)
        => Cache.GetOrAdd(notificationType, static t =>
        {
            var binderType = typeof(StripeNotificationBinder<>).MakeGenericType(t);
            return (StripeNotificationBinder)Activator.CreateInstance(binderType)!;
        });

    /// <summary>
    /// Resolves the SDK event name for a notification type, e.g.
    /// <c>V2CoreAccountCreatedEventNotification</c> to <c>V2CoreAccountCreated</c>.
    /// </summary>
    public static string EventNameFor(Type notificationType)
    {
        var name = notificationType.Name;
        return name.EndsWith(NotificationSuffix, StringComparison.Ordinal)
            ? name[..^NotificationSuffix.Length]
            : name;
    }

    /// <summary>The notification type this binder handles.</summary>
    public abstract Type NotificationType { get; }

    /// <summary>
    /// Subscribes a single adapter to the SDK event for this notification type.
    /// </summary>
    /// <remarks>
    /// Exactly one adapter is attached per event per handler instance. The SDK throws if a second
    /// callback is registered for the same event type, so fan-out across multiple subscribers is
    /// performed inside the adapter rather than by registering more than once.
    /// </remarks>
    public abstract void Attach(
        StripeEventNotificationHandlerBase handler,
        Action<EventNotification, StripeClient> callback);

    /// <summary>Invokes a subscriber instance for this notification type.</summary>
    public abstract ValueTask InvokeAsync(
        object subscriber,
        EventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken);

    /// <summary>Gets the closed subscriber interface type for this notification type.</summary>
    public abstract Type SubscriberInterfaceType { get; }
}

/// <inheritdoc />
internal sealed class StripeNotificationBinder<TNotification> : StripeNotificationBinder
    where TNotification : EventNotification
{
    public override Type NotificationType => typeof(TNotification);

    public override Type SubscriberInterfaceType => typeof(IStripeEventSubscriber<TNotification>);

    public override void Attach(
        StripeEventNotificationHandlerBase handler,
        Action<EventNotification, StripeClient> callback)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(callback);

        var eventName = EventNameFor(typeof(TNotification));
        var eventInfo = typeof(StripeEventNotificationHandlerBase).GetEvent(eventName)
            ?? throw new InvalidOperationException(
                $"The Stripe SDK does not expose an event named '{eventName}' for notification type " +
                $"'{typeof(TNotification).FullName}'. This usually means the installed Stripe.net version " +
                "does not support this event yet.");

        EventHandler<StripeEventNotificationEventArgs<TNotification>> adapter =
            (_, args) => callback(args.EventNotification, args.Client);

        eventInfo.AddEventHandler(handler, adapter);
    }

    public override ValueTask InvokeAsync(
        object subscriber,
        EventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
        => ((IStripeEventSubscriber<TNotification>)subscriber)
            .HandleAsync((TNotification)notification, context, cancellationToken);
}
