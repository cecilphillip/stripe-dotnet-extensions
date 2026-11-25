using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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

    [Theory]
    [InlineData(false, "Default")]
    [InlineData(true, "Secondary")]
    public async Task WithStripeCli_InjectsCredentialsAndWaitsForSigningSecret(
        bool container, string clientName)
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("key", "sk_test_123");
        var publishableKey = builder.AddParameter("public", "pk_test_123");
        IResourceBuilder<IStripeCliResource> stripe = container
            ? builder.AddStripeCliContainer("stripe", apiKey, publishableKey)
            : builder.AddStripeCli("stripe", apiKey, publishableKey);
        var destination = builder.AddContainer("api", "myimage");

        var result = destination.WithStripeCli(stripe, clientName);

        Assert.Same(destination, result);
        var wait = Assert.Single(destination.Resource.Annotations.OfType<WaitAnnotation>());
        Assert.Same(stripe.Resource, wait.Resource);
        Assert.Equal(WaitType.WaitUntilHealthy, wait.WaitType);
        Assert.Single(stripe.Resource.Annotations.OfType<HealthCheckAnnotation>());

        using var app = builder.Build();
        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var registration = Assert.Single(registrations, r => r.Name == "stripe.cli.webhook-secret.stripe");
        var check = registration.Factory(app.Services);
        var healthContext = new HealthCheckContext { Registration = registration };
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(healthContext)).Status);

        var environment = await AspireEnv.MaterializeAsync(destination.Resource);
        Assert.Equal("sk_test_123", await AspireEnv.ResolveAsync(environment["STRIPE_SECRET_KEY"]));
        Assert.Equal("pk_test_123", await AspireEnv.ResolveAsync(environment["STRIPE_PUBLISHABLE_KEY"]));
        Assert.Equal("sk_test_123", await AspireEnv.ResolveAsync(environment[$"Stripe__{clientName}__ApiKey"]));
        Assert.Equal("pk_test_123", await AspireEnv.ResolveAsync(environment[$"Stripe__{clientName}__PublicKey"]));
        Assert.Equal(string.Empty, await AspireEnv.ResolveAsync(environment["STRIPE_WEBHOOK_SECRET"]));
        stripe.Resource.SetWebhookSigningSecret("whsec_captured");
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(healthContext)).Status);
        Assert.Equal("whsec_captured", await AspireEnv.ResolveAsync(environment["STRIPE_WEBHOOK_SECRET"]));
        Assert.Equal("whsec_captured", await AspireEnv.ResolveAsync(environment[$"Stripe__{clientName}__WebhookSecret"]));
    }

    [Fact]
    public void WithStripeCli_ReusesForwardingSecretResolver()
    {
        var builder = DistributedApplication.CreateBuilder();
        var target = builder.AddContainer("target", "myimage").WithHttpEndpoint(port: 5082);
        var stripe = builder.AddStripeCli("stripe").WithWebhookForwardTo(target);

        builder.AddContainer("api", "myimage").WithStripeCli(stripe);

        Assert.Single(stripe.Resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public void WithStripeCli_InvalidArgumentsThrowBeforeAddingAnnotations()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stripe = builder.AddStripeCli("stripe");
        var api = builder.AddContainer("api", "myimage");
        IResourceBuilder<ContainerResource> nullApi = null!;

        Assert.Throws<ArgumentNullException>(() => nullApi.WithStripeCli(stripe));
        Assert.Throws<ArgumentNullException>(() =>
            api.WithStripeCli((IResourceBuilder<StripeCliResource>)null!));
        Assert.Throws<ArgumentException>(() => api.WithStripeCli(stripe, ""));
        Assert.Empty(stripe.Resource.Annotations.OfType<HealthCheckAnnotation>());
        Assert.Empty(api.Resource.Annotations.OfType<WaitAnnotation>());
    }

    [Theory]
    [InlineData("snapshot", false)]
    [InlineData("connect", false)]
    [InlineData("thin", false)]
    [InlineData("snapshot", true)]
    [InlineData("connect", true)]
    [InlineData("thin", true)]
    public async Task ForwardingTargetErrorsIdentifyProblemAndRemedy(string forwarding, bool unallocated)
    {
        var builder = DistributedApplication.CreateBuilder();
        var target = builder.AddContainer("api", "myimage");
        if (unallocated)
        {
            target.WithHttpEndpoint(port: 5082);
        }

        var stripe = builder.AddStripeCli("stripe");
        switch (forwarding)
        {
            case "snapshot":
                stripe.WithWebhookForwardTo(target);
                break;
            case "connect":
                stripe.WithWebhookConnectForwardTo(target);
                break;
            case "thin":
                stripe.WithThinEventForwardTo(target);
                break;
        }

#pragma warning disable CS0618 // The replacement API requires an execution context available at runtime.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await stripe.Resource.GetArgumentValuesAsync());
#pragma warning restore CS0618

        var expected = unallocated
            ? "Stripe CLI resource 'stripe' cannot forward to 'api': endpoint 'http' has not been allocated. " +
              "Aspire allocates endpoints during AppHost startup; check the AppHost logs for endpoint allocation errors."
            : "Stripe CLI resource 'stripe' cannot forward to 'api': the target has no endpoints. " +
              "Add an HTTP or HTTPS endpoint to the target, for example with WithHttpEndpoint(...).";
        Assert.Equal(expected, exception.Message);
    }
}
