namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a Stripe CLI Docker container resource for webhook forwarding and testing.
/// Uses the official <c>stripe/stripe-cli</c> Docker image.
/// </summary>
/// <param name="name">The name of the resource.</param>
[AspireExport(ExposeProperties = true)]
public sealed class StripeCliContainerResource(string name)
    : ContainerResource(name), IStripeCliResource
{
    private volatile string? _webhookSigningSecret;

    /// <inheritdoc/>
    public string? WebhookSigningSecret
    {
        get => _webhookSigningSecret;
        internal set => _webhookSigningSecret = value;
    }

    /// <inheritdoc/>
    public ParameterResource? SecretKey { get; internal set; }

    /// <inheritdoc/>
    public ParameterResource? PublishableKey { get; internal set; }
}
