using System.Text.Json;

namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Verifies at startup that the host can run Stripe.net's serializer.
/// </summary>
/// <remarks>
/// Stripe.net deserializes with reflection and ships no trimming or AOT annotations. When an app
/// disables reflection-based serialization — which <c>PublishAot</c> does by default —
/// <c>Stripe.EventUtility.DeserializeEvent</c> throws
/// <c>"Reflection-based serialization has been disabled for this application."</c> on the first
/// webhook.
/// <para>
/// Without this guard that surfaces as a <c>400</c>, because parse failures are bad input and must
/// map to <c>400</c> so Stripe stops retrying. A misconfigured host is indistinguishable from a
/// signature mismatch, which sends people hunting for the wrong bug. Failing when the endpoint is
/// mapped turns it into an unmissable startup error instead.
/// </para>
/// </remarks>
internal static class StripeSerializationGuard
{
    /// <summary>Throws if reflection-based serialization is unavailable.</summary>
    public static void EnsureReflectionSerializationEnabled()
        => EnsureReflectionSerializationEnabled(JsonSerializer.IsReflectionEnabledByDefault);

    /// <param name="reflectionEnabled">
    /// Normally <see cref="JsonSerializer.IsReflectionEnabledByDefault"/>. Taken as a parameter so
    /// the failure can be tested without disabling the feature switch for the whole test process.
    /// </param>
    /// <inheritdoc cref="EnsureReflectionSerializationEnabled()"/>
    public static void EnsureReflectionSerializationEnabled(bool reflectionEnabled)
    {
        if (reflectionEnabled)
        {
            return;
        }

        throw new InvalidOperationException(
            "Reflection-based JSON serialization is disabled in this application, and Stripe.net " +
            "requires it to deserialize events. This is the default under Native AOT " +
            "(PublishAot), and can also be set explicitly via the " +
            "System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault feature switch or the " +
            "JsonSerializerIsReflectionEnabledByDefault MSBuild property. Stripe webhook " +
            "endpoints cannot work in this configuration. Publish without PublishAot, or use " +
            "PublishTrimmed instead.");
    }
}
