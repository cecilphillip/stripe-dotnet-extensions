namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Sentinel annotation to ensure the webhook signing secret resolver (log watcher + health check)
/// is registered at most once per Stripe CLI resource.
/// </summary>
internal sealed class StripeSecretResolverAnnotation : IResourceAnnotation
{
}
