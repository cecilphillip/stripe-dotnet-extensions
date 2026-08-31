using Stripe.Events;
using Stripe.Extensions.AspNetCore;

namespace SampleEventNotifications.Subscribers;

/// <summary>
/// Provisions local state when a new v2 account is created.
/// </summary>
/// <remarks>
/// Notice what is <em>not</em> here: no signature checking, no JSON parsing, no switch on the event
/// type, no base class. The subscriber declares the one notification it cares about and receives it
/// strongly typed. Ordinary constructor injection works because subscribers are resolved from the
/// request scope.
/// </remarks>
public sealed class AccountProvisioningSubscriber(
    ILogger<AccountProvisioningSubscriber> logger)
    : IStripeEventSubscriber<V2CoreAccountCreatedEventNotification>
{
    public async ValueTask HandleAsync(
        V2CoreAccountCreatedEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        // The thin payload carries only identifiers. Pull the full object on demand; the notification
        // is already bound to a client scoped to the correct account, so Connect works with no
        // extra plumbing.
        // FetchRelatedObjectAsync throws when the event carries no related object, so RelatedObject
        // is checked first. This also avoids a pointless API call.
        if (notification.RelatedObject is null)
        {
            logger.LogWarning(
                "Notification {NotificationId} carried no related account; nothing to provision",
                notification.Id);
            return;
        }

        var account = await notification.FetchRelatedObjectAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Provisioning account {AccountId} ({DisplayName}) from notification {NotificationId}",
            account.Id,
            account.DisplayName ?? "(no display name)",
            notification.Id);
    }
}
