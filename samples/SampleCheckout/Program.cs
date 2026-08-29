using SampleCheckout.WebhookHandlers;
using Stripe.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddStripe();
builder.Services.AddStripe("ProductsReadOnly", opts =>
{
    opts.ApiKey = builder.Configuration.GetValue<string>("Stripe:ProductsReadOnly:ApiKey")
        ?? builder.Configuration.GetValue<string>("Stripe:Default:ApiKey");
    opts.PublicKey = builder.Configuration.GetValue<string>("Stripe:ProductsReadOnly:PublicKey")
        ?? builder.Configuration.GetValue<string>("Stripe:Default:PublicKey")
        ?? string.Empty;
    opts.WebhookSecret = builder.Configuration.GetValue<string>("Stripe:ProductsReadOnly:WebhookSecret")
        ?? builder.Configuration.GetValue<string>("Stripe:Default:WebhookSecret")
        ?? string.Empty;
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapStripeWebhookHandler<MyWebhookHandler>();
app.MapDefaultControllerRoute();

app.Run();