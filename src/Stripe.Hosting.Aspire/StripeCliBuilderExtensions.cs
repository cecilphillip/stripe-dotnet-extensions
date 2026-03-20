using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stripe.Hosting.Aspire;
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
                context.EnvironmentVariables[ApiKeyEnvVar] = ReferenceExpression.Create($"{apiKey.Resource}"));
        }

        if (publishableKey is not null)
        {
            resource.PublishableKey = publishableKey.Resource;
        }

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
                context.EnvironmentVariables[ApiKeyEnvVar] = ReferenceExpression.Create($"{apiKey.Resource}"));
        }

        if (publishableKey is not null)
        {
            resource.PublishableKey = publishableKey.Resource;
        }

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

        return builder;
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

        return builder;
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
                context.EnvironmentVariables[SecretKeyEnvVar] = ReferenceExpression.Create($"{secretKey}"));

            // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:ApiKey
            builder.WithEnvironment(context =>
                context.EnvironmentVariables[$"Stripe__{clientName}__ApiKey"] = ReferenceExpression.Create($"{secretKey}"));
        }

        if (source.Resource.PublishableKey is { } publishableKey)
        {
            // Standalone env var
            builder.WithEnvironment(context =>
                context.EnvironmentVariables[PublishableKeyEnvVar] = ReferenceExpression.Create($"{publishableKey}"));

            // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:PublicKey
            builder.WithEnvironment(context =>
                context.EnvironmentVariables[$"Stripe__{clientName}__PublicKey"] = ReferenceExpression.Create($"{publishableKey}"));
        }

        // Webhook signing secret — always injected (value is empty string until CLI starts)
        builder.WithEnvironment(context =>
            context.EnvironmentVariables[DefaultWebhookSecretEnvVar] = source.Resource.WebhookSigningSecret ?? string.Empty);

        // Stripe.Extensions.DependencyInjection config section format: Stripe:{clientName}:WebhookSecret
        return builder.WithEnvironment(context =>
            context.EnvironmentVariables[$"Stripe__{clientName}__WebhookSecret"] = source.Resource.WebhookSigningSecret ?? string.Empty);
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

    // Sentinel annotation to ensure the log watcher is registered at most once per resource.
    private sealed class StripeSecretResolverAnnotation : IResourceAnnotation { }

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

        builder.OnBeforeResourceStarted((resource, @event, ct) =>
        {
            return Task.Run(async () =>
            {
                var notificationService = @event.Services.GetRequiredService<ResourceNotificationService>();
                var loggerService = @event.Services.GetRequiredService<ResourceLoggerService>();

                await foreach (var resourceEvent in notificationService.WatchAsync(ct).ConfigureAwait(false))
                {
                    if (!string.Equals(resource.Name, resourceEvent.Resource.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _ = WatchResourceLogsAsync(resource, resourceEvent.ResourceId, loggerService, ct);
                    break;
                }
            }, ct);
        });

        return builder;
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
                        if (resource is StripeCliResource localResource)
                        {
                            localResource.WebhookSigningSecret = signingSecret;
                        }
                        else if (resource is StripeCliContainerResource containerResource)
                        {
                            containerResource.WebhookSigningSecret = signingSecret;
                        }
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
    }

    private static bool TryExtractSigningSecret(string? content, out string? secret)
    {
        secret = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        const string Prefix = "whsec_";
        var startIndex = content.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return false;
        }

        var endIndex = startIndex + Prefix.Length;
        while (endIndex < content.Length && IsSecretCharacter(content[endIndex]))
        {
            endIndex++;
        }

        var candidate = content[startIndex..endIndex].TrimEnd('.', ';', ',', ')', '"');

        if (candidate.Length <= Prefix.Length)
        {
            return false;
        }

        secret = candidate;
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
}
