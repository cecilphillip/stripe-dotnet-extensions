using Aspire.Hosting;
using Xunit;

namespace Stripe.Hosting.Aspire.Tests;

/// <summary>
/// Tests for scraping the webhook signing secret out of Stripe CLI stdout.
/// </summary>
public class SigningSecretExtractionTests
{
    [Theory]
    // Representative Stripe CLI startup output.
    [InlineData("Ready! You are using Stripe API Version [2024-06-20]. Your webhook signing secret is whsec_abc123XYZ (^C to quit)", "whsec_abc123XYZ")]
    [InlineData("whsec_onlyTheSecret", "whsec_onlyTheSecret")]
    // Trailing punctuation must not be captured as part of the secret.
    [InlineData("secret is whsec_withPeriod.", "whsec_withPeriod")]
    [InlineData("secret is whsec_withComma, ok", "whsec_withComma")]
    [InlineData("secret is (whsec_withParen)", "whsec_withParen")]
    [InlineData("secret is \"whsec_withQuote\"", "whsec_withQuote")]
    [InlineData("secret is whsec_withSemicolon; ok", "whsec_withSemicolon")]
    // Hyphens and underscores are valid secret characters.
    [InlineData("whsec_with-dash_and_underscore rest", "whsec_with-dash_and_underscore")]
    public void TryExtractSigningSecret_ExtractsSecret(string line, string expected)
    {
        Assert.True(StripeCliBuilderExtensions.TryExtractSigningSecret(line, out var secret));
        Assert.Equal(expected, secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ready! No secret on this line.")]
    // A bare prefix with no body is not a usable secret.
    [InlineData("whsec_")]
    [InlineData("prefix whsec_ suffix")]
    public void TryExtractSigningSecret_ReturnsFalseWhenAbsent(string? line)
    {
        Assert.False(StripeCliBuilderExtensions.TryExtractSigningSecret(line, out var secret));
        Assert.Null(secret);
    }
}
