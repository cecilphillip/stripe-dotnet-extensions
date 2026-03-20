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

    /// <summary>
    /// Gets the Stripe secret (API) key parameter, if one was provided.
    /// Injected into dependent services as <c>STRIPE_SECRET_KEY</c> by <c>WithReference</c>.
    /// </summary>
    ParameterResource? SecretKey { get; }

    /// <summary>
    /// Gets the Stripe publishable key parameter, if one was provided via <c>WithPublishableKey</c>.
    /// Injected into dependent services as <c>STRIPE_PUBLISHABLE_KEY</c> by <c>WithReference</c>.
    /// </summary>
    ParameterResource? PublishableKey { get; }
}
