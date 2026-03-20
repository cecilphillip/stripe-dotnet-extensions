# Stripe.Hosting.Aspire

Provides extension methods and resource definitions for a .NET Aspire AppHost to integrate the Stripe CLI for local webhook forwarding and testing. Supports **two hosting modes**: the locally installed Stripe CLI executable, or the official Stripe CLI Docker image.

## Getting Started

### Prerequisites

**Local CLI mode**: The [Stripe CLI](https://docs.stripe.com/stripe-cli) must be installed and available in your system PATH.

**Docker container mode**: Docker must be running. No local Stripe CLI installation required.

### Install the package

In your AppHost project, install the package:

```dotnetcli
dotnet add package Stripe.Hosting.Aspire
```

## Usage

### Local CLI mode

Use `AddStripeCli` to start the locally installed `stripe` CLI alongside your Aspire services:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);

var api = builder.AddProject<Projects.MyApi>("api");

var stripe = builder.AddStripeCli("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe",
                          events: ["payment_intent.created", "charge.succeeded"]);

api.WithReference(stripe); // injects STRIPE_WEBHOOK_SECRET env var

builder.Build().Run();
```

### Docker container mode

Use `AddStripeCliContainer` to run the Stripe CLI in a Docker container — no local installation required:

```csharp
var stripe = builder.AddStripeCliContainer("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

api.WithReference(stripe);
```

## Forwarding to multiple endpoints

Both `--forward-to` and `--forward-connect-to` support multiple targets. Each resource generates its own CLI flag:

```csharp
var stripe = builder.AddStripeCli("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo("/webhooks/stripe", api, paymentsService, notificationsService);
```

## Stripe Connect support

Use `WithWebhookConnectForwardTo` to forward Stripe Connect events (`--forward-connect-to`) to a separate endpoint:

```csharp
var stripe = builder.AddStripeCli("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe")
    .WithWebhookConnectForwardTo(api, webhookPath: "/webhooks/stripe-connect");
```

## Skip SSL verification

For local HTTPS endpoints with self-signed certificates, use `skipVerify: true`:

```csharp
var stripe = builder.AddStripeCli("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe", skipVerify: true);
```

## Injecting the webhook signing secret

`WithReference` injects the Stripe CLI's webhook signing secret into a dependent service as the `STRIPE_WEBHOOK_SECRET` environment variable. The secret is extracted from the CLI output after startup.

```csharp
api.WithReference(stripe);
// or with a custom env var name:
api.WithReference(stripe, envVarName: "MY_STRIPE_SECRET");
```

## Passing the API key as a CLI argument

In addition to the `STRIPE_API_KEY` environment variable set by `AddStripeCli`, you can also pass the key as a `--api-key` CLI argument:

```csharp
var stripe = builder.AddStripeCli("stripe", apiKey: stripeApiKey)
    .WithApiKey(stripeApiKey)
    .WithWebhookForwardTo(api);
```

## How it works

- **Local mode** (`AddStripeCli`): Runs `stripe listen --forward-to <url>` as a local process.
- **Container mode** (`AddStripeCliContainer`): Starts `docker.io/stripe/stripe-cli:v1.33.0` with the same arguments.
- After startup, the Stripe CLI prints its **webhook signing secret** to stdout. The integration watches the process output and extracts the `whsec_...` value, making it available via `WithReference`.

## Additional Information

- [Stripe CLI documentation](https://docs.stripe.com/stripe-cli)
- [Testing webhooks locally](https://docs.stripe.com/webhooks/test)
