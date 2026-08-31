using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe.Events;
using Stripe.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Extensions.AspNetCore.Tests;

public class EventNotificationSubscriberTests
{
    private const string Secret = "secret_key";
    private const string Path = "/stripe/thin-events";

    private sealed class Recorder
    {
        public ConcurrentQueue<string> Entries { get; } = new();
    }

    private sealed class PingSubscriberA(Recorder recorder)
        : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
    {
        public ValueTask HandleAsync(
            V2CoreEventDestinationPingEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"A:{notification.Type}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PingSubscriberB(Recorder recorder)
        : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
    {
        public ValueTask HandleAsync(
            V2CoreEventDestinationPingEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"B:{notification.Id}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriberA
        : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
    {
        public async ValueTask HandleAsync(
            V2CoreEventDestinationPingEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("failure-a");
        }
    }

    private sealed class ThrowingSubscriberB
        : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>
    {
        public ValueTask HandleAsync(
            V2CoreEventDestinationPingEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken)
            // Throws synchronously. The SDK rethrows callback exceptions out of Handle, so this
            // must be captured by the sink or it would be misreported as a parse failure (400).
            => throw new InvalidOperationException("failure-b");
    }

    private sealed class BaseTypeSubscriber : IStripeEventSubscriber<V2.Core.EventNotification>
    {
        public ValueTask HandleAsync(
            V2.Core.EventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class UnknownTypeSubscriber : IStripeEventSubscriber<UnknownEventNotification>
    {
        public ValueTask HandleAsync(
            UnknownEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class UnhandledSubscriber(Recorder recorder) : IStripeUnhandledEventSubscriber
    {
        public ValueTask HandleAsync(
            StripeUnhandledEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue(
                $"unhandled:{context.Notification.Type}:known={context.Details.IsKnownEventType}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SecondUnhandledSubscriber(Recorder recorder) : IStripeUnhandledEventSubscriber
    {
        public ValueTask HandleAsync(
            StripeUnhandledEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"second:{context.Notification.Type}");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingUnhandledSubscriber : IStripeUnhandledEventSubscriber
    {
        public ValueTask HandleAsync(
            StripeUnhandledEventNotificationContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("unhandled-boom");
    }

    /// <summary>Handles one typed event and everything nobody else claimed.</summary>
    private sealed class HybridSubscriber(Recorder recorder)
        : IStripeEventSubscriber<V2CoreEventDestinationPingEventNotification>, IStripeUnhandledEventSubscriber
    {
        public ValueTask HandleAsync(
            V2CoreEventDestinationPingEventNotification notification,
            StripeEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"typed:{notification.Type}");
            return ValueTask.CompletedTask;
        }

        public ValueTask HandleAsync(
            StripeUnhandledEventNotificationContext context,
            CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"fallback:{context.Notification.Type}");
            return ValueTask.CompletedTask;
        }
    }

    private static WebApplication BuildApp(
        Action<IServiceCollection> register,
        Action<StripeOptions>? configureOptions = null,
        ILogger? logger = null,
        Action<StripeEventNotificationOptions>? configureEndpoint = null,
        bool verifySignature = true,
        Action<IEndpointConventionBuilder>? decorate = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<Recorder>();
        builder.Services.AddLogging(b =>
            b.ClearProviders().SetMinimumLevel(LogLevel.Information).AddPassThrough(logger));
        builder.Services.AddStripe(configureOptions: configureOptions ?? (o =>
        {
            o.ApiKey = Secret;
            o.WebhookSecret = Secret;
        }));
        register(builder.Services);

        var app = builder.Build();
        var endpoint = verifySignature
            ? app.MapStripeEventNotifications(Path, configureEndpoint)
            : app.MapStripeEventNotificationsWithoutSignatureVerification(Path, configure: configureEndpoint);
        decorate?.Invoke(endpoint);

        return app;
    }

    [Fact]
    public async Task DispatchesToEverySubscriberRegisteredForTheSameNotification()
    {
        await using var app = BuildApp(services =>
        {
            services.AddStripeEventSubscriber<PingSubscriberA>();
            services.AddStripeEventSubscriber<PingSubscriberB>();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var recorder = app.Services.GetRequiredService<Recorder>();
        var entries = recorder.Entries.OrderBy(e => e).ToArray();
        Assert.Equal(["A:v2.core.event_destination.ping", "B:evt_123"], entries);

        await app.StopAsync();
    }

    [Fact]
    public async Task PreservesEverySubscriberFailureAndReturns500()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        await using var app = BuildApp(services =>
        {
            services.AddStripeEventSubscriber<ThrowingSubscriberA>();
            services.AddStripeEventSubscriber<ThrowingSubscriberB>();
        }, logger: logger);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // The fake logger receives every category, and EventId 2 is not unique across the framework,
        // so select the call by payload rather than by event id alone.
        var call = Fake.GetCalls(logger).Single(c =>
            c.Method.Name == "Log"
            && c.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.ExecutionErrorId
            && c.GetArgument<Exception>(3) is AggregateException);

        var aggregate = Assert.IsType<AggregateException>(call.GetArgument<Exception>(3));
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Contains(aggregate.InnerExceptions, e => e.Message == "failure-a");
        Assert.Contains(aggregate.InnerExceptions, e => e.Message == "failure-b");

        await app.StopAsync();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    public async Task ReturnsBadRequestForMalformedPayload(string body)
    {
        await using var app = BuildApp(services =>
            services.AddStripeEventSubscriber<PingSubscriberA>());

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, Sign(body));

        // Malformed input raises JsonReaderException, not StripeException. It must still map to 400:
        // a 500 would make Stripe retry a payload that can never succeed.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsBadRequestForV1SnapshotEventPostedToThinEndpoint()
    {
        await using var app = BuildApp(services =>
            services.AddStripeEventSubscriber<PingSubscriberA>());

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, Sign(
            """{"id":"evt_1","object":"event","type":"customer.created","data":{"object":{}}}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsBadRequestWhenSignatureIsInvalid()
    {
        await using var app = BuildApp(services =>
            services.AddStripeEventSubscriber<PingSubscriberA>());

        await app.StartAsync();
        using var client = app.GetTestClient();

        var content = new StringContent(Payload());
        content.Headers.Add("Stripe-Signature", "t=1,v1=deadbeef");

        var response = await client.PostAsync(Path, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsAndFailsWhenWebhookSecretIsMissing()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            configureOptions: o =>
            {
                o.ApiKey = Secret;
                o.WebhookSecret = null!;
            },
            logger: logger);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload());

        Assert.False(response.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<EventId>(1) ==
                                    StripeWebhookHandlerLogger.WebhookSecretValidationFailedId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsWarningWhenNoSubscriberHandlesTheNotification()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            logger: logger);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Warning
                                    && l.GetArgument<EventId>(1) ==
                                    StripeWebhookHandlerLogger.UnhandledNotificationId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task UnhandledSubscriberReplacesTheWarningLog()
    {
        var logger = A.Fake<ILogger>();
        var recorder = new Recorder();

        await using var app = BuildApp(
            services =>
            {
                services.AddSingleton(recorder);
                services.AddStripeEventSubscriber<PingSubscriberA>();
                services.AddStripeEventSubscriber<UnhandledSubscriber>();
            },
            logger: logger);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(["unhandled:v2.core.account.created:known=True"], recorder.Entries.ToArray());

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<EventId>(1) ==
                                    StripeWebhookHandlerLogger.UnhandledNotificationId)
            .MustNotHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task ShouldDispatchAsyncCanSkipDispatchWhileStillAcknowledging()
    {
        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            configureEndpoint: options => options.ShouldDispatchAsync =
                (_, _) => ValueTask.FromResult(false));

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(app.Services.GetRequiredService<Recorder>().Entries);

        await app.StopAsync();
    }

    [Fact]
    public async Task ShouldDispatchAsyncIsAwaitedBeforeSubscribersRun()
    {
        var recorder = new Recorder();
        var release = new TaskCompletionSource();

        await using var app = BuildApp(
            services =>
            {
                services.AddSingleton(recorder);
                services.AddStripeEventSubscriber<PingSubscriberA>();
            },
            configureEndpoint: options => options.ShouldDispatchAsync = async (_, _) =>
            {
                await release.Task;
                recorder.Entries.Enqueue("gate");
                return true;
            });

        await app.StartAsync();
        using var client = app.GetTestClient();

        var pending = client.PostAsync(Path, BuildPayload());

        // The gate has not resolved, so no subscriber may have observed the notification yet.
        Assert.Empty(recorder.Entries);
        release.SetResult();

        var response = await pending;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(["gate", "A:v2.core.event_destination.ping"], recorder.Entries.ToArray());

        await app.StopAsync();
    }

    [Fact]
    public async Task ShouldDispatchAsyncSkipAlsoSuppressesTheUnhandledPath()
    {
        var recorder = new Recorder();
        await using var app = BuildApp(
            services =>
            {
                services.AddSingleton(recorder);
                services.AddStripeEventSubscriber<PingSubscriberA>();
                services.AddStripeEventSubscriber<UnhandledSubscriber>();
            },
            configureEndpoint: options => options.ShouldDispatchAsync =
                (_, _) => ValueTask.FromResult(false));

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(recorder.Entries);

        await app.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldDispatchAsyncFailureIsReportedAsServerErrorNotBadRequest(bool anySubscriberMatches)
    {
        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            configureEndpoint: options => options.ShouldDispatchAsync =
                (_, _) => throw new InvalidOperationException("boom"));

        await app.StartAsync();
        using var client = app.GetTestClient();

        // The second case has no matching subscriber, so nothing awaits the gate on its behalf:
        // the endpoint must still observe the failure rather than silently returning 202.
        var payload = anySubscriberMatches ? BuildPayload() : BuildPayload("v2.core.account.created");
        var response = await client.PostAsync(Path, payload);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ResultFeatureIsVisibleToAnEndpointFilter()
    {
        var observed = new List<string>();

        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            configureEndpoint: null,
            decorate: endpoint => endpoint.AddEndpointFilter(async (context, next) =>
            {
                var response = await next(context);
                var result = context.HttpContext.Features.Get<StripeEventNotificationResult>();
                observed.Add($"{result?.EventType}:{result?.Outcome}:{result?.Exception is null}");
                return response;
            }));

        await app.StartAsync();
        using var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Path, BuildPayload())).StatusCode);

        var bad = new StringContent("not json", SafeUtf8, "application/json");
        bad.Headers.Add("Stripe-Signature", "t=1,v1=deadbeef");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Path, bad)).StatusCode);

        Assert.Equal(
            ["v2.core.event_destination.ping:Dispatched:True", ":Rejected:False"],
            observed);

        await app.StopAsync();
    }

    [Fact]
    public async Task ResultFeatureReportsSkippedAndFailedOutcomes()
    {
        var observed = new List<StripeEventNotificationOutcome?>();
        var skip = false;

        await using var app = BuildApp(
            services =>
            {
                services.AddStripeEventSubscriber<PingSubscriberA>();
                services.AddStripeEventSubscriber<ThrowingSubscriberA>();
            },
            configureEndpoint: options => options.ShouldDispatchAsync =
                (_, _) => ValueTask.FromResult(!skip),
            decorate: endpoint => endpoint.AddEndpointFilter(async (context, next) =>
            {
                var response = await next(context);
                observed.Add(context.HttpContext.Features.Get<StripeEventNotificationResult>()?.Outcome);
                return response;
            }));

        await app.StartAsync();
        using var client = app.GetTestClient();

        skip = true;
        await client.PostAsync(Path, BuildPayload());
        skip = false;
        await client.PostAsync(Path, BuildPayload());

        Assert.Equal(
            [StripeEventNotificationOutcome.Skipped, StripeEventNotificationOutcome.Failed],
            observed);

        await app.StopAsync();
    }

    [Fact]
    public async Task WithoutSignatureVerificationAcceptsEventBridgeEnvelopes()
    {
        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            verifySignature: false);

        await app.StartAsync();
        using var client = app.GetTestClient();

        // This endpoint accepts cloud-transport envelopes, not a raw Stripe webhook body.
        var response = await client.PostAsync(Path, new StringContent(
            $$"""{"version":"0","id":"1","detail-type":"v2.core.event_destination.ping","source":"aws.partner/stripe.com/x","detail":{{Payload()}}}"""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(app.Services.GetRequiredService<Recorder>().Entries);

        await app.StopAsync();
    }

    [Fact]
    public async Task WithoutSignatureVerificationAcceptsCloudEventsEnvelopes()
    {
        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            verifySignature: false);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, new StringContent(
            $$"""{"specversion":"1.0","id":"1","type":"v2.core.event_destination.ping","source":"stripe","data":{{Payload()}}}"""));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Single(app.Services.GetRequiredService<Recorder>().Entries);

        await app.StopAsync();
    }

    [Fact]
    public async Task WithoutSignatureVerificationRejectsRawStripeBodies()
    {
        await using var app = BuildApp(
            services => services.AddStripeEventSubscriber<PingSubscriberA>(),
            verifySignature: false);

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, new StringContent(Payload()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsBadRequestWhenSignatureHeaderIsMissing()
    {
        await using var app = BuildApp(services =>
            services.AddStripeEventSubscriber<PingSubscriberA>());

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, new StringContent(Payload()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public void RegisteringATypeThatIsNotASubscriberFailsFast()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            services.AddStripeEventSubscriber<Recorder>);

        Assert.Contains("IStripeEventSubscriber", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(BaseTypeSubscriber))]
    [InlineData(typeof(UnknownTypeSubscriber))]
    public void SubscribingToANonSpecificNotificationTypeFailsFast(Type subscriberType)
    {
        var services = new ServiceCollection();

        var register = typeof(StripeEventSubscriberServiceCollectionExtensions)
            .GetMethod(nameof(StripeEventSubscriberServiceCollectionExtensions.AddStripeEventSubscriber))!
            .MakeGenericMethod(subscriberType);

        var ex = Assert.IsType<InvalidOperationException>(
            Assert.Throws<TargetInvocationException>(
                () => register.Invoke(null, [services])).InnerException);

        Assert.Contains("not a specific event type", ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IStripeUnhandledEventSubscriber), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledSubscriberReceivesNotificationsNoTypedSubscriberClaimed()
    {
        var recorder = new Recorder();
        await using var app = BuildApp(services =>
        {
            services.AddSingleton(recorder);
            services.AddStripeEventSubscriber<PingSubscriberA>();
            services.AddStripeEventSubscriber<UnhandledSubscriber>();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        // Claimed by a typed subscriber: must not reach the unhandled path.
        var claimed = await client.PostAsync(Path, BuildPayload());
        Assert.Equal(HttpStatusCode.Accepted, claimed.StatusCode);
        Assert.Equal(["A:v2.core.event_destination.ping"], recorder.Entries.ToArray());

        // Known to the SDK but unclaimed.
        var unclaimed = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));
        Assert.Equal(HttpStatusCode.Accepted, unclaimed.StatusCode);
        Assert.Contains("unhandled:v2.core.account.created:known=True", recorder.Entries);

        // Not typed by this SDK version at all.
        var untyped = await client.PostAsync(Path, BuildPayload("v2.core.totally.made.up"));
        Assert.Equal(HttpStatusCode.Accepted, untyped.StatusCode);
        Assert.Contains("unhandled:v2.core.totally.made.up:known=False", recorder.Entries);

        await app.StopAsync();
    }

    [Fact]
    public async Task UnhandledSubscribersFanOut()
    {
        var recorder = new Recorder();
        await using var app = BuildApp(services =>
        {
            services.AddSingleton(recorder);
            services.AddStripeEventSubscriber<PingSubscriberA>();
            services.AddStripeEventSubscriber<UnhandledSubscriber>();
            services.AddStripeEventSubscriber<SecondUnhandledSubscriber>();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            ["second:v2.core.account.created", "unhandled:v2.core.account.created:known=True"],
            recorder.Entries.OrderBy(e => e, StringComparer.Ordinal).ToArray());

        await app.StopAsync();
    }

    [Fact]
    public async Task UnhandledSubscriberFailureReturnsInternalServerErrorWithoutBlockingPeers()
    {
        var recorder = new Recorder();
        await using var app = BuildApp(services =>
        {
            services.AddSingleton(recorder);
            services.AddStripeEventSubscriber<PingSubscriberA>();
            services.AddStripeEventSubscriber<ThrowingUnhandledSubscriber>();
            services.AddStripeEventSubscriber<UnhandledSubscriber>();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        var response = await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(["unhandled:v2.core.account.created:known=True"], recorder.Entries.ToArray());

        await app.StopAsync();
    }

    [Fact]
    public async Task OneClassCanBeBothATypedAndAnUnhandledSubscriber()
    {
        var recorder = new Recorder();
        await using var app = BuildApp(services =>
        {
            services.AddSingleton(recorder);
            services.AddStripeEventSubscriber<HybridSubscriber>();
        });

        await app.StartAsync();
        using var client = app.GetTestClient();

        await client.PostAsync(Path, BuildPayload());
        await client.PostAsync(Path, BuildPayload("v2.core.account.created"));

        Assert.Equal(
            ["typed:v2.core.event_destination.ping", "fallback:v2.core.account.created"],
            recorder.Entries.ToArray());

        await app.StopAsync();
    }

    [Fact]
    public void MappingWithOnlyAnUnhandledSubscriberIsAllowed()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<Recorder>();
        builder.Services.AddStripe(configureOptions: o => o.ApiKey = Secret);
        builder.Services.AddStripeEventSubscriber<UnhandledSubscriber>();

        var app = builder.Build();

        app.MapStripeEventNotifications(Path);
    }

    [Fact]
    public void MappingWithoutAnySubscriberFailsFast()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddStripe(configureOptions: o => o.ApiKey = Secret);

        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapStripeEventNotifications(Path));
        Assert.Contains("AddStripeEventSubscriber", ex.Message, StringComparison.Ordinal);
    }

    private static readonly UTF8Encoding SafeUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var cryptographer = new HMACSHA256(SafeUtf8.GetBytes(secret));
        var hash = cryptographer.ComputeHash(SafeUtf8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Payload(string eventType = "v2.core.event_destination.ping") =>
        "{" +
        "\"id\": \"evt_123\"," +
        "\"type\": \"" + eventType + "\"," +
        "\"created_at\": \"2024-01-01T00:00:00Z\"," +
        "\"data\": { \"object\": {} }" +
        "}";

    private static StringContent Sign(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return new StringContent(payload)
        {
            Headers = { { "Stripe-Signature", $"t={timestamp},v1={ComputeSignature(Secret, timestamp, payload)}" } },
        };
    }

    private static StringContent BuildPayload(string eventType = "v2.core.event_destination.ping")
        => Sign(Payload(eventType));
}
