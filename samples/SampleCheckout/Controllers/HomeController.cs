using Microsoft.AspNetCore.Mvc;
using SampleCheckout.Models;
using Stripe;

namespace SampleCheckout.Controllers;

public class HomeController : Controller
{
    private readonly StripeClient _stripeClient;

    public HomeController([FromKeyedServices("ProductsReadOnly")]StripeClient stripeClient)
    {
        _stripeClient = stripeClient;
    }

    public async Task<IActionResult> Index()
    {
        var products = await _stripeClient.V1.Products.ListAsync(new()
        {
            Limit = 10,
            Expand = ["data.default_price"]
        });

        if (products.Data == null || !products.Data.Any())
            return View();

        var slimProducts = products.Data
            .Where(p => p.DefaultPrice is { Recurring: null })
            .Select(p => new SlimProduct(
                p.Id,
                p.Name,
                p.Description,
                (p.DefaultPrice?.UnitAmount / 100m) ?? 0m
            ));
        
        return View(slimProducts);
    }

    public IActionResult Privacy()
    {
        return View();
    }
}