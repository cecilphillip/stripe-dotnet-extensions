namespace SampleCheckout.Models;

public record SlimProduct(string Id, string Name, string Description, decimal? Price = null);
