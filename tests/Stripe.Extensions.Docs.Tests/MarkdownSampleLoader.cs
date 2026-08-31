using System.Text;
using System.Text.RegularExpressions;

// The enclosing namespace starts with "Stripe", which puts Stripe.File ahead of System.IO.File
// during lookup.
using IoFile = System.IO.File;

namespace Stripe.Extensions.Docs.Tests;

/// <summary>
/// A single fenced <c>csharp</c> block lifted out of a markdown document.
/// </summary>
public sealed record MarkdownSample(string File, int Line, string Code, string? SkipReason)
{
    /// <summary>Using directives hoisted out of <see cref="Code"/>.</summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>The block with its using directives removed.</summary>
    public string Body { get; init; } = string.Empty;

    public string Display => $"{File}:{Line}";

    public override string ToString() => Display;
}

/// <summary>
/// Finds every C# sample in the repository's markdown so the tests can compile them.
/// </summary>
/// <remarks>
/// A block is skipped only when the markdown explicitly opts out with an HTML comment
/// immediately above the fence, which stays invisible in rendered markdown:
/// <code>&lt;!-- docs-verify: skip reason goes here --&gt;</code>
/// Requiring a reason keeps the opt-out honest - a bare skip is not possible.
/// </remarks>
public static class MarkdownSampleLoader
{
    private static readonly Regex FenceRegex = new(
        @"(?:<!--\s*docs-verify:\s*skip\s+(?<reason>[^>]*?)\s*-->\s*\r?\n)?```csharp\r?\n(?<code>.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+[^;()]+;\s*(//.*)?$",
        RegexOptions.Compiled);

    /// <summary>Markdown files whose samples are contractual and must compile.</summary>
    public static IReadOnlyList<string> DocumentedFiles { get; } =
    [
        "README.md",
        "CONTRIBUTING.md",
        "RELEASING.md",
        Path.Combine("samples", "SampleEventNotifications", "README.md"),
        Path.Combine("samples", "SampleCheckout", "README.md"),
        Path.Combine("samples", "SampleCheckout.AppHost", "README.md"),
    ];

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// Extra markdown files to verify, supplied as a path list in DOCS_VERIFY_EXTRA_FILES.
    /// Used by `just verify-notes` to compile release notes, which are published to GitHub and
    /// therefore live outside the repository. Release notes containing hand-written samples were
    /// the original defect this project exists to prevent.
    /// </summary>
    private static IEnumerable<string> ExtraFiles() =>
        (Environment.GetEnvironmentVariable("DOCS_VERIFY_EXTRA_FILES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<MarkdownSample> Load()
    {
        var samples = new List<MarkdownSample>();

        foreach (var relative in DocumentedFiles.Concat(ExtraFiles()))
        {
            var path = Path.Combine(RepositoryRoot, relative);
            if (!IoFile.Exists(path))
            {
                continue;
            }

            var text = IoFile.ReadAllText(path);

            foreach (Match match in FenceRegex.Matches(text))
            {
                var code = match.Groups["code"].Value;
                var reason = match.Groups["reason"].Success
                    ? match.Groups["reason"].Value.Trim()
                    : null;

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;

                samples.Add(Split(new MarkdownSample(relative, line, code, reason)));
            }
        }

        return samples;
    }

    /// <summary>
    /// Separates using directives from the rest of the block. Usings have to be hoisted above
    /// the harness types, otherwise every sample that declares its own usings would fail with
    /// CS1529 for reasons that have nothing to do with the sample being wrong.
    /// </summary>
    private static MarkdownSample Split(MarkdownSample sample)
    {
        var usings = new List<string>();
        var body = new StringBuilder();

        foreach (var line in sample.Code.Split('\n'))
        {
            // A fence nested in a blockquote arrives with the quote markers still attached.
            var text = line.StartsWith("> ", StringComparison.Ordinal) ? line[2..]
                : line.StartsWith('>') ? line[1..]
                : line;

            if (UsingRegex.IsMatch(text) && !text.Contains('='))
            {
                var directive = text.Trim();
                var comment = directive.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    directive = directive[..comment].Trim();
                }

                usings.Add(directive);
            }
            else
            {
                body.Append(text).Append('\n');
            }
        }

        return sample with { Usings = usings, Body = body.ToString().Trim('\n') };
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (IoFile.Exists(Path.Combine(dir.FullName, "Stripe.Extensions.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}.");
    }
}
