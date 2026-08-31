using Stripe.Events;
using Stripe.Extensions.AspNetCore;

namespace SampleEventNotifications.Subscribers;

/// <summary>
/// Handles two different notification types in one class.
/// </summary>
/// <remarks>
/// A subscriber may implement <see cref="IStripeEventSubscriber{TNotification}"/> more
/// than once when the handling logic is genuinely shared. The type registers for both notifications
/// with a single <c>AddStripeEventSubscriber</c> call.
/// </remarks>
public sealed class ComplianceSubscriber(ILogger<ComplianceSubscriber> logger)
    : IStripeEventSubscriber<V2CoreAccountIncludingRequirementsUpdatedEventNotification>,
        IStripeEventSubscriber<V2CoreAccountPersonCreatedEventNotification>
{
    public async ValueTask HandleAsync(
        V2CoreAccountIncludingRequirementsUpdatedEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        // FetchRelatedObjectAsync throws when the event carries no related object, so RelatedObject
        // is checked first. This also avoids a pointless API call.
        if (notification.RelatedObject is null)
        {
            logger.LogWarning(
                "Requirements event {EventId} carried no related account; nothing to re-check",
                notification.Id);
            return;
        }

        var account = await notification.FetchRelatedObjectAsync().ConfigureAwait(false);

        logger.LogInformation(
            "Requirements changed for account {AccountId}; re-running onboarding checks",
            account.Id);
    }

    public ValueTask HandleAsync(
        V2CoreAccountPersonCreatedEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "New person {PersonId} added; queueing identity verification",
            notification.RelatedObject?.Id ?? "(none)");

        return ValueTask.CompletedTask;
    }
}
