using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Stripe.Extensions.DependencyInjection.Tests;

public class StartupValidationTests
{
    [Theory]
    [InlineData("Default")]
    [InlineData("payments")]
    public async Task MissingApiKeyFailsHostStartupWithClientAndConfigurationPath(string name)
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddStripe(name).ValidateOnStart())
            .Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains($"client '{name}'", exception.Message);
        Assert.Contains($"Stripe:{name}:ApiKey", exception.Message);
    }

    [Fact]
    public async Task WithoutOptInHostStartsAndClientResolutionStillFails()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddStripe())
            .Build();

        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var exception = Assert.Throws<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<StripeClient>());
        Assert.Contains("Stripe:Default:ApiKey", exception.Message);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("payments")]
    public async Task PostConfigurationCanSupplyKeyBeforeStartupValidation(string name)
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddStripe(name).ValidateOnStart();
                services.PostConfigure<StripeOptions>(name, options => options.ApiKey = "post-configured");
            })
            .Build();

        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredKeyedService<StripeClient>(name);
        Assert.Equal("post-configured", client.ApiKey);
        await host.StopAsync();
    }

    [Fact]
    public async Task ValidationTargetsOnlyOptedInNamedClient()
    {
        using var host = new HostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Stripe:payments:ApiKey"] = "configured" }))
            .ConfigureServices(services =>
            {
                services.AddStripe();
                services.AddStripe("payments").ValidateOnStart();
            })
            .Build();

        await host.StartAsync();
        using var scope = host.Services.CreateScope();
        Assert.Equal("configured", scope.ServiceProvider.GetRequiredKeyedService<StripeClient>("payments").ApiKey);
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<StripeClient>());
        await host.StopAsync();
    }
}
