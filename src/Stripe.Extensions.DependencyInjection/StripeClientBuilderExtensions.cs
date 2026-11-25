using Stripe.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class StripeClientBuilderExtensions
{
    /// <summary>
    /// Validates this client's API key when the host starts, after options configuration.
    /// </summary>
    public static IStripeClientBuilder ValidateOnStart(this IStripeClientBuilder builder)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        var name = builder.Name;
        builder.Services.AddOptions<StripeOptions>(name)
            .Validate(options => !string.IsNullOrEmpty(options.ApiKey),
                $"Stripe API key is missing for client '{name}'. Set 'Stripe:{name}:ApiKey' in configuration or configure ApiKey with AddStripe.")
            .ValidateOnStart();

        return builder;
    }
}
