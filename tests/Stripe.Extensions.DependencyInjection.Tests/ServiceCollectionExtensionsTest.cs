using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Stripe.Extensions.DependencyInjection.Tests;

public class ServiceCollectionExtensionsTest
{
    [Fact]
    public void CanResolveStripeOptions()
    {
        var collection = new ServiceCollection();

        const string fakeKey = "MyKey";
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()
            {
                ["Stripe:Default:ApiKey"] = fakeKey
            }).Build());

        collection.AddStripe();

        var provider = collection.BuildServiceProvider();
        var opts = provider.GetService<IOptionsSnapshot<StripeOptions>>();

        Assert.NotNull(opts);
        var defaultOpts = opts.Get("Default");

        Assert.Equal(fakeKey, defaultOpts.ApiKey);
        Assert.Equal(defaultOpts.SecretKey, defaultOpts.ApiKey);
    }

    [Fact]
    public void CanResolveStripeClientFromServiceProvider()
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()
            {
                ["Stripe:Default:ApiKey"] = "MyKey"
            }).Build());

        collection.AddStripe();

        var provider = collection.BuildServiceProvider();
        var stripedClient = provider.GetRequiredService<StripeClient>();
        Assert.NotNull(stripedClient);

        var keyedStripeClient =
            provider.GetKeyedService<StripeClient>(StripeOptions.DefaultClientConfigurationSectionName);
        
        Assert.NotNull(keyedStripeClient);
        Assert.StrictEqual(stripedClient, keyedStripeClient);
    }

    [Fact]
    public void CanRegisterMultipleStripeClients()
    {
        const string clientOneKey = "ClientOne";
        const string otherStripe = "OtherStripe";

        var collection = new ServiceCollection();

        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()
            {
                ["Stripe:Default:ApiKey"] = "MyKey",
                [$"Stripe:{clientOneKey}:ApiKey"] = clientOneKey,
                [$"Stripe:{otherStripe}:ApiKey"] = otherStripe
            }).Build());

        collection.AddStripe("ClientOne");
        collection.AddStripe("OtherStripe");

        var provider = collection.BuildServiceProvider();

        Assert.NotNull(provider.GetKeyedService<StripeClient>(clientOneKey));
        Assert.NotNull(provider.GetKeyedService<StripeClient>(otherStripe));

        var stripedClient = provider.GetService<StripeClient>();
        Assert.Null(stripedClient);
    }

    [Fact]
    public void KeyedClientsRetrieveUniqueConfiguration()
    {
        const string clientOneKey = "ClientOne";
        const string otherStripe = "OtherStripe";

        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection().Build());

        collection.AddStripe(clientOneKey, options => options.ApiKey = clientOneKey);
        collection.AddStripe(otherStripe, options => options.ApiKey = otherStripe);

        var provider = collection.BuildServiceProvider();

        var clientOne = provider.GetKeyedService<StripeClient>(clientOneKey);
        Assert.NotNull(clientOne);
        Assert.Equal(clientOneKey, clientOne.ApiKey);

        var otherClient = provider.GetKeyedService<StripeClient>(otherStripe);
        Assert.NotNull(otherClient);
        Assert.Equal(otherStripe, otherClient.ApiKey);
    }

    [Fact]
    public void UniqueConfigurationMerge()
    {
        const string clientOneKey = "ClientOne";

        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()
            {
                { "Stripe:Default:SecretKey","MyKey" },
                { $"Stripe:{clientOneKey}:SecretKey", "one" }
            }).Build());

        collection.AddStripe(clientOneKey, opts => { opts.ApiKey = clientOneKey; });

        var provider = collection.BuildServiceProvider();

        var clientOne = provider.GetKeyedService<StripeClient>(clientOneKey);
        Assert.NotNull(clientOne);
        Assert.Equal(clientOneKey, clientOne.ApiKey);
    }

    [Fact]
    public void ThrowsInvalidOperationExceptionIfSecretKeyIsNotProvided()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection().Build());
        collection.AddStripe();

        var provider = collection.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<StripeClient>());
        Assert.StartsWith("ApiKey is required to make requests to Stripe API. ", exception.Message);
    }

    [Fact]
    public void AddStripeThrowsExceptionIfServiceCollectionNull()
    {
        ServiceCollection? collection = null;
        Assert.Throws<ArgumentNullException>(() => collection!.AddStripe());
    }

    [Fact]
    public void AddStripeThrowsExceptionIfClientNameNull()
    {
        var collection = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => collection.AddStripe(null!));
    }
}