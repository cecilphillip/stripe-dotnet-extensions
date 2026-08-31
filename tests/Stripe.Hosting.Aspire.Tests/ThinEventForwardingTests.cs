using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

public class ThinEventForwardingTests
{
    /// <summary>
    /// Endpoints are normally allocated when the AppHost starts. Allocating them by hand lets these
    /// tests assert the actual command line the Stripe CLI receives rather than just counting
    /// annotations.
    /// </summary>
    private static IResourceBuilder<ContainerResource> AddAllocatedContainer(
        IDistributedApplicationBuilder builder, string name, int port)
    {
        var container = builder.AddContainer(name, "myimage").WithHttpEndpoint(port: port);

        foreach (var endpoint in container.Resource.Annotations.OfType<EndpointAnnotation>())
        {
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port);
        }

        return container;
    }

    private static async Task<string[]> ResolveArgsAsync(IResourceWithArgs resource)
#pragma warning disable CS0618 // The replacement API requires an execution context that is only available at runtime.
        => await resource.GetArgumentValuesAsync();
#pragma warning restore CS0618

    [Fact]
    public async Task WithThinEventForwardTo_EmitsForwardThinToAndSubscribesToAllThinEvents()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = AddAllocatedContainer(builder, "api", 5082);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api, thinEventPath: "/stripe/thin-events");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        var forwardIndex = Array.IndexOf(args, "--forward-thin-to");
        Assert.True(forwardIndex >= 0, "--forward-thin-to was not emitted");
        Assert.EndsWith("/stripe/thin-events", args[forwardIndex + 1], StringComparison.Ordinal);

        // The Stripe CLI subscribes to no thin events by default, so --thin-events must always be
        // emitted or --forward-thin-to would be configured but never receive anything.
        var index = Array.IndexOf(args, "--thin-events");
        Assert.True(index >= 0, "--thin-events was not emitted");
        Assert.Equal("*", args[index + 1]);
    }

    [Fact]
    public async Task WithThinEventForwardTo_HonoursExplicitEventFilter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = AddAllocatedContainer(builder, "api", 5082);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(
                api,
                thinEventPath: "/stripe/thin-events",
                thinEvents: ["v2.core.account.created", "v2.core.account_person.created"]);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        var index = Array.IndexOf(args, "--thin-events");
        Assert.True(index >= 0, "--thin-events was not emitted");
        Assert.Equal("v2.core.account.created,v2.core.account_person.created", args[index + 1]);
    }

    [Fact]
    public async Task WithThinEventForwardTo_SkipVerify_EmitsSkipVerify()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api = AddAllocatedContainer(builder, "api", 5082);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api, thinEventPath: "/stripe/thin-events", skipVerify: true);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        Assert.Contains("--skip-verify", await ResolveArgsAsync(resource));
    }

    [Fact]
    public async Task WithThinEventForwardTo_CoexistsWithSnapshotForwarding()
    {
        var builder = DistributedApplication.CreateBuilder();
        var checkout = AddAllocatedContainer(builder, "checkout", 5082);
        var notifications = AddAllocatedContainer(builder, "notifications", 5083);

        builder.AddStripeCli("stripe")
            .WithWebhookForwardTo(checkout, webhookPath: "/stripe/webhook")
            .WithThinEventForwardTo(notifications, thinEventPath: "/stripe/thin-events");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        // Snapshot and thin events are separate delivery channels and need separate endpoints.
        var snapshot = Array.IndexOf(args, "--forward-to");
        var thin = Array.IndexOf(args, "--forward-thin-to");
        Assert.True(snapshot >= 0 && thin >= 0);
        Assert.EndsWith("/stripe/webhook", args[snapshot + 1], StringComparison.Ordinal);
        Assert.EndsWith("/stripe/thin-events", args[thin + 1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithThinEventForwardTo_MultipleTargets_EmitsFlagPerTarget()
    {
        var builder = DistributedApplication.CreateBuilder();
        var one = AddAllocatedContainer(builder, "api1", 5082);
        var two = AddAllocatedContainer(builder, "api2", 5083);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo("/stripe/thin-events", one, two);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        Assert.Equal(2, args.Count(a => a == "--forward-thin-to"));
        Assert.Contains("--thin-events", args);
    }

    [Fact]
    public void WithThinEventForwardTo_MultipleTargets_EmptyArray_Throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stripe = builder.AddStripeCli("stripe");

        Assert.Throws<ArgumentException>(() => stripe.WithThinEventForwardTo("/stripe/thin-events"));
    }

    [Fact]
    public async Task WithThinEventForwardTo_CalledTwice_EmitsSessionWideFlagsOnlyOnce()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api1 = AddAllocatedContainer(builder, "api1", 5191);
        var api2 = AddAllocatedContainer(builder, "api2", 5192);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api1, thinEventPath: "/a", skipVerify: true)
            .WithThinEventForwardTo(api2, thinEventPath: "/b", skipVerify: true);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        // Each target still gets its own --forward-thin-to.
        Assert.Equal(2, args.Count(a => a == "--forward-thin-to"));

        // --thin-events and --skip-verify are session-wide flags on `stripe listen`, so repeating
        // them is at best noise and at worst (for the StringSlice --thin-events) changes meaning.
        Assert.Equal(1, args.Count(a => a == "--thin-events"));
        Assert.Equal(1, args.Count(a => a == "--skip-verify"));
    }

    [Fact]
    public async Task WithThinEventForwardTo_CalledTwice_UnsetFilterWidensSessionToAllEvents()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api1 = AddAllocatedContainer(builder, "api1", 5193);
        var api2 = AddAllocatedContainer(builder, "api2", 5194);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api1, thinEventPath: "/a", thinEvents: ["v1.billing.meter.error_report_triggered"])
            .WithThinEventForwardTo(api2, thinEventPath: "/b");

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        // api2 left its filter unset, which the API documents as "all events". The CLI has a single
        // session-wide subscription list, so the only way to honour that is to widen to "*"; keeping
        // api1's narrow filter would silently starve api2. Extra events are harmless because the
        // endpoint simply leaves unclaimed notifications unhandled.
        var index = Array.IndexOf(args, "--thin-events");
        Assert.True(index >= 0, "--thin-events was not emitted");
        Assert.Equal("*", args[index + 1]);
        Assert.Equal(1, args.Count(a => a == "--thin-events"));
    }

    [Fact]
    public async Task WithThinEventForwardTo_CalledTwice_UnionsExplicitEventFilters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api1 = AddAllocatedContainer(builder, "api1", 5195);
        var api2 = AddAllocatedContainer(builder, "api2", 5196);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api1, thinEventPath: "/a", thinEvents: ["v2.core.account_link.completed", "shared"])
            .WithThinEventForwardTo(api2, thinEventPath: "/b", thinEvents: ["shared", "v2.core.account.created"]);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        // The CLI has one session-wide subscription list, so both calls' events must survive,
        // de-duplicated and in first-requested order.
        var index = Array.IndexOf(args, "--thin-events");
        Assert.True(index >= 0, "--thin-events was not emitted");
        Assert.Equal("v2.core.account_link.completed,shared,v2.core.account.created", args[index + 1]);
    }

    [Fact]
    public async Task WithThinEventForwardTo_SkipVerifyOnAnyCall_AppliesToTheSession()
    {
        var builder = DistributedApplication.CreateBuilder();
        var api1 = AddAllocatedContainer(builder, "api1", 5197);
        var api2 = AddAllocatedContainer(builder, "api2", 5198);

        builder.AddStripeCli("stripe")
            .WithThinEventForwardTo(api1, thinEventPath: "/a")
            .WithThinEventForwardTo(api2, thinEventPath: "/b", skipVerify: true);

        using var app = builder.Build();
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = Assert.Single(appModel.Resources.OfType<StripeCliResource>());

        var args = await ResolveArgsAsync(resource);

        // --skip-verify is session-wide, so a later opt-in still has to be honoured even though the
        // flag is emitted by the first call's deferred callback.
        Assert.Single(args, a => a == "--skip-verify");
    }

}
