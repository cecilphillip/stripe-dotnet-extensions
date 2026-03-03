using System.Reflection;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Extensions.DependencyInjection;
using static Stripe.Extensions.DependencyInjection.StripeOptions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class StripeServiceCollectionExtensions
{
    private static readonly AssemblyName AsmName = typeof(StripeServiceCollectionExtensions).Assembly.GetName();

    public static IStripeClientBuilder AddStripe(this IServiceCollection services,
        string clientName = DefaultClientConfigurationSectionName, Action<StripeOptions>? configureOptions = null)
    {
        if (services is null)
            throw new ArgumentNullException(nameof(services));

        if (clientName is null)
            throw new ArgumentNullException(nameof(clientName));

        // Register the named stripe client with configuration section
       services.AddOptions<StripeOptions>(clientName)
            .Configure(ConfigureStripeOptions)
            .Configure<IServiceProvider>((options, provider) =>
                BindOptionsConfiguration(clientName, options, provider))
            .PostConfigure(opts => configureOptions?.Invoke(opts));

        return services
            .RegisterClientServices(clientName);

        static void ConfigureStripeOptions(StripeOptions options)
        {
            options.AppInfo ??= new AppInfo
            {
                Name = AsmName.Name,
                Version = AsmName.Version?.ToString()
            };
        }

        static void BindOptionsConfiguration(string clientName, StripeOptions options, IServiceProvider provider)
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var configSection = configuration.GetSection($"Stripe:{clientName}");

            configSection.Bind(options);
            options.ClientName = clientName;
        }
    }

    private static IStripeClientBuilder RegisterClientServices(this IServiceCollection services, string clientName)
    {
        var httpClientBuilder = services.AddHttpClient(clientName);
        var stripeClientBuilder = new StripeClientBuilder(httpClientBuilder);

        services.AddKeyedScoped<StripeClient>(clientName,
            (serviceProvider, _) => stripeClientBuilder.Build(serviceProvider));

        if (clientName == DefaultClientConfigurationSectionName)
        {
            services.AddScoped<StripeClient>(serviceProvider =>
                serviceProvider.GetRequiredKeyedService<StripeClient>(clientName));
        }

        return stripeClientBuilder;
    }
}