using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

/// <summary>
/// Tests covering how the Stripe CLI resource behaves when the app host runs in publish mode.
/// </summary>
/// <remarks>
/// The Stripe CLI is excluded from the manifest because it is a local development tool, but the
/// documented usage pattern also calls <c>WaitFor(stripeCli)</c>. Left alone those two facts produce a
/// published artifact that depends on a resource which was never emitted — with the Docker Compose
/// publisher, <c>docker compose config</c> rejects the project with
/// <c>depends on undefined service "stripe-cli"</c>.
/// </remarks>
public class StripeCliPublishModeTests
{
    public enum CliKind
    {
        LocalExecutable,
        Container
    }

    private static IResourceBuilder<IResource> AddCli(IDistributedApplicationBuilder builder, CliKind kind) =>
        kind == CliKind.LocalExecutable
            ? builder.AddStripeCli("stripe-cli")
            : builder.AddStripeCliContainer("stripe-cli");

    /// <summary>
    /// Fires the <see cref="BeforeStartEvent"/> that the app host raises during both run and publish,
    /// which is where the wait annotations are reconciled.
    /// </summary>
    private static async Task<DistributedApplication> BuildAndRaiseBeforeStartAsync(IDistributedApplicationBuilder builder)
    {
        var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var eventing = app.Services.GetRequiredService<IDistributedApplicationEventing>();
        await eventing.PublishAsync(new BeforeStartEvent(app.Services, model));
        return app;
    }

    private static WaitAnnotation[] WaitAnnotationsTargeting(IResource resource, IResource target) =>
        resource.TryGetAnnotationsOfType<WaitAnnotation>(out var annotations)
            ? annotations.Where(a => ReferenceEquals(a.Resource, target)).ToArray()
            : [];

    [Theory]
    [InlineData(CliKind.LocalExecutable)]
    [InlineData(CliKind.Container)]
    public async Task PublishMode_DropsWaitAnnotationsTargetingStripeCli(CliKind kind)
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish"]);
        Assert.True(builder.ExecutionContext.IsPublishMode);

        var stripeCli = AddCli(builder, kind);
        var checkout = builder.AddContainer("checkout", "nginx").WaitFor(stripeCli);

        // Sanity check: WaitFor really did register before the reconciliation runs.
        Assert.Single(WaitAnnotationsTargeting(checkout.Resource, stripeCli.Resource));

        using var app = await BuildAndRaiseBeforeStartAsync(builder);

        Assert.Empty(WaitAnnotationsTargeting(checkout.Resource, stripeCli.Resource));
    }

    [Theory]
    [InlineData(CliKind.LocalExecutable)]
    [InlineData(CliKind.Container)]
    public void RunMode_PreservesWaitAnnotationsTargetingStripeCli(CliKind kind)
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.True(builder.ExecutionContext.IsRunMode);

        var stripeCli = AddCli(builder, kind);
        var checkout = builder.AddContainer("checkout", "nginx").WaitFor(stripeCli);

        using var app = builder.Build();

        // WaitFor is load bearing during run: it is what gates startup on the captured signing secret.
        // Unlike the publish-mode tests this does not raise BeforeStartEvent, because Aspire's built-in
        // run-mode handler for that event resolves DCP and dashboard paths that do not exist in a unit
        // test host. That is acceptable here: in run mode the reconciliation is never subscribed at all,
        // so there is no subscriber that could remove the annotation.
        Assert.Single(WaitAnnotationsTargeting(checkout.Resource, stripeCli.Resource));
    }

    [Theory]
    [InlineData(CliKind.LocalExecutable)]
    [InlineData(CliKind.Container)]
    public async Task PublishMode_PreservesWaitAnnotationsTargetingOtherResources(CliKind kind)
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish"]);

        var stripeCli = AddCli(builder, kind);
        var database = builder.AddContainer("database", "postgres");
        var checkout = builder.AddContainer("checkout", "nginx")
            .WaitFor(stripeCli)
            .WaitFor(database);

        using var app = await BuildAndRaiseBeforeStartAsync(builder);

        // Only the Stripe CLI relationship is removed; unrelated dependencies must survive.
        Assert.Empty(WaitAnnotationsTargeting(checkout.Resource, stripeCli.Resource));
        Assert.Single(WaitAnnotationsTargeting(checkout.Resource, database.Resource));
    }
}
