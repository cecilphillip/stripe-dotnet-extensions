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
    private readonly List<Event> _invocations = [];

    private WebApplication BuildWebApplication(Action<StripeOptions>? configureOptions = null, ILogger? logger = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_invocations);
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
    public async Task LogsFailedEventParsing()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);
        
        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();

        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", new StringContent("{}"));
        
        Assert.Equal((HttpStatusCode)400, response.StatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log" 
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1).Id == 1)
            .MustHaveHappened();
    }

    [Fact]
    public async Task ThrowsUsefulErrorMessageIfWebhookSecretNotSet()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = null!;
        }, logger);
        
        await app.StartAsync();
        using var httpClient = app.GetTestClient();

        var resp =await httpClient.PostAsync("/stripe/webhook", BuildPayload());
        
        Assert.False(resp.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1).Id == 5).MustHaveHappened();
        
    }

    [Fact]
    public async Task LogsUnregisteredEvent()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);
        
        await app.StartAsync();
        
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", BuildPayload("customer.deleted"));

        Assert.True(response.IsSuccessStatusCode);

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Warning
                                    && l.GetArgument<EventId>(1).Id == 4).MustHaveHappened();
    }

    [Fact]
    public async Task LogsUnknownEvent()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();
        
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", BuildPayload("customer.happy"));
        
        Assert.True(response.IsSuccessStatusCode);
        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                && l.GetArgument<LogLevel>(0) == LogLevel.Warning
                                && l.GetArgument<EventId>(1).Id == 3).MustHaveHappened();
    }

    [Fact]
    public async Task LogsWhenHandlerThrows()
    {
        var logger = A.Fake<ILogger>();
        A.CallTo(() => logger.IsEnabled(A<LogLevel>._)).Returns(true);

        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        }, logger);

        await app.StartAsync();
        
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", BuildPayload("customer.updated"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        A.CallTo(logger).Where(l => l.Method.Name == "Log"
                                    && l.GetArgument<LogLevel>(0) == LogLevel.Error
                                    && l.GetArgument<EventId>(1).Id == 2).MustHaveHappened();
    }

    [Fact]
    public async Task RunsEventCallback()
    {
        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        });
        
        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", BuildPayload());
        
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(_invocations, e => e.Type == "customer.created");
    }

    [Fact]
    public async Task CanDisableApiVersionCheck()
    {
        var app =
            BuildWebApplication(configureOptions: options =>
            {
                options.ApiKey = Secret;
                options.WebhookSecret = Secret;
                options.ThrowOnWebhookApiVersionMismatch = false;
            });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        var response = await httpClient.PostAsync("/stripe/webhook", BuildPayload(apiVersion: "01-02-1234"));

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains(_invocations, e => e.Type == "customer.created");
    }

    [Fact]
    public async Task VersionCheckIsOnByDefault()
    {
        var app = BuildWebApplication(configureOptions: opts =>
        {
            opts.ApiKey = Secret;
            opts.WebhookSecret = Secret;
        });

        await app.StartAsync();
        using var httpClient = app.GetTestClient();
        await httpClient.PostAsync("/stripe/webhook", BuildPayload(apiVersion: "01-02-1234"));


        // Assert.Contains(testSink.LogEntries, e =>
        //     e.LogLevel == LogLevel.Warning &&
        //     e.Exception?.Message.Contains("API version") == true &&
        //     e.Message == "Exception occured while parsing the Stripe WebHook event payload.");
    }

    private class MockHandler : StripeWebhookHandler
    {
        private readonly List<Event> _invocations;

        public MockHandler(List<Event> invocations, StripeWebhookContext stripeWebhookContext, ILogger<MockHandler> logger) : base(
            stripeWebhookContext, logger)
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
                      "\"id\": \"evt_123\"," +
                      "\"object\": \"event\"," +
                      "\"account\": \"acct_123\"," +
                      $"\"api_version\": \"{apiVersion}\"," +
                      "\"created\": 1533204620," +
                      "\"data\": {" +
                      "\"object\": {" +
                      "\"id\": \"cus_123\"," +
                      "\"object\": \"customer\"," +
                      "\"created\": 123456789," +
                      "\"email\": \"test@stripe.com\"," +
                      "\"livemode\": false," +
                      "\"metadata\": {}" +
                      "}" +
                      "}," +
                      "\"livemode\": false," +
                      "\"pending_webhooks\": 1," +
                      "\"request\": {" +
                      "\"id\": \"req_123\"," +
                      "\"idempotency_key\": \"idempotency-key-123\"" +
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