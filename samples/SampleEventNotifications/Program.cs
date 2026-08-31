using System.Collections.Concurrent;
using System.Diagnostics;
using SampleEventNotifications.Subscribers;
using Stripe.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Binds Stripe:Default:ApiKey and Stripe:Default:WebhookSecret. When run under the Aspire AppHost,
// both are injected automatically by WithReference(stripeCli) — no local configuration needed.
builder.Services.AddStripe();

// Each subscriber is registered independently. Two of them target the same notification type
// (V2CoreAccountCreated); the library fans out to both.
builder.Services.AddStripeEventSubscriber<AccountProvisioningSubscriber>();
builder.Services.AddStripeEventSubscriber<AccountAnalyticsSubscriber>();

// ComplianceSubscriber implements the interface twice, so one call registers it for both
// notification types it handles.
builder.Services.AddStripeEventSubscriber<ComplianceSubscriber>();
builder.Services.AddStripeEventSubscriber<EventDestinationPingSubscriber>();

// Handles anything the subscribers above did not claim. Not a catch-all: notifications they handled
// never reach it. Without one of these, the library just logs a warning.
builder.Services.AddStripeEventSubscriber<UnhandledEventAuditSubscriber>();

var app = builder.Build();

// Stripe retries deliveries, so the same notification id can arrive more than once. A real service
// would use a durable store; an in-memory set is enough to show where the check belongs.
var processed = new ConcurrentDictionary<string, byte>();

app.MapStripeEventNotifications("/stripe/thin-events", options =>
{
    // Runs after parsing and signature verification but before any subscriber. Returning false
    // skips dispatch; the endpoint still answers 202 so Stripe stops retrying. It is asynchronous
    // because a real duplicate check is a database or cache lookup.
    options.ShouldDispatchAsync = (context, _) =>
    {
        var firstDelivery = processed.TryAdd(context.Notification.Id, 0);
        if (!firstDelivery)
        {
            app.Logger.LogInformation(
                "Skipping duplicate delivery of {NotificationId}", context.Notification.Id);
        }

        return ValueTask.FromResult(firstDelivery);
    };    
})
// There is no "post-handle" callback, because the endpoint is a normal ASP.NET Core endpoint and
// filters already do this job: they compose, resolve services, and can rewrite the response. The
// endpoint publishes its outcome as a feature so a filter has something to report.
.AddEndpointFilter(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    var response = await next(context);
    var result = context.HttpContext.Features.Get<StripeEventNotificationResult>();

    app.Logger.LogInformation(
        "{EventType} -> {Outcome} in {ElapsedMs}ms",
        result?.EventType ?? "unparsed", result?.Outcome, stopwatch.ElapsedMilliseconds);

    return response;
});

app.MapGet("/", () => Results.Ok(new
{
    service = "SampleEventNotifications",
    endpoint = "/stripe/thin-events",
    subscribers = new[]
    {
        "AccountProvisioningSubscriber  -> v2.core.account.created",
        "AccountAnalyticsSubscriber     -> v2.core.account.created (fan-out)",
        "ComplianceSubscriber           -> v2.core.account[requirements].updated, v2.core.account_person.created",
        "EventDestinationPingSubscriber -> v2.core.event_destination.ping",
        "UnhandledEventAuditSubscriber  -> anything the above did not claim"
    }
}));

app.Run();
