using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

public class StripeCliContainerExtensionsTests
{
    private const string TestApiKeyValue = "sk_test_123";

    // Docker image constants — kept in sync with StripeCliContainerImageTags
    private const string ExpectedRegistry = "docker.io";
    private const string ExpectedImage = "stripe/stripe-cli";
    private const string ExpectedTag = "v1.33.0";

    [Fact]
    public void AddStripeCliContainer_RegistersContainerResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCliContainer("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();

        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());
        Assert.Equal("stripe", resource.Name);
    }

    [Fact]
    public void AddStripeCliContainer_ConfiguresCorrectContainerImage()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCliContainer("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        var imageAnnotation = Assert.Single(resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal(ExpectedImage, imageAnnotation.Image);
        Assert.Equal(ExpectedTag, imageAnnotation.Tag);
        Assert.Equal(ExpectedRegistry, imageAnnotation.Registry);
    }

    [Fact]
    public async Task AddStripeCliContainer_AddsListenAsFirstArg()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddStripeCliContainer("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        var args = await resource.GetArgumentValuesAsync();

        Assert.Contains("listen", args);
        Assert.Equal("listen", args[0]);
    }

    [Fact]
    public void AddStripeCliContainer_WithApiKey_SetsEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("stripe-api-key", TestApiKeyValue);

        builder.AddStripeCliContainer("stripe", apiKey: apiKey);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        var envAnnotations = resource.Annotations.OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void AddStripeCliContainer_NullBuilder_Throws()
    {
        IDistributedApplicationBuilder builder = null!;
        Assert.Throws<ArgumentNullException>(() => builder.AddStripeCliContainer("stripe"));
    }

    [Fact]
    public void AddStripeCliContainer_EmptyName_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddStripeCliContainer(""));
    }

    [Fact]
    public void WithWebhookForwardTo_Container_SingleTarget_AddsCallbackAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCliContainer("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        // listen (1) + forward-to callback (1) = 2
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(2, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_Container_MultipleTargets_AddsCallbackPerTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var service1 = builder.AddContainer("api1", "img").WithHttpEndpoint(port: 5082);
        var service2 = builder.AddContainer("api2", "img").WithHttpEndpoint(port: 5083);

        builder.AddStripeCliContainer("stripe")
            .WithWebhookForwardTo("/webhooks/stripe", service1, service2);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        // listen (1) + 2 target callbacks = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_Container_WithEvents_AddsEventsAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCliContainer("stripe")
            .WithWebhookForwardTo(api, webhookPath: "/webhooks/stripe",
                                  events: ["payment_intent.created", "charge.succeeded"]);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        // listen (1) + forward-to (1) + events (1) = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithWebhookForwardTo_Container_SkipVerify_AddsSkipVerifyAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("api", "myimage").WithHttpEndpoint(port: 5082);

        builder.AddStripeCliContainer("stripe")
            .WithWebhookForwardTo(api, skipVerify: true);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        // listen (1) + forward-to (1) + skip-verify (1) = 3
        var argsAnnotations = resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>();
        Assert.Equal(3, argsAnnotations.Count());
    }

    [Fact]
    public void WithReference_Container_InjectsWebhookSecretEnvVar()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = builder.AddContainer("target", "myimage").WithHttpEndpoint(port: 5082);
        var stripe = builder.AddStripeCliContainer("stripe")
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
    public async Task WithApiKey_Container_AddsApiKeyArg()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apiKey = builder.AddParameter("api-key", TestApiKeyValue);

        builder.AddStripeCliContainer("stripe")
            .WithApiKey(apiKey);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        var args = await resource.GetArgumentValuesAsync();

        Assert.Contains("--api-key", args);
        Assert.Contains(TestApiKeyValue, args);
    }

    [Fact]
    public void WebhookSigningSecret_InitiallyNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddStripeCliContainer("stripe");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliContainerResource>());

        Assert.Null(resource.WebhookSigningSecret);
    }

    [Fact]
    public void StripeCliContainerResource_ImplementsIStripeCliResource()
    {
        var resource = new StripeCliContainerResource("test");
        Assert.IsAssignableFrom<IStripeCliResource>(resource);
    }

    [Fact]
    public void StripeCliResource_ImplementsIStripeCliResource()
    {
        var resource = new StripeCliResource("test", "stripe", "/tmp");
        Assert.IsAssignableFrom<IStripeCliResource>(resource);
    }
}
