# SampleCheckout.AppHost

Aspire AppHost that runs the Stripe CLI alongside the sample web apps, so Stripe events reach
`localhost` during local development.

It wires up two apps:

| App | Endpoint | Events |
|---|---|---|
| `SampleCheckout` | `/stripe/webhook` | v1 snapshot events |
| `SampleEventNotifications` | `/stripe/thin-events` | v2 thin events |

## Prerequisites

- **Docker running** — the default configuration uses `AddStripeCliContainer`
- **Or the Stripe CLI** installed and on `PATH`, plus a one-time `stripe login`, if you switch to
  `AddStripeCli`
- Stripe **test-mode** API keys

## Configuration

Store your keys with user secrets, from this directory:

```bash
dotnet user-secrets set "Parameters:stripe-api-key"         "sk_test_..."
dotnet user-secrets set "Parameters:stripe-publishable-key" "pk_test_..."
```

## Running

```bash
dotnet run
```

The Stripe CLI starts in webhook-forwarding mode and prints a signing secret. `WaitFor(stripeCli)`
holds the web apps back until that secret has been captured, so it is never empty on first start.

## Local CLI instead of the container

Swap `AddStripeCliContainer` for `AddStripeCli` in `Program.cs`. Both take the same arguments; the
container variant needs no local CLI install, while the executable variant reuses your existing
`stripe login` session.

On Linux the container mode adds `--add-host=host.docker.internal:host-gateway` automatically; on
macOS and Windows that hostname already routes to the host.

## Injected environment variables

`WithReference(stripeCli)` injects the credentials into a referencing project:

| Variable | Purpose |
|---|---|
| `STRIPE_SECRET_KEY` | Secret API key, for custom use |
| `STRIPE_PUBLISHABLE_KEY` | Publishable key (present because `publishableKey` was supplied) |
| `STRIPE_WEBHOOK_SECRET` | Signing secret captured from CLI output at startup |
| `Stripe__Default__ApiKey` | Binds to `Stripe:Default:ApiKey` |
| `Stripe__Default__PublicKey` | Binds to `Stripe:Default:PublicKey` |
| `Stripe__Default__WebhookSecret` | Binds to `Stripe:Default:WebhookSecret` |

The `Stripe__Default__*` set is what `AddStripe()` binds, so the sample apps need no further
configuration. Pass `clientName: "Secondary"` to target a named client registered with
`AddStripe("Secondary")`.

## Snapshot vs. thin events

They are separate delivery channels and need separate endpoints, which is why the AppHost calls both
`WithWebhookForwardTo` and `WithThinEventForwardTo`. The CLI subscribes to no thin events unless
asked, so `WithThinEventForwardTo` also emits `--thin-events *`.

A single `stripe listen` session covers both channels with one signing secret, so both apps are
wired the same way.
