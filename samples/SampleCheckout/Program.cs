using SampleCheckout.WebhookHandlers;
using Stripe.Extensions.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddStripe();
builder.Services.AddStripe("ProductsReadOnly");


var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.MapStripeWebhookHandler<MyWebhookHandler>();
app.MapDefaultControllerRoute();
app.Run();