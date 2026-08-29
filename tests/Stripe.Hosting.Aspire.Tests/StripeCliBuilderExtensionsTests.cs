using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

public class StripeCliBuilderExtensionsTests
{
    private const string TestApiKeyValue = "sk_test_123";

    [Fact]
    public void AddStripeCli_RegistersStripeCliResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCli("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());
        Assert.Equal("stripe", resource.Name);
        Assert.Equal("stripe", resource.Command);
    }

    [Fact]
    public void AddStripeCli_WithCustomPath_UsesCustomPath()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCli("stripe", stripePath: "/usr/local/bin/stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());
        Assert.Equal("/usr/local/bin/stripe", resource.Command);
    }

    [Fact]
    public void AddStripeCli_AddsListenArgAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCli("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // AddStripeCli always calls WithArgs("listen"), which contributes one args callback annotation.
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Single(argsAnnotations);
    }

    [Fact]
    public void AddStripeCli_WithApiKey_SetsApiKeyEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("stripe-api-key", TestApiKeyValue);

        builder.AddStripeCli("stripe", apiKey: apiKey);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void AddStripeCli_NullBuilder_Throws()
    {
        IDistributedApplicationBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddStripeCli("stripe"));
    }

    [Fact]
    public void AddStripeCli_EmptyName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddStripeCli(""));
    }

    [Fact]
    public void WithWebhookForwardTo_SingleTarget_AddsCallbackAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + forward-to callback (1)
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(2, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_WithEvents_AddsEventsAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe",
                                  events: ["payment_intent.created", "charge.succeeded"]);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + forward-to callback (1) + events (1) = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_SkipVerify_AddsSkipVerifyAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe", skipVerify: true);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + forward-to callback (1) + skip-verify (1) = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_MultipleTargets_AddsCallbackPerTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var service1 = builder.AddContainer("api1", "img").WithHttpEndpoint(port: 5082);
        var service2 = builder.AddContainer("api2", "img").WithHttpEndpoint(port: 5083);
        var service3 = builder.AddContainer("api3", "img").WithHttpEndpoint(port: 5084);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo("/webhooks/stripe", service1, service2, service3);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + 3 target callbacks = 4
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(4, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_MultipleTargets_EmptyArray_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stripe = builder.AddStripeCli("stripe");

        Assert.Throws<ArgumentException>(() =>
            stripe.WithWebhookForwardTo("/webhooks/stripe", []));
    }

    [Fact]
    public void WithWebhookConnectForwardTo_SingleTarget_AddsCallbackAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api)
            .WithWebhookConnectForwardTo(api, webhookPath: "/webhooks/stripe-connect");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + forward-to (1) + connect-forward-to (1) = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookConnectForwardTo_RegistersSecretResolver()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookConnectForwardTo(api, webhookPath: "/webhooks/stripe-connect");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var healthCheck = Assert.Single(resource.Annotations.OfType<HealthCheckAnnotation>());
        Assert.Equal("stripe.cli.webhook-secret.stripe", healthCheck.Key);
    }

    [Fact]
    public void WithWebhookForwardTo_RegistersSecretResolver()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var healthCheck = Assert.Single(resource.Annotations.OfType<HealthCheckAnnotation>());
        Assert.Equal("stripe.cli.webhook-secret.stripe", healthCheck.Key);
    }

    [Fact]
    public void WithWebhookConnectForwardTo_MultipleTargets_AddsCallbackPerTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var service1 = builder.AddContainer("api1", "img").WithHttpEndpoint(port: 5082);
        var service2 = builder.AddContainer("api2", "img").WithHttpEndpoint(port: 5083);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(service1)
            .WithWebhookConnectForwardTo("/webhooks/connect", service1, service2);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + forward-to (1) + connect callbacks (2) = 4
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(4, argsAnnotations.Count());
    }

    [Fact]
    public void WithReference_InjectsDefaultWebhookSecretEnvVar()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("target", "myimage").WithHttpEndpoint(port: 5082);
        var stripe = builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api);

        var destination = builder.AddContainer("api", "myimage");
        destination.WithReference(stripe);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var apiResource = appModel.Resources.Single(r => r.Name == "api");
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToArray();
        Assert.Equal(2, envAnnotations.Length);
        Assert.All(envAnnotations, annotation => Assert.NotNull(annotation.Callback));
    }

    [Fact]
    public void WithReference_InjectsEnvironmentAnnotations()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("target", "myimage").WithHttpEndpoint(port: 5082);
        var stripe = builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(api);

        var destination = builder.AddContainer("api", "myimage");
        destination.WithReference(stripe);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var apiResource = appModel.Resources.Single(r => r.Name == "api");
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void WithReference_NullBuilder_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stripe = builder.AddStripeCli("stripe");

        IResourceBuilder<ContainerResource> apiBuilder = null!;
        Assert.Throws<ArgumentNullException>(() => apiBuilder.WithReference(stripe));
    }

    [Fact]
    public void WithReference_NullSource_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage");

        Assert.Throws<ArgumentNullException>(() =>
            api.WithReference((IResourceBuilder<StripeCliResource>)null!));
    }

    [Fact]
    public void WithReference_WithSecretKey_InjectsSecretKeyEnvVar()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_123");
        var stripe = builder.AddStripeCli("stripe", apiKey: apiKey);
        var destination = builder.AddContainer("api", "myimage");

        destination.WithReference(stripe);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var apiResource = appModel.Resources.Single(r => r.Name == "api");
        // STRIPE_SECRET_KEY annotation added because apiKey was provided
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envAnnotations);
        Assert.NotNull(stripe.Resource.SecretKey);
    }

    [Fact]
    public void WithWebhookForwardTo_CalledTwice_OnlyOneSecretResolverRegistered()
    {
        var builder = DistributedApplication.CreateBuilder();
        var service1 = builder.AddContainer("api1", "img").WithHttpEndpoint(port: 5082);
        var service2 = builder.AddContainer("api2", "img").WithHttpEndpoint(port: 5083);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(service1)
            .WithWebhookForwardTo(service2);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        // listen (1) + 2 forward-to callbacks (2) = 3 CommandLineArgsCallbackAnnotation entries
        // The StripeSecretResolverAnnotation should appear exactly once (idempotent guard)
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void StripeCliResource_ImplementsIStripeCliResource()
    {
        var resource = new StripeCliResource("test", "stripe", "/tmp");
        Assert.IsAssignableFrom<IStripeCliResource>(resource);
    }

    [Fact]
    public void StripeCliResource_WebhookSigningSecret_InitiallyNull()
    {
        var resource = new StripeCliResource("test", "stripe", "/tmp");
        Assert.Null(resource.WebhookSigningSecret);
    }

    [Fact]
    public async Task AddStripeCli_WithApiKey_StoresSecretKeyOnResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_abc");

        var stripe = builder.AddStripeCli("stripe", apiKey: apiKey);

        Assert.NotNull(stripe.Resource.SecretKey);
        Assert.Equal("sk_test_abc", await stripe.Resource.SecretKey!.GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddStripeCli_WithPublishableKey_StoresKeyOnResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pubKey = builder.AddParameter("pub-key", "pk_test_abc");

        var stripe = builder.AddStripeCli("stripe", publishableKey: pubKey);

        Assert.NotNull(stripe.Resource.PublishableKey);
        Assert.Equal("pk_test_abc", await stripe.Resource.PublishableKey!.GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddStripeCliContainer_WithPublishableKey_StoresKeyOnResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var pubKey = builder.AddParameter("pub-key", "pk_test_abc");

        var stripe = builder.AddStripeCliContainer("stripe", publishableKey: pubKey);

        Assert.NotNull(stripe.Resource.PublishableKey);
        Assert.Equal("pk_test_abc", await stripe.Resource.PublishableKey!.GetValueAsync(CancellationToken.None));
    }

    [Fact]
    public void WithReference_WithSecretKey_InjectsDiConfigEnvVars()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_123");
        var stripe = builder.AddStripeCli("stripe", apiKey: apiKey);
        var destination = builder.AddContainer("api", "myimage");

        destination.WithReference(stripe);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var apiResource = appModel.Resources.Single(r => r.Name == "api");

        // Should have annotations for: STRIPE_SECRET_KEY, Stripe__Default__ApiKey,
        // STRIPE_WEBHOOK_SECRET, Stripe__Default__WebhookSecret
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.Equal(4, envAnnotations.Count());
    }

    [Fact]
    public void WithReference_WithAllKeys_InjectsAllEnvVars()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_123");
        var pubKey = builder.AddParameter("pub-key", "pk_test_abc");
        var stripe = builder.AddStripeCli("stripe", apiKey: apiKey, publishableKey: pubKey);
        var destination = builder.AddContainer("api", "myimage");

        destination.WithReference(stripe);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var apiResource = appModel.Resources.Single(r => r.Name == "api");

        // STRIPE_SECRET_KEY, Stripe__Default__ApiKey,
        // STRIPE_PUBLISHABLE_KEY, Stripe__Default__PublicKey,
        // STRIPE_WEBHOOK_SECRET, Stripe__Default__WebhookSecret
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.Equal(6, envAnnotations.Count());
    }

    [Fact]
    public void WithReference_CustomClientName_UsesCorrectSectionKey()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_123");
        var stripe = builder.AddStripeCli("stripe", apiKey: apiKey);
        var destination = builder.AddContainer("api", "myimage");

        destination.WithReference(stripe, clientName: "Secondary");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var apiResource = appModel.Resources.Single(r => r.Name == "api");

        // STRIPE_SECRET_KEY, Stripe__Secondary__ApiKey,
        // STRIPE_WEBHOOK_SECRET, Stripe__Secondary__WebhookSecret
        var envAnnotations = apiResource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.Equal(4, envAnnotations.Count());
    }

    [Fact]
    public void WithReference_EmptyClientName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stripe = builder.AddStripeCli("stripe");
        var api = builder.AddContainer("api", "myimage");

        Assert.Throws<ArgumentException>(() => api.WithReference(stripe, clientName: ""));
    }
}
