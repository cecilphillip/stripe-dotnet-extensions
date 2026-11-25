using System.Net;
using System.Security.Cryptography;
using System.Text;
using FakeItEasy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stripe.Extensions.DependencyInjection;
using Xunit;

namespace Stripe.Extensions.AspNetCore.Tests;

public class WebhookHandlerTests
{
    private const string Secret = "secret_key";
    private const string WebhookPath = "/stripe/webhook";
    private const string TestEventId = "evt_123";
    private const string TestAccountId = "acct_123";
    private const string TestCustomerId = "cus_123";
    private const string TestRequestId = "req_123";
    private const string TestIdempotencyKey = "idempotency-key-123";
    private const long EventCreatedTimestamp = 1533204620;
    private const long CustomerCreatedTimestamp = 123456789;

    private WebApplication BuildWebApplication(List<Event> invocations, Action<StripeOptions>? configureOptions = null, ILogger? logger = null)
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
        app.MapStripeWebhookHandler<MockHandler>();

        return app;
    }

    [Fact]
    public async Task LogsErrorWhenEventPayloadFailsSignatureValidation()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<Event>();

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
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = null!;
        }, logger);

        await app.StartAsync();
        using var httpClient = app.GetTestClient();

        var resp = await httpClient.PostAsync(WebhookPath, BuildPayload());

        Assert.False(resp.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.WebhookSecretValidationFailedId
                                    && l.GetArgument<Exception>(3)!.Message.Contains("Stripe:Default:WebhookSecret"))
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task RejectsWhitespaceOnlyWebhookSecret()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = " \t\r\n";
        }, logger);

        await app.StartAsync();
        using var httpClient = app.GetTestClient();

        var response = await httpClient.PostAsync(WebhookPath, BuildPayload());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(invocations);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.WebhookSecretValidationFailedId)
            .MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task LogsWarningForEventWithNoOverriddenHandler()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildPayload("customer.deleted"));

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
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildPayload("customer.happy"));

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
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildPayload("customer.updated"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1) == StripeWebhookHandlerLogger.ExecutionErrorId).MustHaveHappened();

        await app.StopAsync();
    }

    [Fact]
    public async Task RunsEventCallback()
    {
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildPayload());

        Assert.True(response.IsSuccessStatusCode);
        var customerCreatedEvent = Assert.Single(invocations, e => e.Type == "customer.created");
        Assert.NotNull(customerCreatedEvent.Data.Object);
        Assert.False(customerCreatedEvent.Livemode);
        Assert.Equal(TestAccountId, customerCreatedEvent.Account);

        await app.StopAsync();
    }

    [Fact]
    public async Task CanDisableApiVersionCheck()
    {
        var invocations = new List<Event>();

        await using var app =
            BuildWebApplication(invocations, configureOptions: options =>
            {
                options.ApiKey = Secret;
                options.WebhookSecret = Secret;
                options.ThrowOnWebhookApiVersionMismatch = false;
            });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync(WebhookPath, BuildPayload(apiVersion: "01-02-1234"));

        Assert.True(response.IsSuccessStatusCode);
        var customerCreatedEvent = Assert.Single(invocations, e => e.Type == "customer.created");
        Assert.NotNull(customerCreatedEvent.Data.Object);
        Assert.False(customerCreatedEvent.Livemode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ParseEventThrowsWhenApiVersionMismatch()
    {
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        await httpClient.PostAsync(WebhookPath, BuildPayload(apiVersion: "01-02-1234"));

        await app.StopAsync();
    }

    [Fact]
    public async Task ReturnsErrorWhenSignatureHeaderIsMissing()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var payload = BuildPayload();
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
        var invocations = new List<Event>();

        await using var app = BuildWebApplication(invocations, configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var payload = BuildPayload();
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

    private class MockHandler : StripeWebhookHandler<MockHandler>
    {
        private readonly List<Event> _invocations;

        public MockHandler(List<Event> invocations, StripeWebhookContext stripeWebhookContext) : base(
            stripeWebhookContext)
        {
            _invocations = invocations;
        }

        public override Task OnCustomerCreatedAsync(Event e)
        {
            _invocations.Add(e);
            return Task.CompletedTask;
        }

        public override Task OnCustomerUpdatedAsync(Event e)
        {
            throw new Exception();
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

    private StringContent BuildPayload(string eventType = "customer.created", string? apiVersion = null)
    {
        apiVersion ??= StripeConfiguration.ApiVersion;

        var payload = "{" +
                      $"\"id\": \"{TestEventId}\"," +
                      "\"object\": \"event\"," +
                      $"\"account\": \"{TestAccountId}\"," +
                      $"\"api_version\": \"{apiVersion}\"," +
                      $"\"created\": {EventCreatedTimestamp}," +
                      "\"data\": {" +
                      "\"object\": {" +
                      $"\"id\": \"{TestCustomerId}\"," +
                      "\"object\": \"customer\"," +
                      $"\"created\": {CustomerCreatedTimestamp}," +
                      "\"email\": \"test@stripe.com\"," +
                      "\"livemode\": false," +
                      "\"metadata\": {}" +
                      "}" +
                      "}," +
                      "\"livemode\": false," +
                      "\"pending_webhooks\": 1," +
                      "\"request\": {" +
                      $"\"id\": \"{TestRequestId}\"," +
                      $"\"idempotency_key\": \"{TestIdempotencyKey}\"" +
                      "}," +
                      $"\"type\": \"{eventType}\"" +
                      "}";

        var eventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = $"t={eventTimestamp},v1={ComputeSignature(Secret, eventTimestamp, payload)}";

        return new StringContent(payload)
        {
            Headers = { { "Stripe-Signature", signature } },
        };
    }
}