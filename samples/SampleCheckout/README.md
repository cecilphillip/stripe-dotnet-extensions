# SampleCheckout

Demonstrates **v1 snapshot webhook handling** with `StripeWebhookHandler<T>`, plus **named clients**
for using more than one set of Stripe credentials in the same app.

## What it shows

| Concept | Where |
|---|---|
| Handling v1 events by overriding `On*Async` | `WebhookHandlers/MyWebhookHandler.cs` |
| Mapping the webhook endpoint | `Program.cs` — `MapStripeWebhookHandler<MyWebhookHandler>()` |
| A second, named client | `Program.cs` — `AddStripe("ProductsReadOnly", ...)` |
| Resolving a named client | `Controllers/HomeController.cs` via `[FromKeyedServices]` |

The webhook endpoint is `/stripe/webhook`, the default for `MapStripeWebhookHandler<T>`.

## Running under the AppHost (recommended)

Run [`SampleCheckout.AppHost`](../SampleCheckout.AppHost) instead of this project directly. It starts
the Stripe CLI, forwards events to this app, and injects the API key and signing secret
automatically, so no local configuration is needed.

## Running standalone

Provide the credentials yourself. The signing secret comes from `stripe listen`:

```bash
stripe listen --forward-to localhost:5000/stripe/webhook
```

Then, in another terminal:

```bash
export Stripe__Default__ApiKey="sk_test_..."
export Stripe__Default__WebhookSecret="whsec_..."   # printed by `stripe listen`
dotnet run
```

The double underscore maps to configuration nesting, so `Stripe__Default__ApiKey` binds to
`Stripe:Default:ApiKey`. The same values can go in user secrets or `appsettings.Development.json`
instead — do not commit real keys.

Trigger the event this sample handles to see it run:

```bash
stripe trigger customer.created
```
