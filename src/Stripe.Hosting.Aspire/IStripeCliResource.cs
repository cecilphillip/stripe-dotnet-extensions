namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a Stripe CLI resource for local webhook forwarding and testing.
/// Implemented by both <see cref="StripeCliResource"/> (local executable) and
/// <see cref="StripeCliContainerResource"/> (Docker container).
/// </summary>
public interface IStripeCliResource : IResourceWithArgs
{
    /// <summary>
    /// Gets the webhook signing secret extracted from the Stripe CLI output after startup.
    /// This value is populated asynchronously once the CLI outputs its ready message.
    /// </summary>
    string? WebhookSigningSecret { get; }
}
