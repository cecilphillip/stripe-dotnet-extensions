# Stripe .NET Extensions

![logo](stripe_logo_blurple.png)

![](https://github.com/cecilphillip-stripe/stripe-dotnet-extensions/actions/workflows/build.yml/badge.svg)

The Stripe .NET Extension packages provide a collection of convenient features 
to help improve the experience integrating Stripe in .NET applications. 

- **Stripe.Extensions.DependencyInjection** — configuration and dependency injection support for the [Stripe .NET SDK](https://github.com/stripe/stripe-dotnet).
- **Stripe.Extensions.AspNetCore** — webhook handling helpers for Stripe [events](https://docs.stripe.com/api/events/types) in ASP.NET Core applications.
- **Stripe.Hosting.Aspire** — [Aspire](https://aspire.dev) hosting integration for the Stripe CLI, enabling local webhook forwarding during development.


## Install

```shell
dotnet add package Stripe.Extensions.DependencyInjection
dotnet add package Stripe.Extensions.AspNetCore

# For Aspire AppHost projects
dotnet add package Stripe.Hosting.Aspire
```

## Building Locally

This project uses [Just](https://just.systems/) for build automation. 

### Prerequisites
- .NET 10.0 SDK or later
- Just (install via `brew install just` on macOS/Linux, or see [just.systems](https://just.systems/) for other platforms)

### Available Commands

```bash
# List all available build recipes
just

# Build the solution (Release configuration)
just build

# Run all tests
just test

# Run a specific test
just test-filter "FullyQualifiedName~YourTest"

# Create NuGet packages
just pack

# Clean all build artifacts
just clean

# Full CI pipeline (clean → build → test → pack)
just ci
```

You can also use `dotnet` commands directly if you prefer:
```bash
dotnet build
dotnet test
dotnet pack
```

### Dependency Injection & Configuration

Using `Stripe.Extensions.DependencyInjection` you can register named and unnamed versions of `StripeClient` using `AddStripe()`.
The `StripeClient` service is registered as scoped. 

```csharp
builder.Services.AddStripe();
```

The `AddStripe()` extension also supports registering named `StripeClient` instances, which uses keyed DI registrations.

```csharp
builder.Services.AddStripe(); // default client
builder.Services.AddStripe("client1"); // named client1
builder.Services.AddStripe("client2"); // named client2
```

### Configuration

The Stripe [API keys](https://docs.stripe.com/keys#obtain-api-keys) need to be configured in your application before calls can be made using the SDK.
The extension packages will look for a `Stripe` configuration section when calling `AddStripe()`. Configuring multiple clients is also supported by
using the client name as the key in the configuration section. When configuring the default client without a client name, the key should be `Default`.

To configure the default client when using `AddStripe()`: 
```json
{
  "Stripe": { 
    "Default" : {
      "ApiKey": "<secret key>",
      "WebhookSecret": "<webhook secret>"
    }
  } 
}
```

To configure a client named `client1` when using `AddStripe("client1")`::
```json
{
  "Stripe": {
    "client1": {
      "ApiKey": "<secret key>",
      "WebhookSecret": "<webhook secret>"
    }
  }
}
```

Configuration can also be attached to each registered client by passing a configuration delegate.

```csharp
// default registration 
builder.Services.AddStripe(configureOptions: opts =>
{
    opts.ApiKey = "<secret key>";
    opts.WebhookSecret = "<webhook secret>";
});

// name registration 
builder.Services.AddStripe("client1", opts =>
{
    opts.ApiKey = "<secret key>";
    opts.WebhookSecret = "<webhook secret>";
});
```

> See [StripeOptions](src/Stripe.Extensions.DependencyInjection/StripeOptions.cs) for all the available options.

### Aspire event forwarding

`Stripe.Hosting.Aspire` includes convenience APIs for wiring the Stripe CLI to Aspire resources during local development.

```csharp
var stripe = builder.AddStripeCli("stripe");

stripe.WithWebhookForwardTo(api);
stripe.WithWebhookConnectForwardTo(api);

// Multiple targets are also supported
stripe.WithWebhookForwardTo("/webhooks/stripe", api, worker);
stripe.WithWebhookConnectForwardTo("/webhooks/stripe-connect", api, worker);
```

`WithReference(stripe)` injects the Stripe API key, publishable key, and webhook secret into dependent resources. The webhook secret is resolved when the CLI starts, so dependent resources can use `WaitFor(stripe)` before starting.

For Stripe v1 events, use `MapStripeWebhookHandler<T>()` / `StripeWebhookHandler<T>`.
For Stripe v2 thin events and snapshot events, use `MapStripeThinEventHandler<T>()` / `StripeThinEventHandler<T>`.

Retrieving the default client registered with `AddStripe()`: 

```csharp
public class HomeController : Controller
{
    private readonly StripeClient _stripeClient;

    public HomeController(StripeClient stripeClient)
    {
        _stripeClient = stripeClient;
    }
    
    public async Task<IActionResult> Index()
    {        
        var customer = await _stripeClient.V1.Customers.GetAsync("cus_NffrFeUfNV2Hib");        
        ...
        return View();
    } 
}
```

Retrieving a client registered with `AddStripe("client1")`:

```csharp
public class HomeController : Controller
{
    private readonly StripeClient _stripeClient;

    public HomeController([FromKeyedServices("client1")] StripeClient stripeClient)
    {
        _stripeClient = stripeClient;
    }
}
```

## Webhook handling

The `Stripe.Extensions.AspNetCore` package simplifies Webhook handling by automating the event parsing, signature validation and logging.
All that's needed is to override the appropriate events of the handler class.

### Snapshot Events (v1)

Create a handler class that inherits from [StripeWebhookHandler](./src/Stripe.Extensions.AspNetCore/StripeWebhookHandler.cs), which provides virtual methods for all known webhook events.
To handle an event override the corresponding `On*Async` method.

```csharp
public class MyWebhookHandler: StripeWebhookHandler<MyWebhookHandler>();
{
    public MyWebhookHandler(StripeWebhookContext context) : base(context) {}
    
    public override Task OnCustomerCreatedAsync(Event e)
    {
        // handle customer.create event
        var customer = (e.Data.Object as Customer);        
    }
}
```
Each handler has a single constructor that accepts an instance of [StripeWebhookContext](./src/Stripe.Extensions.AspNetCore/StripeWebhookContext.cs), which provides
access to `StripeClient`, the configured `StripeOptions` and an instance of `ILogger`.


The last step is to register the webhook handler with ASP.NET Core routing by calling `MapStripeWebhookHandler`.

```csharp
app.MapStripeWebhookHandler<MyWebhookHandler>();
```

### Thin Events (v2)

Stripe v2 APIs generate [thin events](https://docs.stripe.com/event-destinations#thin-events) - lightweight event notifications that contain only the event type and related object ID. These are strongly-typed in the Stripe SDK.

Create a handler class that inherits from [StripeThinEventHandler](./src/Stripe.Extensions.AspNetCore/StripeThinEventHandler.cs):

```csharp
public class MyThinEventHandler : StripeThinEventHandler<MyThinEventHandler>
{
    public MyThinEventHandler(StripeWebhookContext context) : base(context) { }
    
    public override async Task OnV1BillingMeterErrorReportTriggeredAsync(
        V1BillingMeterErrorReportTriggeredEventNotification notification)
    {
        // Option 1: Fetch the full event with additional data
        var fullEvent = await notification.FetchEventAsync();
        
        // Option 2: Fetch the related object directly
        var meter = await notification.FetchRelatedObjectAsync();
        
        // Process the event...
    }

    public override async Task OnV2CoreAccountCreatedAsync(
        V2CoreAccountCreatedEventNotification notification)
    {
        var account = await notification.FetchRelatedObjectAsync();
        // Handle account creation...
    }
}
```

Register the thin event handler with a separate endpoint:

```csharp
// Minimal API based apps
app.MapStripeThinEventHandler<MyThinEventHandler>("/stripe/thin-event");

// Or with a named configuration
app.MapStripeThinEventHandler<MyThinEventHandler>("/stripe/thin-event", "client1");
```

Key differences from snapshot events:
- Thin events use `EventNotification` types from `Stripe.Events.*` namespace
- Each notification provides `FetchEventAsync()` to get the full event with additional data
- Each notification provides `FetchRelatedObjectAsync()` to fetch the latest version of the related resource
- Unknown event types return `UnknownEventNotification`

### Dependency Injection in StripeWebhookHandler

The `StripeWebhookHandler` also supports constructor dependency injection, so Stripe or other services can be injected by defining them as constructor parameters.

```csharp
public class MyWebhookHandler: StripeWebhookHandler<MyWebhookHandler>
{
    private readonly IMyService _myService;
    public MyWebhookHandler(IMyService myService, StripeWebhookContext context) : base(context) {}
    {
        _myService = myService;
    }

    public override async Task OnCustomerCreatedAsync(Event e)
    {
        Customer customer = (Customer)e.Data.Object;
        await Context.Client.V1.Customers.UpdateAsync(customer.Id, new CustomerUpdateOptions()
        {
            Description = "New customer"
        });
    }
}
```

## Unit testing

The `StripeWebhookHandler` also simplifies unit testing of webhook handling logic.
For example, here is how a unit-test might be written to test the logic of the handler from the previous section:

```csharp
[Fact]
public async Task UpdatesCustomerOnCreation()
{
    var serviceMock = new Mock<CustomerService>();
    var handler = new MyWebhookHandler(serviceMock.Object);
    // Prepare the event
    var e = new Event()
    {
        Data = new EventData()
        {
            Object = new Customer()
            {
                Id = "cus_123"
            }
        }
    };

    // Invoke the handler
    await handler.OnCustomerCreatedAsync(e);

    // Verify that the customer was updated with a new description
    serviceMock.Verify(s => s.UpdateAsync(
        "cus_123",
        It.Is<CustomerUpdateOptions>(o => o.Description == "New customer"),
        It.IsAny<RequestOptions>(),
        It.IsAny<CancellationToken>()));
}
```

## Aspire Integration

`Stripe.Hosting.Aspire` adds the Stripe CLI to your Aspire AppHost so it automatically forwards webhook events to your local services during development. It supports two modes: a **locally installed Stripe CLI** or the official **Docker image**.

### Prerequisites

**Local CLI mode**: Install the [Stripe CLI](https://docs.stripe.com/stripe-cli) and run `stripe login` once.

**Docker container mode**: Docker must be running. No local Stripe CLI installation required.

### Install

In your AppHost project:

```shell
dotnet add package Stripe.Hosting.Aspire
```

### Quick start

Store your Stripe API keys as user secrets in the AppHost project:

```shell
aspire secret set "Parameters:stripe-api-key"         "sk_test_..."
aspire secret set "Parameters:stripe-publishable-key" "pk_test_..."
```

Then wire up the Stripe CLI in your AppHost:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var stripeApiKey        = builder.AddParameter("stripe-api-key",         secret: true);
var stripePublishableKey = builder.AddParameter("stripe-publishable-key", secret: false);

var api = builder.AddProject<Projects.MyApi>("api");

// Docker container mode (no local Stripe CLI required)
var stripeCli = builder.AddStripeCliContainer("stripe-cli",
        apiKey: stripeApiKey,
        publishableKey: stripePublishableKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

// WaitFor ensures the api starts only after the signing secret is captured
api.WithReference(stripeCli)
   .WaitFor(stripeCli);

builder.Build().Run();
```

Switch to the locally installed CLI by replacing `AddStripeCliContainer` with `AddStripeCli`:

```csharp
var stripeCli = builder.AddStripeCli("stripe-cli",
        apiKey: stripeApiKey,
        publishableKey: stripePublishableKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");
```

### Injected environment variables

`WithReference(stripeCli)` injects the following environment variables into the dependent service, covering both standalone usage and zero-config `services.AddStripe()`:

| Environment variable | Config path (`Stripe:Default:*`) | Value |
|---|---|---|
| `STRIPE_SECRET_KEY` | `Stripe__Default__ApiKey` | Secret API key |
| `STRIPE_PUBLISHABLE_KEY` | `Stripe__Default__PublicKey` | Publishable key |
| `STRIPE_WEBHOOK_SECRET` | `Stripe__Default__WebhookSecret` | Signing secret from CLI output |

Because the `Stripe__Default__*` variables map directly to the `Stripe:Default` configuration section, calling `services.AddStripe()` in the dependent service requires **no additional configuration** — all values are supplied automatically at startup.

To target a named client (e.g. `services.AddStripe("payments")`), pass the client name:

```csharp
api.WithReference(stripeCli, clientName: "payments");
// injects Stripe__payments__ApiKey, Stripe__payments__WebhookSecret, etc.
```

### Forwarding to multiple services

```csharp
var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey)
    .WithWebhookForwardTo("/webhooks/stripe", api, paymentsService, notificationsService);
```

### Stripe Connect webhooks

```csharp
var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe")
    .WithWebhookConnectForwardTo(api, webhookPath: "/webhooks/stripe-connect");
```

### Filtering events

```csharp
var stripeCli = builder.AddStripeCliContainer("stripe-cli", apiKey: stripeApiKey)
    .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe",
        events: ["payment_intent.succeeded", "customer.created"]);
```

### How it works

1. The Stripe CLI starts with `stripe listen --forward-to <url>` (local mode) or as a Docker container (container mode).
2. The integration watches the CLI stdout for the `whsec_...` signing secret printed at startup.
3. Once captured, a health check on the `stripe-cli` resource transitions to **Healthy**.
4. `WaitFor(stripeCli)` holds the dependent service until the health check passes, guaranteeing `STRIPE_WEBHOOK_SECRET` is populated before the service starts.
5. On **macOS/Windows** (Docker Desktop), `localhost` in `--forward-to` URLs is automatically rewritten to `host.docker.internal`. On **Linux**, `--add-host=host.docker.internal:host-gateway` is injected into the container runtime args.

### Publish mode

The Stripe CLI is a local development tool, so it is excluded from published artifacts (`aspire publish`). Two things follow from that:

- **The webhook secret becomes a deployment parameter.** Because the CLI never runs during publish, there is no secret to capture. The environment variables are still emitted, but as an unresolved placeholder for you to supply at deploy time — for example, the Docker Compose publisher writes `STRIPE_WEBHOOK_SECRET: "${STRIPE_CLI_WEBHOOKSIGNINGSECRET}"` and adds a matching blank entry to `.env`. In production, supply the signing secret of a real webhook endpoint created in the Stripe Dashboard rather than one from the CLI.
- **`WaitFor(stripeCli)` is dropped automatically.** Waiting on a resource that was excluded from the manifest would emit a dependency on a service that does not exist in the output (with the Docker Compose publisher, `docker compose config` rejects the project outright). The integration removes those wait relationships during publish, so you can keep using `WaitFor` unconditionally in your AppHost. It remains fully active in run mode, where it is what guarantees the secret is populated before your service starts.

No credential is ever written into published artifacts.

### Additional Information

- [Stripe CLI documentation](https://docs.stripe.com/stripe-cli)
- [Testing webhooks locally](https://docs.stripe.com/webhooks/test)
- [Aspire documentation](https://aspire.dev)

### Useful links
- [Stripe.NET](https://github.com/stripe/stripe-dotnet)
- [Stripe Docs](https://docs.stripe.com)
- [Stripe API Reference](https://docs.stripe.com/api)

To keep track of major Stripe API updates and versions, reference the 
[API upgrades page](https://docs.stripe.com/upgrades#api-versions) in the Stripe documentation. 
For a detailed list of API changes, please refer to the [API Changelog](https://docs.stripe.com/changelog).

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for:
- Development setup instructions
- Build and test commands
- Code style guidelines
- Pull request process

## License

This project is licensed under the MIT License. See [LICENSE.md](LICENSE.md) for details.
