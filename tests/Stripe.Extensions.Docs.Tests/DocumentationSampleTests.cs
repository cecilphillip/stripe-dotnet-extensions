namespace Stripe.Extensions.Docs.Tests;

/// <summary>
/// Compiles every C# sample in the project's documentation against the real assemblies.
/// </summary>
/// <remarks>
/// These tests exist because published documentation samples were shipped that did not compile:
/// a malformed constructor, a method with a missing return, and a unit-test sample asserting on a
/// dependency the handler never called. Every one of them would have been caught by compiling the
/// markdown, and none of them were caught by reading it. Reviewing samples by eye does not work;
/// this does.
/// </remarks>
public class DocumentationSampleTests
{
    private static readonly IReadOnlyList<MarkdownSample> Samples = MarkdownSampleLoader.Load();

    /// <summary>
    /// Type declarations from across all documentation, so a sample that registers
    /// <c>AccountProvisioningSubscriber</c> can resolve the sample that declares it.
    /// </summary>
    private static readonly IReadOnlyList<(string Name, string Source)> DeclaredTypes = BuildTypeIndex();

    public static TheoryData<MarkdownSample> AllSamples()
    {
        var data = new TheoryData<MarkdownSample>();

        foreach (var sample in Samples.Where(s => s.SkipReason is null))
        {
            data.Add(sample);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllSamples))]
    public void SampleCompiles(MarkdownSample sample)
    {
        var declaredHere = SampleCompiler.DeclaredTypeNames(sample.Body).ToHashSet(StringComparer.Ordinal);

        var context = DeclaredTypes
            .Where(t => !declaredHere.Contains(t.Name))
            .Select(t => t.Source)
            .ToList();

        var failure = SampleCompiler.TryCompile(sample, context, out var withContextErrors);

        if (failure is not null)
        {
            // Retry in isolation. The shared index only supplies helper types, so dropping it can
            // add "type not found" errors but can never hide a defect in this sample. Without the
            // retry, one broken declaration cascades into every other sample and buries the real
            // failure - which is exactly what happened while building this harness.
            var isolated = SampleCompiler.TryCompile(sample, [], out var isolatedErrors);

            if (isolated is null)
            {
                return;
            }

            if (isolatedErrors < withContextErrors)
            {
                failure = isolated;
            }
        }

        Assert.True(
            failure is null,
            $"""
             The C# sample at {sample.Display} does not compile.

             {failure}

             Fix the sample, or - if it genuinely cannot compile - opt it out in the markdown with
             a reason immediately above the fence:

                 <!-- docs-verify: skip why this cannot be compiled -->
             """);
    }

    /// <summary>
    /// Every opt-out must carry a reason, so skips stay visible and justified rather than becoming
    /// a silent way to disable this safety net.
    /// </summary>
    [Fact]
    public void SkippedSamplesDocumentWhyTheyAreSkipped()
    {
        var unexplained = Samples
            .Where(s => s.SkipReason is not null && s.SkipReason.Length == 0)
            .Select(s => s.Display)
            .ToList();

        Assert.True(unexplained.Count == 0, $"Skipped without a reason: {string.Join(", ", unexplained)}");
    }

    /// <summary>
    /// Guards the harness itself. If the loader silently stopped finding samples - a moved file, a
    /// changed fence style - every other test here would vacuously pass.
    /// </summary>
    [Fact]
    public void DocumentationContainsSamplesToVerify()
    {
        Assert.True(
            Samples.Count >= 25,
            $"Only {Samples.Count} samples found under {MarkdownSampleLoader.RepositoryRoot}; " +
            "the loader is probably not finding the documentation any more.");
    }

    /// <remarks>
    /// A declaration is only admitted if it compiles against the declarations already admitted.
    /// Otherwise a single broken sample contaminates the context of every other sample, turning one
    /// real defect into dozens of misleading failures - which is how this harness first behaved.
    /// </remarks>
    private static List<(string, string)> BuildTypeIndex()
    {
        var index = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<string>();

        foreach (var sample in Samples)
        {
            var (types, _, _) = SampleCompiler.Partition(sample.Body);

            if (string.IsNullOrWhiteSpace(types))
            {
                continue;
            }

            var probe = sample with { Body = types };

            if (SampleCompiler.TryCompile(probe, accepted) is not null)
            {
                continue;
            }

            var added = false;

            foreach (var name in SampleCompiler.DeclaredTypeNames(sample.Body))
            {
                if (seen.Add(name))
                {
                    index.Add((name, types));
                    added = true;
                }
            }

            if (added)
            {
                accepted.Add(types);
            }
        }

        return index;
    }
}
