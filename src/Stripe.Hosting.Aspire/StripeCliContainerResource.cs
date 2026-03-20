namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a Stripe CLI Docker container resource for webhook forwarding and testing.
/// Uses the official <c>stripe/stripe-cli</c> Docker image.
/// </summary>
/// <param name="name">The name of the resource.</param>
public sealed class StripeCliContainerResource(string name)
    : ContainerResource(name), IStripeCliResource
{
    /// <inheritdoc/>
    public string? WebhookSigningSecret { get; internal set; }
}
