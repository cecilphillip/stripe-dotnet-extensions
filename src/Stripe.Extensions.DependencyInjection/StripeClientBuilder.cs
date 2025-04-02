using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            throw new InvalidOperationException("ApiKey is required to make requests to Stripe API. " +
                                                "You can set it using Stripe:<Section>:ApiKey configuration section or " +
                                                "by passing the value to .AddStripe(\"key\") call");
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