using SampleCheckout.WebhookHandlers;
using Stripe.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddStripe();
builder.Services.AddStripe("ProductsReadOnly", opts =>
{
    opts.ApiKey = builder.Configuration.GetValue<string>("Stripe:ApiKey");
    opts.PublicKey = builder.Configuration.GetValue<string>("Stripe:PublishableKey") ?? string.Empty;
    opts.WebhookSecret =  builder.Configuration.GetValue<string>("Stripe:WebhookSecret") ?? string.Empty;
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapStripeWebhookHandler<MyWebhookHandler>();
app.MapDefaultControllerRoute();

app.Run();