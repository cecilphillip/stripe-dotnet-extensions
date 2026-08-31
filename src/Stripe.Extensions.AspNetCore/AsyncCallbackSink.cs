namespace Stripe.Extensions.AspNetCore;

/// <summary>
/// Bridges the Stripe SDK's synchronous, <see langword="void"/>-returning event dispatch to the
/// asynchronous ASP.NET Core pipeline.
/// </summary>
/// <remarks>
/// The SDK's <c>Handle</c> method and its <see cref="EventHandler{TEventArgs}"/> delegates are
/// synchronous, so an <c>async</c> callback attached directly becomes <c>async void</c>: its work
/// is neither awaited nor observable, and the HTTP response would be sent before it completed.
/// <para>
/// Instead, each adapter <em>starts</em> the work synchronously and records the resulting
/// <see cref="Task"/>. After <c>Handle</c> returns, <see cref="DrainAsync"/> awaits everything.
/// </para>
/// <para>
/// Recording synchronous throws as faulted tasks rather than letting them propagate is deliberate:
/// the SDK rethrows callback exceptions out of <c>Handle</c>, which would otherwise be
/// indistinguishable from a signature or parse failure and map to the wrong status code.
/// </para>
/// </remarks>
internal sealed class AsyncCallbackSink
{
    private readonly List<Task> _pending = [];

    /// <summary>Starts <paramref name="work"/> and records it, never throwing to the caller.</summary>
    public void Run(Func<Task> work)
    {
        try
        {
            _pending.Add(work());
        }
        catch (Exception ex)
        {
            _pending.Add(Task.FromException(ex));
        }
    }

    /// <summary>
    /// Awaits all recorded work. If more than one callback failed, the thrown
    /// <see cref="AggregateException"/> preserves every failure — awaiting
    /// <see cref="Task.WhenAll(Task[])"/> alone would surface only the first.
    /// </summary>
    public async Task DrainAsync()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var all = Task.WhenAll(_pending);
        try
        {
            await all.ConfigureAwait(false);
        }
        catch
        {
            throw all.Exception ?? throw new InvalidOperationException("Faulted task had no exception.");
        }
    }
}
