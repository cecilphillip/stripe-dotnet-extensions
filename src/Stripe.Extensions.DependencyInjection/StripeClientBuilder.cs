using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Stripe.Extensions.DependencyInjection.StripeOptions;

namespace Stripe.Extensions.DependencyInjection;

public interface IStripeClientBuilder : IHttpClientBuilder
{
    StripeClient Build(IServiceProvider serviceProvider);
}

internal sealed class StripeClientBuilder(IHttpClientBuilder httpClientBuilder) : IStripeClientBuilder
{
    public string Name => httpClientBuilder.Name;
    public IServiceCollection Services => httpClientBuilder.Services;

    public StripeClient Build(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope(); //IOptionsSnapshot requires scope
        
        var stripeOptions = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<StripeOptions>>().Get(Name);
        
        if (string.IsNullOrEmpty(stripeOptions.ApiKey))
        {
            var configPath = $"Stripe:{Name}:ApiKey";
            var hint = Name == DefaultClientConfigurationSectionName
                ? "pass the value via AddStripe(configureOptions: opts => opts.ApiKey = ...)."
                : $"pass the value via AddStripe(\"{Name}\", opts => opts.ApiKey = ...).";
            
            throw new InvalidOperationException(
                $"Stripe API key is missing for client '{Name}'. " +
                $"Set '{configPath}' in configuration or {hint}");
        }

        var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var systemHttpClient = new SystemNetHttpClient(
            httpClient: clientFactory.CreateClient(Name),
            maxNetworkRetries: stripeOptions.MaxNetworkRetries,
            appInfo: stripeOptions.AppInfo,
            enableTelemetry: stripeOptions.EnableTelemetry);
        

        stripeOptions.HttpClient = systemHttpClient;
        return new StripeClient(stripeOptions);
    }
}