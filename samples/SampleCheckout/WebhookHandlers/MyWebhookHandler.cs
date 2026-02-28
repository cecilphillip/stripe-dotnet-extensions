using Stripe;
using Stripe.Extensions.AspNetCore;

namespace SampleCheckout.WebhookHandlers;

public class MyWebhookHandler : StripeWebhookHandler<MyWebhookHandler>
{
    public MyWebhookHandler(StripeWebhookContext context) : base(context)
    {
        
    }

    public override async Task OnCustomerCreatedAsync(Event e)
    {
        Logger.LogInformation($"Running {nameof(OnCustomerCreatedAsync)}");
        
        var customer = (e.Data.Object as Customer)!;
        await Context.Client.V1.Customers.UpdateAsync(customer.Id, new CustomerUpdateOptions()
        {
            Description = "New customer"
        });
    }
}