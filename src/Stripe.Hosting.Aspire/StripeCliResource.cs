namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a locally installed Stripe CLI resource for webhook forwarding and testing.
/// The <c>stripe</c> executable must be available in the system PATH or specified via a custom path.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="command">The path to the Stripe CLI executable.</param>
/// <param name="workingDirectory">The working directory for the executable.</param>
public sealed class StripeCliResource(string name, string command, string workingDirectory)
    : ExecutableResource(name, command, workingDirectory), IStripeCliResource
{
    /// <inheritdoc/>
    public string? WebhookSigningSecret { get; internal set; }
}
