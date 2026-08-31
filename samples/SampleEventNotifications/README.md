# SampleEventNotifications

Demonstrates **DI-registered event notification subscribers** — the recommended thin-event (v2)
programming model, layered on top of Stripe.net's `StripeEventNotificationHandler`.

Register **many small classes**, each owning a single event, resolved from DI per request.

## What it shows

| Concept | Where |
|---|---|
| One subscriber per event | `Subscribers/EventDestinationPingSubscriber.cs` |
| **Fan-out** — two subscribers on the same event | `AccountProvisioningSubscriber` + `AccountAnalyticsSubscriber`, both on `v2.core.account.created` |
| One class handling **multiple** events | `Subscribers/ComplianceSubscriber.cs` |
| Handling events **nobody subscribed to** | `Subscribers/UnhandledEventAuditSubscriber.cs` |
| Fetching the related object | `AccountProvisioningSubscriber` calls `notification.FetchRelatedObjectAsync()` |
| `ShouldDispatchAsync` idempotency gate | `Program.cs` |
| Observing the outcome with an **endpoint filter** | `Program.cs` |

## Handling events nobody subscribed to

`IStripeUnhandledEventSubscriber` receives whatever the typed subscribers didn't claim — the
complement, not every event. That set shrinks as you add typed subscribers.

Subscribing to the base `EventNotification` type to get everything is rejected at startup; the two
are separate interfaces so the distinction is compiler-enforced:

```csharp
public sealed class UnhandledEventAuditSubscriber : IStripeUnhandledEventSubscriber
{
    public ValueTask HandleAsync(
        StripeUnhandledEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        // context.Details.IsKnownEventType is false only when this Stripe.net version
        // cannot type the event at all — a precise "the SDK is behind the API" signal.
    }
}
```

Registered the same way as any other subscriber, and it fans out like one:

```csharp
builder.Services.AddStripeEventSubscriber<UnhandledEventAuditSubscriber>();
```

Anything a typed subscriber handled never reaches it. Register none and the library logs a warning
instead, so events are never silently dropped. A single class may implement both
`IStripeEventSubscriber<T>` and `IStripeUnhandledEventSubscriber`.

## Registration

```csharp
builder.Services.AddStripe();

builder.Services.AddStripeEventSubscriber<AccountProvisioningSubscriber>();
builder.Services.AddStripeEventSubscriber<AccountAnalyticsSubscriber>();
builder.Services.AddStripeEventSubscriber<ComplianceSubscriber>();

app.MapStripeEventNotifications("/stripe/thin-events", options => { /* ShouldDispatchAsync */ });
```

A subscriber is just:

```csharp
public sealed class EventDestinationPingSubscriber(ILogger<EventDestinationPingSubscriber> logger)
    : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
{
    public ValueTask HandleAsync(
        V2CoreEventDestinationPingEventNotification notification,
        StripeEventNotificationContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Ping received. Endpoint is reachable.");
        return ValueTask.CompletedTask;
    }
}
```

That particular subscriber is optional — the endpoint answers 202 for events nobody claims, so a
ping succeeds without it. It is shown because it is the smallest subscriber that does something
observable. Stripe sends the ping only when you request one, from the Dashboard or the
`/v2/core/event_destinations/:id/ping` API.

Registration is **fail-fast**: registering a type that implements no
`IStripeEventSubscriber<T>`, or mapping the endpoint with no subscribers registered,
throws at startup rather than silently no-op'ing at request time.

## Response codes

| Situation | Status |
|---|---|
| All subscribers completed | `202 Accepted` |
| Missing/invalid signature, malformed JSON, v1 event posted to the v2 endpoint | `400 Bad Request` |
| A subscriber threw | `500 Internal Server Error` |

`400` is terminal for Stripe; `500` triggers retry. Bad input is never reported as `500`, so Stripe
won't retry a payload that can never succeed. Every subscriber runs even if an earlier one throws,
and **all** failures are preserved in the aggregate.

## Running it

Via the Aspire AppHost (recommended) — the Stripe CLI forwards thin events automatically:

```bash
cd samples/SampleCheckout.AppHost
dotnet run
```

The AppHost wires this with:

```csharp
.WithThinEventForwardTo(notifications, thinEventPath: "/stripe/thin-events")
```

> `stripe listen` defaults `--thin-events` to `none`, so `--forward-thin-to` alone forwards nothing.
> `WithThinEventForwardTo` always emits `--thin-events` (defaulting to `*`) to avoid that trap.

Standalone:

```bash
export Stripe__Default__ApiKey=sk_test_...
export Stripe__Default__WebhookSecret=whsec_...
dotnet run --project samples/SampleEventNotifications
```

then in another terminal:

```bash
stripe listen --thin-events '*' --forward-thin-to http://localhost:5182/stripe/thin-events
```

Snapshot (v1) and thin (v2) events must be forwarded to **separate** endpoints; a single
`stripe listen` session covers both with the same signing secret.

## Before and after dispatch

`ShouldDispatchAsync` is the one hook the endpoint has to own, because it is the only place that
sees the parsed notification before any subscriber runs. Return `false` and nothing is dispatched,
but the endpoint still answers 202 so Stripe stops retrying.

```csharp
options.ShouldDispatchAsync = async (context, cancellationToken) =>
    await store.TryMarkSeenAsync(context.Notification.Id, cancellationToken);
```

There is no matching "post-handle" callback, and that is deliberate. The endpoint returns a
`RouteHandlerBuilder`, so ASP.NET Core's own filters already wrap it — they compose, resolve
services, and can rewrite the response, none of which a single callback property would offer. The
only thing a filter cannot work out for itself is what the endpoint decided, so the endpoint
publishes that as a feature:

```csharp
app.MapStripeEventNotifications("/stripe/thin-events")
   .AddEndpointFilter(async (context, next) =>
   {
       var response = await next(context);
       var result = context.HttpContext.Features.Get<StripeEventNotificationResult>();
       // result.EventType, result.EventId, result.Outcome, result.Exception
       return response;
   });
```

`Outcome` is `Rejected` (400), `Skipped` (202, the gate declined), `Dispatched` (202) or `Failed`
(500). The feature is set before the body is read, so it is present even when the request is
rejected — `EventType` is simply null because nothing parsed.
