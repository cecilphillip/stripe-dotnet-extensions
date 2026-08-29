using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Stripe.Hosting.Aspire.Tests;

/// <summary>
/// Helpers for driving Aspire environment-variable materialization in tests.
/// </summary>
/// <remarks>
/// Materialization and resolution are deliberately kept as separate steps. Deferred injection can
/// only be distinguished from eager capture by materializing the environment <em>once</em>, mutating
/// the source value, and then resolving the already-materialized dictionary. Re-invoking the
/// environment callbacks after the mutation yields the correct value for both implementations and
/// therefore proves nothing.
/// </remarks>
internal static class AspireEnv
{
    /// <summary>
    /// Invokes every <see cref="EnvironmentCallbackAnnotation"/> on <paramref name="resource"/> once
    /// and returns the raw (unresolved) environment dictionary.
    /// </summary>
    public static async Task<Dictionary<string, object>> MaterializeAsync(
        IResource resource,
        DistributedApplicationOperation operation = DistributedApplicationOperation.Run)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(operation),
            resource,
            new Dictionary<string, object>());

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return new Dictionary<string, object>(context.EnvironmentVariables);
    }

    /// <summary>
    /// Resolves a materialized environment value, awaiting <see cref="IValueProvider"/> entries.
    /// </summary>
    public static async Task<string?> ResolveAsync(object value) => value switch
    {
        string s => s,
        IValueProvider provider => await provider.GetValueAsync(default),
        _ => value?.ToString()
    };

    /// <summary>
    /// Returns the manifest expression for a materialized environment value, or <see langword="null"/>
    /// when the value does not participate in manifest generation.
    /// </summary>
    public static string? ManifestExpressionOf(object value) =>
        value is IManifestExpressionProvider provider ? provider.ValueExpression : null;
}
