using Microsoft.AspNetCore.Http;

namespace Stripe.Extensions.AspNetCore;

internal interface IStripeWebhookExecutor
{
    Task<IResult> ExecuteAsync();
}
