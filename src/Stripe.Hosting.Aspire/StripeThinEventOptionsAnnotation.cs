namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Accumulates the thin-event listen options across repeated
/// <c>WithThinEventForwardTo</c> calls on one Stripe CLI resource.
/// </summary>
/// <remarks>
/// <c>--thin-events</c> and <c>--skip-verify</c> are session-wide flags on <c>stripe listen</c>,
/// not per-target ones, so they must be emitted exactly once no matter how many forward targets are
/// configured. <c>--thin-events</c> in particular is a Go <c>StringSlice</c> flag, meaning repeats
/// append rather than replace: emitting it per call would silently union a narrow filter with a
/// later default of <c>*</c> and widen the subscription.
/// </remarks>
internal sealed class StripeThinEventOptionsAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Explicitly requested thin event types, de-duplicated and in first-requested order. Ignored
    /// when <see cref="SubscribeToAllThinEvents"/> is <c>true</c>.
    /// </summary>
    public List<string> ThinEvents { get; } = [];

    /// <summary>
    /// True once any call has left its event filter unset, which the public API documents as
    /// meaning "all events". Because the CLI keeps a single subscription list per <c>listen</c>
    /// session, that request can only be honoured by widening the whole session to <c>*</c>.
    /// Narrowing instead would silently starve the target that asked for everything.
    /// </summary>
    public bool SubscribeToAllThinEvents { get; set; }

    /// <summary>
    /// True once any call has asked to skip TLS verification.
    /// </summary>
    public bool SkipVerify { get; set; }
}
