namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a locally installed Stripe CLI resource for webhook forwarding and testing.
/// The <c>stripe</c> executable must be available in the system PATH or specified via a custom path.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="command">The path to the Stripe CLI executable.</param>
/// <param name="workingDirectory">The working directory for the executable.</param>
[AspireExport(ExposeProperties = true)]
public sealed class StripeCliResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory), IStripeCliResource
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
