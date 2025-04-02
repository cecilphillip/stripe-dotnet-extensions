using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace SampleCheckout.Controllers;

public class HomeController : Controller
{
    private readonly StripeClient _stripeClient;
    private readonly ILogger<HomeController> _logger;

    public HomeController(StripeClient stripeClient, ILogger<HomeController> logger)
    {
        _stripeClient = stripeClient;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }
}