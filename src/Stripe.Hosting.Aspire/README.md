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

api.WithStripeCli(stripe);

builder.Build().Run();
```

### Docker container mode

Use `AddStripeCliContainer` to run the Stripe CLI in a Docker container — no local installation required:

```csharp
var stripe = builder.AddStripeCliContainer("stripe", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

api.WithStripeCli(stripe);
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

`WithStripeCli` injects the Stripe CLI's credentials and signing secret into a
dependent service and waits for secret capture. The secret is extracted from CLI output after
startup. Pass a client name to target a named Stripe configuration section:

```csharp
api.WithStripeCli(stripe, clientName: "Secondary");
```

This also injects `Stripe__{clientName}__WebhookSecret` for `AddStripe()` configuration binding.
Use `WithReference(stripe)` alone when a startup wait is not wanted; it remains available.

## Lifecycle notes (secret extraction + WaitFor)

- `WebhookSigningSecret` starts as `null`.
- `WithReference(...)` always injects `STRIPE_WEBHOOK_SECRET`, but its value is an empty string until Stripe CLI startup logs include a `whsec_...` secret.
- Forwarding helpers and `WithStripeCli(...)` register a health check that becomes healthy only after secret extraction.

To avoid startup races, use the convenience helper:

```csharp
api.WithStripeCli(stripe);
```

This is equivalent to `api.WithReference(stripe).WaitFor(stripe)` when forwarding is configured.
Without the wait, dependent services can start before the webhook secret is available. Configure
forwarding with `WithWebhookForwardTo`, `WithWebhookConnectForwardTo`, or `WithThinEventForwardTo`
to deliver events. The wait is removed when publishing because the CLI is local-only.

## Troubleshooting / diagnostics

### `stripe` command not found (local mode)

- Install the [Stripe CLI](https://docs.stripe.com/stripe-cli).
- Ensure `stripe` is on your `PATH`, or pass `stripePath` to `AddStripeCli(...)`.

### `STRIPE_WEBHOOK_SECRET` is empty at startup

- Confirm you configured forwarding on the Stripe CLI.
- Use `WithStripeCli(stripe)`, or add `.WaitFor(stripe)` after `WithReference(stripe)`.
- Check AppHost logs for Stripe CLI startup output and the emitted `whsec_...` line.

### Invalid forwarding target errors

Snapshot, Connect, and thin-event forwarding throw if the target has no endpoints or its selected
endpoint has not been allocated. For example:

```text
Stripe CLI resource 'stripe' cannot forward to 'api': the target has no endpoints. Add an HTTP or HTTPS endpoint to the target, for example with WithHttpEndpoint(...).
Stripe CLI resource 'stripe' cannot forward to 'api': endpoint 'http' has not been allocated. Aspire allocates endpoints during AppHost startup; check the AppHost logs for endpoint allocation errors.
```

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
