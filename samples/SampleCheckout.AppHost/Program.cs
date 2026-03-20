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
//   - A Stripe API key (test mode) configured in user secrets or environment

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Parameters
// ---------------------------------------------------------------------------

// Store your Stripe test API key with user secrets:
//   dotnet user-secrets set "stripe-api-key" "sk_test_..."
//   (from the SampleCheckout.AppHost directory)
var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);

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
//
// The CLI will start with `stripe listen --forward-to <checkout-url>/stripe/webhook`
// and print a signing secret (whsec_...) that is automatically captured and
// injected into the checkout service as STRIPE_WEBHOOK_SECRET.

// var stripeCli = builder.AddStripeCli("stripe-cli", apiKey: stripeApiKey)
//     .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook");

// ---------------------------------------------------------------------------
// Stripe CLI — Docker container mode (alternative)
// ---------------------------------------------------------------------------
// Swap the block above for this one to use the official stripe/stripe-cli image
// instead of a locally installed CLI. No local installation required.
//
var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey)
    .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook");

// ---------------------------------------------------------------------------
// Inject the webhook signing secret into the checkout service
// ---------------------------------------------------------------------------
// WithReference reads the signing secret that the CLI printed at startup and
// makes it available to the checkout app as the STRIPE_WEBHOOK_SECRET env var.
// The app's Stripe middleware uses this to verify incoming webhook signatures.
//
// WaitFor ensures checkout starts only after the signing secret is captured,
// preventing STRIPE_WEBHOOK_SECRET from being empty on first start.

checkout.WithReference(stripeCli)
        .WaitFor(stripeCli);

builder.Build().Run();
