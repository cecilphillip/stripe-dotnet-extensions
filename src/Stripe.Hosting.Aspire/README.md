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
// optional named Stripe config section mapping (Stripe__Secondary__*)
api.WithReference(stripe, clientName: "Secondary");
```

`WithReference` also injects `Stripe__{clientName}__WebhookSecret` for `AddStripe()` configuration binding.

## Lifecycle notes (secret extraction + WaitFor)

- `WebhookSigningSecret` starts as `null`.
- `WithReference(...)` always injects `STRIPE_WEBHOOK_SECRET`, but its value is an empty string until Stripe CLI startup logs include a `whsec_...` secret.
- `WithWebhookForwardTo(...)` registers a health check that becomes healthy only after secret extraction.

To avoid startup races, wire the dependency and wait explicitly:

```csharp
api.WithReference(stripe)
   .WaitFor(stripe);
```

Without `WaitFor(stripe)`, dependent services can start before the webhook secret is available.

## Troubleshooting / diagnostics

### `stripe` command not found (local mode)

- Install the [Stripe CLI](https://docs.stripe.com/stripe-cli).
- Ensure `stripe` is on your `PATH`, or pass `stripePath` to `AddStripeCli(...)`.

### `STRIPE_WEBHOOK_SECRET` is empty at startup

- Confirm you configured forwarding with `WithWebhookForwardTo(...)` (this enables secret extraction).
- Add `.WaitFor(stripe)` on services that consume `WithReference(stripe)`.
- Check AppHost logs for Stripe CLI startup output and the emitted `whsec_...` line.

### Invalid forwarding target errors

`WithWebhookForwardTo` throws when the target has no endpoints or an endpoint has not been allocated yet. Ensure forwarded resources expose an endpoint (for example, `WithHttpEndpoint(...)`).

### Docker container cannot reach host endpoint

When using `AddStripeCliContainer(...)`, forwarding to host-bound services rewrites `localhost` to `host.docker.internal`.
- On macOS/Windows (Docker Desktop), this is available by default.
- On Linux, the integration adds `--add-host=host.docker.internal:host-gateway` automatically.

## How it works

- **Local mode** (`AddStripeCli`): Runs `stripe listen --forward-to <url>` as a local process.
- **Container mode** (`AddStripeCliContainer`): Starts `docker.io/stripe/stripe-cli:v1.33.0` with the same arguments.
- After startup, the Stripe CLI prints its **webhook signing secret** to stdout. The integration watches the process output and extracts the `whsec_...` value, making it available via `WithReference`.

## Additional Information

- [Stripe CLI documentation](https://docs.stripe.com/stripe-cli)
- [Testing webhooks locally](https://docs.stripe.com/webhooks/test)
