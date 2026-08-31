namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// A notification type and every subscriber registered for it.
/// </summary>
internal sealed record SubscriberRegistration(Type NotificationType, IReadOnlyList<Type> SubscriberTypes);

/// <summary>
/// Records which subscriber types handle which notification types. Populated at startup by
/// <c>AddStripeEventSubscriber</c> and read once when an endpoint is mapped.
/// </summary>
internal sealed class StripeEventNotificationSubscriberRegistry
{
    private readonly Dictionary<Type, List<Type>> _subscribersByNotification = [];
    private readonly List<Type> _unhandledSubscribers = [];

    /// <summary>Registers a subscriber implementation for a notification type.</summary>
    public void Add(Type notificationType, Type subscriberType)
    {
        if (!_subscribersByNotification.TryGetValue(notificationType, out var list))
        {
            list = [];
            _subscribersByNotification[notificationType] = list;
        }

        if (!list.Contains(subscriberType))
        {
            list.Add(subscriberType);
        }
    }

    /// <summary>Registers a subscriber for notifications no typed subscriber claims.</summary>
    public void AddUnhandled(Type subscriberType)
    {
        if (!_unhandledSubscribers.Contains(subscriberType))
        {
            _unhandledSubscribers.Add(subscriberType);
        }
    }

    /// <summary>Gets every registered (notification type, subscriber types) pair.</summary>
    public IReadOnlyList<SubscriberRegistration> Entries
        => _subscribersByNotification
            .Select(kvp => new SubscriberRegistration(kvp.Key, kvp.Value))
            .ToList();

    /// <summary>Gets the registered unhandled-notification subscriber types.</summary>
    public IReadOnlyList<Type> UnhandledSubscribers => _unhandledSubscribers;

    /// <summary>Gets a value indicating whether any subscriber has been registered.</summary>
    public bool IsEmpty => _subscribersByNotification.Count == 0 && _unhandledSubscribers.Count == 0;
}
