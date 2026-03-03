namespace Stripe.Extensions.DependencyInjection;

public class StripeOptions: StripeClientOptions
{
    public const string DefaultClientConfigurationSectionName = "Default";
    public string ClientName { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string SecretKey => ApiKey;
    public string WebhookSecret { get; set; } = string.Empty;
    public int WebhookTimestampTolerance { get; set; } = 300; // seconds (5 minutes)
    public bool ThrowOnWebhookApiVersionMismatch { get; set; } = true;
    public bool EnableTelemetry { get; set; } = true;
    public AppInfo? AppInfo { get; set; }
    public string ApiVersion => StripeConfiguration.ApiVersion;
    public int MaxNetworkRetries { get; set; } = SystemNetHttpClient.DefaultMaxNumberRetries;
}
