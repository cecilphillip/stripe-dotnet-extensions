using Stripe.Extensions.AspNetCore;
using Xunit;

namespace Stripe.Extensions.AspNetCore.Tests;

public class SerializationGuardTests
{
    [Fact]
    public void DoesNotThrowWhenReflectionSerializationIsAvailable()
    {
        StripeSerializationGuard.EnsureReflectionSerializationEnabled(reflectionEnabled: true);
    }

    [Fact]
    public void ThrowsWhenReflectionSerializationIsDisabled()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => StripeSerializationGuard.EnsureReflectionSerializationEnabled(reflectionEnabled: false));

        // The message has to name the cause, because the failure it prevents otherwise surfaces
        // as a 400 that looks like a signature mismatch.
        Assert.Contains("Stripe.net requires it", ex.Message);
        Assert.Contains("PublishAot", ex.Message);
    }

    [Fact]
    public void TheHostRunningTheseTestsSupportsReflectionSerialization()
    {
        // Guards the guard: if this ever fails, every webhook test below is testing a broken host.
        StripeSerializationGuard.EnsureReflectionSerializationEnabled();
    }
}
