// Stripe CLI Aspire Integration — Sample AppHost
//
// This AppHost demonstrates using Stripe.Hosting.Aspire to run the Stripe CLI
// alongside an ASP.NET Core web application during local development.
//
// The Stripe CLI starts in webhook-forwarding mode, proxying Stripe events to
// the local application and automatically setting the webhook signing secret.
//
// Prerequisites:
//   - Stripe CLI installed and in PATH  (https://stripe.com/docs/stripe-cli)
//   - OR: Docker running (for the container mode variant below)
//   - Stripe API keys configured in user secrets or environment

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

// Store your Stripe test keys with user secrets (from the AppHost directory):
//   dotnet user-secrets set "Parameters:stripe-api-key"        "sk_test_..."
//   dotnet user-secrets set "Parameters:stripe-publishable-key" "pk_test_..."
var stripeApiKey        = builder.AddParameter("stripe-api-key",        secret: true);
var stripePublishableKey = builder.AddParameter("stripe-publishable-key", secret: false);

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------

// The SampleCheckout web app — exposes the Stripe webhook endpoint at
// /stripe/webhook (the default path for MapStripeWebhookHandler<T>).
var checkout = builder.AddProject<Projects.SampleCheckout>("checkout");

// ---------------------------------------------------------------------------
// Stripe CLI — local executable mode
// ---------------------------------------------------------------------------
// Requires the `stripe` CLI to be installed and available in PATH.
// Run `stripe login` once before starting the AppHost.

// var stripeCli = builder.AddStripeCli("stripe-cli", apiKey: stripeApiKey, publishableKey: stripePublishableKey)
//     .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook");

// ---------------------------------------------------------------------------
// Stripe CLI — Docker container mode (alternative)
// ---------------------------------------------------------------------------
// Swap the block above for this one to use the official stripe/stripe-cli image.
// On macOS/Windows, host.docker.internal routes to the host automatically.
// On Linux, --add-host=host.docker.internal:host-gateway is added automatically.

var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey, publishableKey: stripePublishableKey)
    .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook");

// ---------------------------------------------------------------------------
// Inject Stripe credentials into the checkout service
// ---------------------------------------------------------------------------
// WithReference injects all available Stripe credentials as environment variables.
//
// Standalone vars (for custom usage):
//   STRIPE_SECRET_KEY      — the secret API key
//   STRIPE_PUBLISHABLE_KEY — the publishable key (because WithPublishableKey was called)
//   STRIPE_WEBHOOK_SECRET  — the signing secret captured from CLI output at startup
//
// Stripe.Extensions.DependencyInjection config-binding vars (default clientName = "Default"):
//   Stripe__Default__ApiKey        — maps to Stripe:Default:ApiKey
//   Stripe__Default__PublicKey     — maps to Stripe:Default:PublicKey
//   Stripe__Default__WebhookSecret — maps to Stripe:Default:WebhookSecret
//
// This means services.AddStripe() in the checkout app requires zero additional configuration.
// Use clientName: "Secondary" to target a named client (services.AddStripe("Secondary")).
//
// WaitFor ensures checkout starts only after the signing secret is captured,
// preventing STRIPE_WEBHOOK_SECRET from being empty on first start.

checkout.WithReference(stripeCli)
        .WaitFor(stripeCli);

builder.Build().Run();
