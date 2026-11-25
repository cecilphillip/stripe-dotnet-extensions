// Runs the Stripe CLI alongside the sample apps so events reach localhost.
// Setup and configuration: see README.md.

var builder = DistributedApplication.CreateBuilder(args);

var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);
var stripePublishableKey = builder.AddParameter("stripe-publishable-key", secret: false);

var checkout = builder.AddDotnetProject("checkout", "../SampleCheckout/SampleCheckout.csproj");
var notifications = builder.AddDotnetProject("notifications", "../SampleEventNotifications/SampleEventNotifications.csproj");

// Local CLI alternative to the container below; requires `stripe` on PATH.
// var stripeCli = builder.AddStripeCli("stripe-cli", apiKey: stripeApiKey, publishableKey: stripePublishableKey)
//     .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook")
//     .WithThinEventForwardTo(notifications, thinEventPath: "/stripe/thin-events");

var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey, publishableKey: stripePublishableKey)
    // Snapshot (v1) and thin (v2) events are separate channels and need separate endpoints.
    .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook")
    .WithThinEventForwardTo(notifications, thinEventPath: "/stripe/thin-events");

// Wait until the signing secret has been captured before starting either app.
checkout.WithStripeCli(stripeCli)
    .WithReference(stripeCli, clientName: "ProductsReadOnly");

notifications.WithStripeCli(stripeCli);

builder.Build().Run();
