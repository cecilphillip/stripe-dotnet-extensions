using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Stripe.Hosting.Aspire;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

// Extension methods go in Aspire.Hosting namespace so they are discovered automatically
// when the Aspire.Hosting package is referenced, without needing an extra using directive.
namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding Stripe CLI resources to a <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
public static class StripeCliBuilderExtensions
{
    private const string DefaultWebhookPath = "/webhooks/stripe";
    private const string DefaultConnectWebhookPath = "/webhooks/stripe-connect";
    private const string DefaultWebhookSecretEnvVar = "STRIPE_WEBHOOK_SECRET";
    private const string SecretKeyEnvVar = "STRIPE_SECRET_KEY";
    private const string PublishableKeyEnvVar = "STRIPE_PUBLISHABLE_KEY";
    private const string ApiKeyEnvVar = "STRIPE_API_KEY";

    // On Docker Desktop (Windows/macOS), host.docker.internal resolves to the host machine.
    // On Linux, host.docker.internal is not set up automatically; we add --add-host instead.
    private static readonly bool IsDockerDesktop =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Adds a locally installed Stripe CLI resource to the application model.
    /// The <c>stripe</c> executable must be available in the system PATH or provided via <paramref name="stripePath"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="apiKey">Optional parameter resource providing the Stripe secret API key.</param>
    /// <param name="stripePath">Optional path to the Stripe CLI executable. Defaults to <c>stripe</c> (resolved from PATH).</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{StripeCliResource}"/>.</returns>
    [AspireExport("addStripeCli", Description = "Adds a locally installed Stripe CLI resource for webhook forwarding")]
    public static IResourceBuilder<StripeCliResource> AddStripeCli(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? apiKey = null,
        IResourceBuilder<ParameterResource>? publishableKey = null,
        string? stripePath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var command = stripePath ?? "stripe";
        var resource = new StripeCliResource(name, command, builder.AppHostDirectory);

        var resourceBuilder = builder.AddResource(resource)
            .ExcludeFromManifest()
            .WithArgs("listen");

        if (apiKey is not null)
        {
            resource.SecretKey = apiKey.Resource;
            resourceBuilder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[ApiKeyEnvVar] = ReferenceExpression.Create($"{apiKey.Resource}");
                return Task.CompletedTask;
            });
        }

        if (publishableKey is not null)
        {
            resource.PublishableKey = publishableKey.Resource;
        }

        DropWaitAnnotationsWhenPublishing(builder, resource);

        return resourceBuilder;
    }

    /// <summary>
    /// Adds a Stripe CLI Docker container resource to the application model.
    /// Uses the official <c>stripe/stripe-cli</c> Docker image.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="apiKey">Optional parameter resource providing the Stripe secret API key.</param>
    /// <param name="publishableKey">Optional parameter resource providing the Stripe publishable key.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{StripeCliContainerResource}"/>.</returns>
    [AspireExport("addStripeCliContainer", Description = "Adds a Stripe CLI Docker container resource for webhook forwarding")]
    public static IResourceBuilder<StripeCliContainerResource> AddStripeCliContainer(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<ParameterResource>? apiKey = null,
        IResourceBuilder<ParameterResource>? publishableKey = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        var resource = new StripeCliContainerResource(name);

        var resourceBuilder = builder.AddResource(resource)
            .WithImage(StripeCliContainerImageTags.Image, StripeCliContainerImageTags.Tag)
            .WithImageRegistry(StripeCliContainerImageTags.Registry)
            .ExcludeFromManifest()
            .WithArgs("listen");

        // On Linux, host.docker.internal is not automatically defined in containers.
        // Adding --add-host makes host.docker.internal resolve to the host gateway,
        // allowing the container to reach host-bound processes (projects run via dotnet run).
        if (!IsDockerDesktop)
        {
            resourceBuilder.WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway");
        }

        if (apiKey is not null)
        {
            resource.SecretKey = apiKey.Resource;
            resourceBuilder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[ApiKeyEnvVar] = ReferenceExpression.Create($"{apiKey.Resource}");
                return Task.CompletedTask;
            });
        }

        if (publishableKey is not null)
        {
            resource.PublishableKey = publishableKey.Resource;
        }

        DropWaitAnnotationsWhenPublishing(builder, resource);

        return resourceBuilder;
    }

    /// <summary>
    /// Configures the Stripe CLI to listen for webhook events and forward them to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">The Stripe CLI resource type.</typeparam>
    /// <typeparam name="TTarget">The target resource type with endpoints.</typeparam>
    /// <param name="builder">The Stripe CLI resource builder.</param>
    /// <param name="forwardTo">The Aspire resource to forward webhook events to.</param>
    /// <param name="webhookPath">The path on the target resource's endpoint. Defaults to <c>/webhooks/stripe</c>.</param>
    /// <param name="events">Optional collection of specific event types to listen for. If not specified, all events are forwarded.</param>
    /// <param name="skipVerify">When <c>true</c>, passes <c>--skip-verify</c> to skip SSL certificate verification.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport("withWebhookForwardTo", Description = "Configures Stripe CLI to forward webhook events to the specified endpoint")]
    public static IResourceBuilder<T> WithWebhookForwardTo<T, TTarget>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<TTarget> forwardTo,
        string webhookPath = DefaultWebhookPath,
        IEnumerable<string>? events = null,
        bool skipVerify = false)
        where T : IResource, IStripeCliResource
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(forwardTo);

        var stripeResource = builder.Resource;
        builder.WithArgs(context =>
        {
            context.Args.Add("--forward-to");
            context.Args.Add(BuildForwardToUrl(stripeResource, forwardTo.Resource, webhookPath));
        });

        AppendListenOptions(builder, events, skipVerify);
        return builder.EnsureWebhookSigningSecretResolver();
    }

    /// <summary>
    /// Configures the Stripe CLI to listen for webhook events and forward them to multiple endpoints.
    /// Each resource in <paramref name="forwardTo"/> generates a separate <c>--forward-to</c> flag.
    /// </summary>
    /// <typeparam name="T">The Stripe CLI resource type.</typeparam>
    /// <param name="builder">The Stripe CLI resource builder.</param>
    /// <param name="webhookPath">The path on each target resource's endpoint.</param>
    /// <param name="forwardTo">One or more Aspire resources to forward webhook events to.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport("withWebhookForwardTo", Description = "Configures Stripe CLI to forward webhook events to multiple endpoints")]
    public static IResourceBuilder<T> WithWebhookForwardTo<T>(
        this IResourceBuilder<T> builder,
        string webhookPath,
        params IResourceBuilder<IResourceWithEndpoints>[] forwardTo)
        where T : IResource, IStripeCliResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(webhookPath);
        ArgumentNullException.ThrowIfNull(forwardTo);

        if (forwardTo.Length == 0)
        {
            throw new ArgumentException("At least one forward-to target must be specified.", nameof(forwardTo));
        }

        var stripeResource = builder.Resource;
        foreach (var target in forwardTo)
        {
            var capturedTarget = target;
            builder.WithArgs(context =>
            {
                context.Args.Add("--forward-to");
                context.Args.Add(BuildForwardToUrl(stripeResource, capturedTarget.Resource, webhookPath));
            });
        }

        return builder.EnsureWebhookSigningSecretResolver();
    }

    /// <summary>
    /// Configures the Stripe CLI to forward Stripe Connect webhook events to the specified endpoint
    /// using <c>--forward-connect-to</c>.
    /// </summary>
    /// <typeparam name="T">The Stripe CLI resource type.</typeparam>
    /// <typeparam name="TTarget">The target resource type with endpoints.</typeparam>
    /// <param name="builder">The Stripe CLI resource builder.</param>
    /// <param name="forwardTo">The Aspire resource to forward Connect webhook events to.</param>
    /// <param name="webhookPath">The path on the target resource's endpoint. Defaults to <c>/webhooks/stripe-connect</c>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport("withWebhookConnectForwardTo", Description = "Configures Stripe CLI to forward Connect webhook events to the specified endpoint")]
    public static IResourceBuilder<T> WithWebhookConnectForwardTo<T, TTarget>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<TTarget> forwardTo,
        string webhookPath = DefaultConnectWebhookPath)
        where T : IResource, IStripeCliResource
        where TTarget : IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(forwardTo);

        var stripeResource = builder.Resource;
        builder.WithArgs(context =>
        {
            context.Args.Add("--forward-connect-to");
            context.Args.Add(BuildForwardToUrl(stripeResource, forwardTo.Resource, webhookPath));
        });

        return builder.EnsureWebhookSigningSecretResolver();
    }

    /// <summary>
    /// Configures the Stripe CLI to forward Stripe Connect webhook events to multiple endpoints.
    /// Each resource in <paramref name="forwardTo"/> generates a separate <c>--forward-connect-to</c> flag.
    /// </summary>
    /// <typeparam name="T">The Stripe CLI resource type.</typeparam>
    /// <param name="builder">The Stripe CLI resource builder.</param>
    /// <param name="webhookPath">The path on each target resource's endpoint.</param>
    /// <param name="forwardTo">One or more Aspire resources to forward Connect webhook events to.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    [AspireExport("withWebhookConnectForwardTo", Description = "Configures Stripe CLI to forward Connect webhook events to multiple endpoints")]
    public static IResourceBuilder<T> WithWebhookConnectForwardTo<T>(
        this IResourceBuilder<T> builder,
        string webhookPath,
        params IResourceBuilder<IResourceWithEndpoints>[] forwardTo)
        where T : IResource, IStripeCliResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(webhookPath);
        ArgumentNullException.ThrowIfNull(forwardTo);

        if (forwardTo.Length == 0)
        {
            throw new ArgumentException("At least one forward-connect-to target must be specified.", nameof(forwardTo));
        }

        var stripeResource = builder.Resource;
        foreach (var target in forwardTo)
        {
            var capturedTarget = target;
            builder.WithArgs(context =>
            {
                context.Args.Add("--forward-connect-to");
                context.Args.Add(BuildForwardToUrl(stripeResource, capturedTarget.Resource, webhookPath));
            });
        }

        return builder.EnsureWebhookSigningSecretResolver();
    }

    private const string DefaultClientName = "Default";

    /// <summary>
    /// Adds a reference to a Stripe CLI resource, injecting Stripe credentials as environment
    /// variables into the destination resource.
    /// <para>
    /// Injects standalone environment variables:
    /// <list type="bullet">
    ///   <item><description><c>STRIPE_SECRET_KEY</c> — the Stripe secret API key (if provided)</description></item>
    ///   <item><description><c>STRIPE_PUBLISHABLE_KEY</c> — the Stripe publishable key (if provided via <c>publishableKey</c> parameter)</description></item>
    ///   <item><description><c>STRIPE_WEBHOOK_SECRET</c> — the webhook signing secret captured from the CLI output at startup</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Also injects variables in the format expected by <c>Stripe.Extensions.DependencyInjection</c>
    /// (i.e., the <c>Stripe:{clientName}</c> configuration section via double-underscore env var syntax),
    /// so that <c>services.AddStripe()</c> requires no additional configuration:
    /// <list type="bullet">
    ///   <item><description><c>Stripe__{clientName}__ApiKey</c></description></item>
    ///   <item><description><c>Stripe__{clientName}__PublicKey</c> (if publishable key is provided)</description></item>
    ///   <item><description><c>Stripe__{clientName}__WebhookSecret</c></description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <typeparam name="TDestination">The destination resource type.</typeparam>
    /// <typeparam name="TStripe">The Stripe CLI resource type.</typeparam>
    /// <param name="builder">The destination resource builder.</param>
    /// <param name="source">The Stripe CLI resource to reference.</param>
    /// <param name="clientName">
    /// The Stripe client name used as the configuration section key (e.g. <c>Stripe:{clientName}</c>).
    /// Defaults to <c>"Default"</c>, matching the default used by <c>services.AddStripe()</c>.
    /// </param>
    /// <returns>A reference to the <see cref="IResourceBuilder{TDestination}"/>.</returns>
    [AspireExport("withReference", Description = "Injects Stripe credentials and webhook secret into a dependent service")]
    public static IResourceBuilder<TDestination> WithReference<TDestination, TStripe>(
        this IResourceBuilder<TDestination> builder,
        IResourceBuilder<TStripe> source,
        string clientName = DefaultClientName)
        where TDestination : IResourceWithEnvironment
        where TStripe : IResource, IStripeCliResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(clientName);

        if (source.Resource.SecretKey is { } secretKey)
        {
            // Standalone env var
            builder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[SecretKeyEnvVar] = ReferenceExpression.Create($"{secretKey}");
                return Task.CompletedTask;
            });

            // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:ApiKey
            builder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[$"Stripe__{clientName}__ApiKey"] = ReferenceExpression.Create($"{secretKey}");
                return Task.CompletedTask;
            });
        }

        if (source.Resource.PublishableKey is { } publishableKey)
        {
            // Standalone env var
            builder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[PublishableKeyEnvVar] = ReferenceExpression.Create($"{publishableKey}");
                return Task.CompletedTask;
            });

            // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:PublicKey
            builder.WithEnvironment(context =>
            {
                context.EnvironmentVariables[$"Stripe__{clientName}__PublicKey"] = ReferenceExpression.Create($"{publishableKey}");
                return Task.CompletedTask;
            });
        }

        // Built once, outside the callbacks: the reference defers resolution to the value provider,
        // so a single expression stays correct no matter when the environment is materialized.
        var webhookSecretReference = ReferenceExpression.Create($"{new WebhookSecretReference(source.Resource)}");

        builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables[DefaultWebhookSecretEnvVar] = webhookSecretReference;
            return Task.CompletedTask;
        });

        // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:WebhookSecret
        return builder.WithEnvironment(context =>
        {
            context.EnvironmentVariables[$"Stripe__{clientName}__WebhookSecret"] = webhookSecretReference;
            return Task.CompletedTask;
        });
    }

    private static void AppendListenOptions<T>(
        IResourceBuilder<T> builder,
        IEnumerable<string>? events,
        bool skipVerify)
        where T : IResource, IStripeCliResource
    {
        if (events is not null)
        {
            var eventList = string.Join(",", events);
            if (!string.IsNullOrWhiteSpace(eventList))
            {
                builder.WithArgs("--events", eventList);
            }
        }

        if (skipVerify)
        {
            builder.WithArgs("--skip-verify");
        }
    }

    private static string BuildForwardToUrl(IResource stripeResource, IResourceWithEndpoints targetResource, string webhookPath)
    {
        if (!targetResource.TryGetEndpoints(out var endpoints) || !endpoints.Any())
        {
            throw new InvalidOperationException(
                $"The resource '{targetResource.Name}' does not have any endpoints defined. " +
                "Ensure the resource has at least one endpoint configured before calling WithWebhookForwardTo.");
        }

        var allocatedEndpoint = endpoints.First().AllocatedEndpoint
            ?? throw new InvalidOperationException(
                $"The endpoint for resource '{targetResource.Name}' has not been allocated yet. " +
                "Endpoint allocation occurs when the Aspire application host starts.");

        var path = webhookPath.StartsWith('/') ? webhookPath : $"/{webhookPath}";

        // When the Stripe CLI runs as a Docker container and the target is a host-bound process
        // (not a container), 'localhost' inside the container refers to the container's own
        // loopback rather than the host machine. We must rewrite the host to reach the host machine:
        //   - Docker Desktop (Windows/macOS): use host.docker.internal (resolved automatically)
        //   - Linux: use host.docker.internal too — we add --add-host=host.docker.internal:host-gateway
        //            to the container args in AddStripeCliContainer for Linux hosts
        if (stripeResource is StripeCliContainerResource && targetResource is not ContainerResource)
        {
            var isLocalhost = allocatedEndpoint.Address.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || allocatedEndpoint.Address.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

            if (isLocalhost)
            {
                return $"{allocatedEndpoint.UriScheme}://host.docker.internal:{allocatedEndpoint.Port}{path}";
            }
        }

        return $"{allocatedEndpoint.UriString}{path}";
    }

    private static IResourceBuilder<T> EnsureWebhookSigningSecretResolver<T>(this IResourceBuilder<T> builder)
        where T : IResource, IStripeCliResource
    {
        if (builder.Resource.Annotations.OfType<StripeSecretResolverAnnotation>().Any())
        {
            return builder;
        }

        builder.Resource.Annotations.Add(new StripeSecretResolverAnnotation());

        // Register a health check that becomes healthy once the signing secret is captured from CLI output.
        // This allows dependent resources to use WaitFor(stripeCli) to delay their start
        // until the STRIPE_WEBHOOK_SECRET value is available.
        var healthCheckKey = $"stripe.cli.webhook-secret.{builder.Resource.Name}";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddCheck(healthCheckKey, new StripeSigningSecretHealthCheck<T>(builder.Resource));
        builder.WithHealthCheck(healthCheckKey);

        builder.OnBeforeResourceStarted(async (resource, @event, ct) =>
        {
            var notificationService = @event.Services.GetRequiredService<ResourceNotificationService>();
            var loggerService = @event.Services.GetRequiredService<ResourceLoggerService>();

            // Instance-id discovery is awaited so the log watcher is attached before the CLI produces
            // the line containing the signing secret. It is bounded by a timeout because this runs on
            // the startup path: ResourceNotificationService.WatchAsync is documented only as "watch for
            // changes to the state for all resources", with no guarantee that it replays state that was
            // already current. Without the bound, a non-replaying watch would block BeforeResourceStarted
            // forever — a hang is not an exception, so the catch handlers below would never see it.
            //
            // On timeout the discovery task is deliberately left running rather than cancelled: it still
            // attaches the log watcher when the instance appears. That degrades the failure mode from
            // "resource never starts" to "the secret may be captured slightly later", which WaitFor on
            // the health check already handles.
            var discovery = WatchForInstanceAsync(resource, notificationService, loggerService, ct);

            if (!await CompletedWithinTimeoutAsync(discovery, InstanceDiscoveryTimeout, ct).ConfigureAwait(false)
                && !ct.IsCancellationRequested)
            {
                LogInstanceDiscoveryTimeout(resource, loggerService);
            }
        });

        return builder;
    }

    /// <summary>
    /// Waits for <paramref name="work"/> to finish, giving up after <paramref name="timeout"/>.
    /// </summary>
    /// <returns><c>true</c> if the work completed in time; <c>false</c> if the timeout elapsed first.</returns>
    /// <remarks>
    /// On timeout the work task is intentionally <em>not</em> cancelled. The caller uses this to stop
    /// blocking startup while allowing the operation to finish in the background.
    /// </remarks>
    internal static async Task<bool> CompletedWithinTimeoutAsync(Task work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The delay is cancelled once the race is decided so a pending timer does not outlive this call.
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var delay = Task.Delay(timeout, timeoutCancellation.Token);
        var winner = await Task.WhenAny(work, delay).ConfigureAwait(false);

        timeoutCancellation.Cancel();

        return winner == work;
    }

    /// <summary>
    /// Maximum time to wait for the Stripe CLI resource instance to be reported before allowing startup
    /// to continue. See the call site for why this bound exists.
    /// </summary>
    private static readonly TimeSpan InstanceDiscoveryTimeout = TimeSpan.FromSeconds(10);

    private static async Task WatchForInstanceAsync<T>(
        T resource,
        ResourceNotificationService notificationService,
        ResourceLoggerService loggerService,
        CancellationToken cancellationToken)
        where T : IResource, IStripeCliResource
    {
        try
        {
            await foreach (var resourceEvent in notificationService.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!string.Equals(resource.Name, resourceEvent.Resource.Name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _ = WatchResourceLogsAsync(resource, resourceEvent.ResourceId, loggerService, cancellationToken);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            LogResolverFailure(resource, loggerService, ex);
        }
    }

    private static async Task WatchResourceLogsAsync<T>(
        T resource,
        string resourceId,
        ResourceLoggerService loggerService,
        CancellationToken cancellationToken)
        where T : IResource, IStripeCliResource
    {
        try
        {
            await foreach (var logBatch in loggerService.WatchAsync(resourceId).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                foreach (var line in logBatch.Where(l => !string.IsNullOrWhiteSpace(l.Content)))
                {
                    if (TryExtractSigningSecret(line.Content, out var signingSecret))
                    {
                        resource.SetWebhookSigningSecret(signingSecret);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            LogResolverFailure(resource, loggerService, ex);
        }
    }

    /// <summary>
    /// Surfaces a resolver failure on the resource's own log stream.
    /// </summary>
    /// <remarks>
    /// Without this, a fault leaves the signing secret permanently uncaptured: the health check never
    /// reports healthy and every <c>WaitFor</c> on this resource waits forever with no diagnostic.
    /// </remarks>
    private static void LogResolverFailure(IResource resource, ResourceLoggerService loggerService, Exception exception)
    {
        try
        {
            loggerService.GetLogger(resource).LogError(
                exception,
                "Failed to capture the Stripe webhook signing secret from the CLI output. " +
                "Resources waiting on '{ResourceName}' will not start.",
                resource.Name);
        }
        catch (Exception loggingFailure)
        {
            Debug.WriteLine(loggingFailure);
        }
    }

    private static void LogInstanceDiscoveryTimeout(IResource resource, ResourceLoggerService loggerService)
    {
        try
        {
            loggerService.GetLogger(resource).LogWarning(
                "Timed out waiting for the '{ResourceName}' instance to be reported before startup. " +
                "Continuing without blocking; the webhook signing secret will still be captured once the " +
                "instance appears, but it may arrive later than usual.",
                resource.Name);
        }
        catch (Exception loggingFailure)
        {
            Debug.WriteLine(loggingFailure);
        }
    }

    /// <summary>
    /// Removes <see cref="WaitAnnotation"/>s that target the Stripe CLI resource when the app host runs
    /// in publish mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Stripe CLI is a local development tool, so both <c>AddStripeCli</c> and
    /// <c>AddStripeCliContainer</c> call <c>ExcludeFromManifest()</c> and the resource is therefore absent
    /// from published artifacts. The documented usage pattern also calls <c>WaitFor(stripeCli)</c> so the
    /// dependent service starts only after the signing secret has been scraped from CLI output.
    /// </para>
    /// <para>
    /// Those two facts conflict during publish: the wait relationship is emitted as a dependency on a
    /// resource that was excluded, which produces an unusable artifact. With the Docker Compose publisher
    /// the result is <c>service "x" depends on undefined service "stripe-cli"</c> and
    /// <c>docker compose config</c> rejects the project outright.
    /// </para>
    /// <para>
    /// Dropping the annotations in publish mode keeps <c>WaitFor</c> fully functional during <c>run</c>,
    /// where it is load bearing, while ensuring published output does not declare a dependency on a
    /// resource that was never emitted.
    /// </para>
    /// <para>
    /// Environment variable injection from <c>WithReference</c> is deliberately left intact, because the
    /// deployed application still needs a webhook signing secret — just not one scraped from a CLI that
    /// does not run in production. The variables are emitted as the manifest expression
    /// <c>{stripe-cli.webhookSigningSecret}</c> rather than a value, so no credential is written to the
    /// artifact.
    /// </para>
    /// <para>
    /// How that expression is rendered is publisher-specific and only verified here for the Docker
    /// Compose publisher (Aspire 13.5.3), which turns it into a <c>${STRIPE_CLI_WEBHOOKSIGNINGSECRET}</c>
    /// reference plus a blank entry in <c>.env</c> for the operator to fill in;
    /// <c>docker compose config</c> accepts the result. This is unlike the <c>depends_on</c> case above,
    /// which named a service that had to exist in the same file. Other publishers have not been tested.
    /// </para>
    /// </remarks>
    private static void DropWaitAnnotationsWhenPublishing(IDistributedApplicationBuilder builder, IResource resource)
    {
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return;
        }

        builder.Eventing.Subscribe<BeforeStartEvent>((@event, _) =>
        {
            foreach (var candidate in @event.Model.Resources)
            {
                if (ReferenceEquals(candidate, resource) ||
                    !candidate.TryGetAnnotationsOfType<WaitAnnotation>(out var waitAnnotations))
                {
                    continue;
                }

                foreach (var waitAnnotation in waitAnnotations.Where(w => ReferenceEquals(w.Resource, resource)).ToList())
                {
                    candidate.Annotations.Remove(waitAnnotation);
                }
            }

            return Task.CompletedTask;
        });
    }

    internal static bool TryExtractSigningSecret(string? content, [NotNullWhen(true)] out string? secret)
    {
        secret = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var span = content.AsSpan();
        var startIndex = span.IndexOf("whsec_", StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return false;
        }

        const int PrefixLength = 6; // "whsec_".Length
        var endIndex = startIndex + PrefixLength;
        while (endIndex < span.Length && IsSecretCharacter(span[endIndex]))
        {
            endIndex++;
        }

        // The scan above stops at the first character that cannot be part of a secret, so trailing
        // punctuation is already excluded and the candidate needs no further trimming.
        var candidate = span.Slice(startIndex, endIndex - startIndex);

        if (candidate.Length <= PrefixLength)
        {
            return false;
        }

        secret = candidate.ToString();
        return true;

        static bool IsSecretCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';
    }

    private sealed class StripeSigningSecretHealthCheck<T>(T resource) : IHealthCheck
        where T : IStripeCliResource
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(resource.WebhookSigningSecret is not null
                ? HealthCheckResult.Healthy("Webhook signing secret is available.")
                : HealthCheckResult.Unhealthy("Waiting for webhook signing secret from Stripe CLI."));
    }

    internal static void SetWebhookSigningSecret(this IStripeCliResource resource, string signingSecret)
    {
        switch (resource)
        {
            case StripeCliResource localResource:
                localResource.WebhookSigningSecret = signingSecret;
                break;
            case StripeCliContainerResource containerResource:
                containerResource.WebhookSigningSecret = signingSecret;
                break;
            default:
                // Failing loudly matters here: silently discarding the secret would leave the health
                // check permanently unhealthy and any WaitFor on this resource hanging with no diagnostic.
                throw new NotSupportedException(
                    $"Cannot set the webhook signing secret on unsupported {nameof(IStripeCliResource)} " +
                    $"implementation '{resource.GetType().Name}'. Supported types are " +
                    $"{nameof(StripeCliResource)} and {nameof(StripeCliContainerResource)}.");
        }
    }

    /// <summary>
    /// Defers resolution of the webhook signing secret until the environment is materialized.
    /// </summary>
    /// <remarks>
    /// The secret is scraped from Stripe CLI stdout after the process starts, so it does not exist
    /// when the app host is built. <see cref="ValueExpression"/> deliberately returns a value-free
    /// placeholder: it is the manifest-facing template, and the live credential must never be
    /// serialized into it.
    /// </remarks>
    private sealed class WebhookSecretReference(IStripeCliResource resource) : IValueProvider, IManifestExpressionProvider
    {
        public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
            => new(resource.WebhookSigningSecret);

        public string ValueExpression => $"{{{resource.Name}.webhookSigningSecret}}";
    }
}
