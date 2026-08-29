using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

/// <summary>
/// Tests for deferred resolution of the Stripe CLI webhook signing secret.
/// </summary>
/// <remarks>
/// The secret does not exist when the app host is built; it is scraped from CLI stdout after the
/// process starts. Environment injection must therefore resolve lazily. These tests are written so
/// that they fail against an eager implementation — see
/// <see cref="WebhookSecret_ResolvesAfterLateCapture"/> for the discriminating shape.
/// </remarks>
public class WebhookSecretDeferralTests
{
    private const string HealthCheckKey = "stripe.cli.webhook-secret.stripe";
    private const string CapturedSecret = "whsec_capturedAfterMaterialization";

    public enum ResourceKind
    {
        LocalExecutable,
        Container
    }

    private sealed record Scenario(
        DistributedApplication App,
        IStripeCliResource StripeResource,
        IResource Destination) : IDisposable
    {
        public void Dispose() => App.Dispose();
    }

    private static Scenario Build(ResourceKind kind)
    {
        var builder = DistributedApplication.CreateBuilder();
        var target = builder.AddContainer("target", "img").WithHttpEndpoint(port: 5099);

        IResourceBuilder<IStripeCliResource> stripe = kind == ResourceKind.LocalExecutable
            ? builder.AddStripeCli("stripe").WithWebhookForwardTo(target)
            : builder.AddStripeCliContainer("stripe").WithWebhookForwardTo(target);

        var destination = builder.AddContainer("api", "img");
        destination.WithReference(stripe);

        var app = builder.Build();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();

        return new Scenario(
            app,
            model.Resources.OfType<IStripeCliResource>().Single(),
            model.Resources.Single(r => r.Name == "api"));
    }

    private static void Capture(IStripeCliResource resource, string secret) =>
        resource.SetWebhookSigningSecret(secret);

    /// <summary>
    /// The discriminating test for deferred resolution.
    /// </summary>
    /// <remarks>
    /// The environment is materialized <em>once, before</em> the secret is captured, and the
    /// already-materialized dictionary is then resolved. A deferred implementation stores a value
    /// provider and resolves the live value; an eager implementation stores a plain string snapshot
    /// and yields the empty value it captured. Re-invoking the environment callbacks after capture
    /// would produce the correct value for both implementations and would prove nothing.
    /// </remarks>
    [Theory]
    [InlineData(ResourceKind.LocalExecutable)]
    [InlineData(ResourceKind.Container)]
    public async Task WebhookSecret_ResolvesAfterLateCapture(ResourceKind kind)
    {
        using var scenario = Build(kind);

        var materialized = await AspireEnv.MaterializeAsync(scenario.Destination);

        Assert.Equal(string.Empty, await AspireEnv.ResolveAsync(materialized["STRIPE_WEBHOOK_SECRET"]));

        Capture(scenario.StripeResource, CapturedSecret);

        Assert.Equal(CapturedSecret, await AspireEnv.ResolveAsync(materialized["STRIPE_WEBHOOK_SECRET"]));
        Assert.Equal(CapturedSecret, await AspireEnv.ResolveAsync(materialized["Stripe__Default__WebhookSecret"]));
    }

    /// <summary>
    /// The manifest expression must never carry the credential itself, regardless of whether the
    /// environment is materialized before or after the secret is captured.
    /// </summary>
    [Theory]
    [InlineData(ResourceKind.LocalExecutable)]
    [InlineData(ResourceKind.Container)]
    public async Task ValueExpression_NeverContainsSecret(ResourceKind kind)
    {
        using var scenario = Build(kind);

        // Materialize *after* capture: this is the ordering produced by the documented
        // WaitFor(stripeCli) pattern, where the destination starts only once the health check
        // reports the secret is available.
        Capture(scenario.StripeResource, CapturedSecret);

        foreach (var operation in new[] { DistributedApplicationOperation.Run, DistributedApplicationOperation.Publish })
        {
            var materialized = await AspireEnv.MaterializeAsync(scenario.Destination, operation);

            foreach (var key in new[] { "STRIPE_WEBHOOK_SECRET", "Stripe__Default__WebhookSecret" })
            {
                var expression = AspireEnv.ManifestExpressionOf(materialized[key]);

                Assert.NotNull(expression);
                Assert.DoesNotContain("whsec_", expression, StringComparison.OrdinalIgnoreCase);
                Assert.Equal("{stripe.webhookSigningSecret}", expression);
            }
        }
    }

    /// <summary>
    /// The health check is what <c>WaitFor</c> gates on, so it must flip from unhealthy to healthy
    /// when the secret is captured.
    /// </summary>
    [Theory]
    [InlineData(ResourceKind.LocalExecutable)]
    [InlineData(ResourceKind.Container)]
    public async Task HealthCheck_UnhealthyBeforeCapture_HealthyAfter(ResourceKind kind)
    {
        using var scenario = Build(kind);

        var registrations = scenario.App.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var registration = Assert.Single(registrations, r => r.Name == HealthCheckKey);
        var check = registration.Factory(scenario.App.Services);
        var context = new HealthCheckContext { Registration = registration };

        var before = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Unhealthy, before.Status);

        Capture(scenario.StripeResource, CapturedSecret);

        var after = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }

    /// <summary>
    /// An unrecognized <see cref="IStripeCliResource"/> implementation must fail loudly rather than
    /// silently discarding the captured secret, which would leave <c>WaitFor</c> hanging forever.
    /// </summary>
    [Fact]
    public void SetWebhookSigningSecret_ThrowsForUnknownImplementation()
    {
        var resource = new UnknownStripeCliResource();

        var exception = Assert.Throws<NotSupportedException>(
            () => resource.SetWebhookSigningSecret(CapturedSecret));

        Assert.Contains(nameof(UnknownStripeCliResource), exception.Message, StringComparison.Ordinal);
    }

    private sealed class UnknownStripeCliResource : IStripeCliResource
    {
        public string Name => "unknown";
        public ResourceAnnotationCollection Annotations { get; } = [];
        public string? WebhookSigningSecret => null;
        public ParameterResource? SecretKey => null;
        public ParameterResource? PublishableKey => null;
    }
}
