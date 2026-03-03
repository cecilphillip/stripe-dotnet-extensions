using System.Net;
using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe.Extensions.DependencyInjection;
using Stripe.V2.Core;
using Xunit;

namespace Stripe.Extensions.AspNetCore.Tests;

public class ThinEventHandlerTests
{
    private const string Secret = "secret_key";
    private const string WebhookPath = "/stripe/thin-event";

    private WebApplication BuildWebApplication(List<EventNotification> invocations, Action<StripeOptions>? configureOptions = null, ILogger? logger = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(invocations);
        builder.Services.AddLogging(b =>
            b.ClearProviders()
                .SetMinimumLevel(LogLevel.Information)
                .AddPassThrough(logger)
        );
        builder.Services.AddStripe(configureOptions: configureOptions);

        var app = builder.Build();
        app.MapStripeThinEventHandler<MockThinHandler>();

        return app;
    }

    [Fact]
    public async Task LogsErrorWhenEventPayloadFailsSignatureValidation()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, new StringContent("{}"));

        Assert.Equal((HttpStatusCode)400, response.StatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.EventParsingErrorId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task ThrowsUsefulErrorMessageIfWebhookSecretNotSet()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = null!;
        }, logger);

        await app.StartAsync();
        using var httpClient = app.GetTestClient();

        var resp = await httpClient.PostAsync(WebhookPath, BuildThinEventPayload());

        Assert.False(resp.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.WebhookSecretValidationFailedId).MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsWarningForEventWithNoOverriddenHandler()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildThinEventPayload("v2.core.account.created"));

        Assert.True(response.IsSuccessStatusCode);

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Warning
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.UnhandledEventId).MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsWarningForUnrecognizedEventType()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildThinEventPayload("v2.unknown.event"));

        Assert.True(response.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                && l.GetArgument<LogLevel>(0) == LogLevel.Warning
                                && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.UnknownEventId).MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsErrorWhenHandlerThrowsException()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildThinEventPayload("v2.core.account.updated"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.ExecutionErrorId).MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task RunsEventCallback()
    {
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildThinEventPayload());

        Assert.True(response.IsSuccessStatusCode);
        var pingEvent = Assert.Single(invocations, e => e.Type == "v2.core.event_destination.ping");
        Assert.NotNull(pingEvent);

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsErrorWhenSignatureHeaderIsMissing()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var payload = BuildThinEventPayload();
        payload.Headers.Remove("Stripe-Signature");
        var response = await httpClient.PostAsync(WebhookPath, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.EventParsingErrorId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsErrorWhenSignatureIsInvalid()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<EventNotification>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var payload = BuildThinEventPayload();
        payload.Headers.Remove("Stripe-Signature");
        payload.Headers.Add("Stripe-Signature", "t=123456789,v1=invalidsignature");
        var response = await httpClient.PostAsync(WebhookPath, payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.EventParsingErrorId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    private class MockThinHandler : StripeThinEventHandler<MockThinHandler>
    {
        private readonly List<EventNotification> _invocations;

        public MockThinHandler(List<EventNotification> invocations, StripeWebhookContext stripeWebhookContext) : base(
            stripeWebhookContext)
        {
            _invocations = invocations;
        }

        protected override Task ExecuteAsync(EventNotification notification)
        {
            // Throw for account.updated events to test error handling
            if (notification.Type == "v2.core.account.updated")
            {
                throw new Exception("Test exception from handler");
            }
            _invocations.Add(notification);
            return base.ExecuteAsync(notification);
        }
    }

    private static readonly UTF8Encoding SafeUtf8
        = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        var secretBytes = SafeUtf8.GetBytes(secret);
        var payloadBytes = SafeUtf8.GetBytes($"{timestamp}.{payload}");

        using (var cryptographer = new HMACSHA256(secretBytes))
        {
            var hash = cryptographer.ComputeHash(payloadBytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    private StringContent BuildThinEventPayload(string eventType = "v2.core.event_destination.ping")
    {
        var payload = "{" +
                      "\"id\": \"evt_123\"," +
                      "\"type\": \"" + eventType + "\"," +
                      "\"created_at\": \"2024-01-01T00:00:00Z\"," +
                      "\"data\": {" +
                      "\"object\": {}" +
                      "}" +
                      "}";

        var eventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = $"t={eventTimestamp},v1={ComputeSignature(Secret, eventTimestamp, payload)}";

        return new StringContent(payload)
        {
            Headers = { { "Stripe-Signature", signature } },
        };
    }
}
