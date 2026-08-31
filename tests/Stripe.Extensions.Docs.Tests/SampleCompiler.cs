using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Stripe.Extensions.Docs.Tests;

/// <summary>
/// Compiles a documentation sample against the real assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Documentation samples are written as fragments, so they are wrapped before compiling. A sample
/// may be a set of type declarations, a run of statements, or a mix of both, and it may rely on
/// variables introduced by the surrounding prose (<c>builder</c>, <c>app</c>, <c>api</c>). Rather
/// than forcing every block to be a self-contained program - which would make the documentation
/// far worse to read - the compiler tries a small set of contexts and accepts the sample when any
/// one of them compiles.
/// </para>
/// <para>
/// This is deliberately lenient about <em>context</em> and strict about <em>API usage</em>. Every
/// context resolves against the same real assemblies, so an unknown type, a misspelled method, a
/// wrong signature or a syntax error fails in all of them. That is precisely the class of defect
/// this harness exists to catch.
/// </para>
/// </remarks>
internal static class SampleCompiler
{
    private static readonly string[] PreambleUsings =
    [
        "using System;",
        "using System.Collections.Generic;",
        "using System.Linq;",
        "using System.Threading;",
        "using System.Threading.Tasks;",
        "using Microsoft.AspNetCore.Builder;",
        "using Microsoft.AspNetCore.Http;",
        "using Microsoft.AspNetCore.Mvc;",
        "using Microsoft.Extensions.DependencyInjection;",
        "using Microsoft.Extensions.Logging;",
        "using Microsoft.Extensions.Logging.Abstractions;",
        "using Aspire.Hosting;",
        "using Aspire.Hosting.ApplicationModel;",
        "using Stripe;",
        "using Stripe.Events;",
        "using Stripe.Extensions.AspNetCore;",
        "using Stripe.Extensions.DependencyInjection;",
        "using Xunit;",
    ];

    /// <summary>
    /// Placeholder types the documentation refers to but never defines, because they stand in for
    /// something the reader owns.
    /// </summary>
    private const string Stubs = """
        public interface IMyService { Task RecordAsync(string customerId); }

        public sealed class FakeMyService : IMyService
        {
            public List<string> Recorded { get; } = new();
            public Task RecordAsync(string customerId) { Recorded.Add(customerId); return Task.CompletedTask; }
        }

        public interface IProvisioningService { Task CreateWorkspaceAsync(string accountId, CancellationToken cancellationToken); }
        public interface IDedupeStore { Task<bool> TryMarkSeenAsync(string id, CancellationToken cancellationToken); }
        public interface IMetricsSink { void Record(string? eventType, StripeEventNotificationOutcome? outcome); }

        // Subscribers the documentation registers as examples without re-declaring them.
        public sealed class AccountAnalyticsSubscriber : IStripeEventSubscriber<V2CoreAccountCreatedEventNotification>
        {
            public ValueTask HandleAsync(V2CoreAccountCreatedEventNotification n, StripeEventNotificationContext c, CancellationToken t)
                => ValueTask.CompletedTask;
        }

        public sealed class ComplianceSubscriber : IStripeEventSubscriber<V2CoreAccountCreatedEventNotification>
        {
            public ValueTask HandleAsync(V2CoreAccountCreatedEventNotification n, StripeEventNotificationContext c, CancellationToken t)
                => ValueTask.CompletedTask;
        }

        namespace Projects
        {
            public sealed class MyApi : Aspire.Hosting.IProjectMetadata
            {
                public string ProjectPath => "MyApi.csproj";
            }
        }
        """;

    private const string AspNetCoreLocals = """
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        var options = new StripeEventNotificationOptions();
        var store = default(IDedupeStore)!;
        var metrics = default(IMetricsSink)!;
        """;

    private const string AspireTargets = """
        var args = Array.Empty<string>();
        var builder = DistributedApplication.CreateBuilder(args);
        var api = builder.AddProject<Projects.MyApi>("api");
        var worker = builder.AddProject<Projects.MyApi>("worker");
        var checkout = builder.AddProject<Projects.MyApi>("checkout");
        var notifications = builder.AddProject<Projects.MyApi>("notifications");
        var paymentsService = builder.AddProject<Projects.MyApi>("payments");
        var notificationsService = builder.AddProject<Projects.MyApi>("notifications-svc");
        var stripeApiKey = builder.AddParameter("stripe-api-key", secret: true);
        var stripePublishableKey = builder.AddParameter("stripe-publishable-key", secret: false);
        """;

    private const string AspireResources = AspireTargets + """

        var stripe = builder.AddStripeCli("stripe");
        var stripeCli = builder.AddStripeCliContainer("stripe-cli");
        """;

    private const string ArgsOnly = """
        var args = Array.Empty<string>();
        """;

    private const string AspireBuilderOnly = ArgsOnly + """

        var builder = DistributedApplication.CreateBuilder(args);
        """;

    /// <summary>Contexts are tried in order; the first that compiles wins.</summary>
    private static readonly string[] Contexts =
    [
        "",
        ArgsOnly,
        AspNetCoreLocals,
        AspireResources,
        AspireTargets,
        AspireBuilderOnly,
    ];

    private static readonly Lazy<List<MetadataReference>> References = new(LoadReferences);

    /// <summary>
    /// Attempts to compile <paramref name="sample"/>. Returns <c>null</c> on success, or a report
    /// describing the closest failure.
    /// </summary>
    public static string? TryCompile(MarkdownSample sample, IReadOnlyList<string> contextTypes)
        => TryCompile(sample, contextTypes, out _);

    /// <inheritdoc cref="TryCompile(MarkdownSample, IReadOnlyList{string})"/>
    /// <param name="errorCount">Number of errors in the closest failure, or 0 on success.</param>
    public static string? TryCompile(
        MarkdownSample sample,
        IReadOnlyList<string> contextTypes,
        out int errorCount)
    {
        var (types, members, statements) = Partition(sample.Body);

        string? best = null;
        var fewest = int.MaxValue;

        foreach (var context in Contexts)
        {
            var source = Build(sample, contextTypes, types, members, statements, context);

            var compilation = CSharpCompilation.Create(
                "DocSamples",
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
                References.Value,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var errors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if (errors.Count == 0)
            {
                errorCount = 0;
                return null;
            }

            if (errors.Count < fewest)
            {
                fewest = errors.Count;
                best = string.Join(
                    Environment.NewLine,
                    errors.Take(5).Select(e => "  " + e.GetMessage()));
            }
        }

        errorCount = fewest;
        return best;
    }

    private static string Build(
        MarkdownSample sample,
        IReadOnlyList<string> contextTypes,
        string types,
        string members,
        string statements,
        string locals)
    {
        var source = new StringBuilder();

        foreach (var directive in PreambleUsings.Concat(sample.Usings).Distinct())
        {
            source.AppendLine(directive);
        }

        source.AppendLine(Stubs);

        foreach (var type in contextTypes)
        {
            source.AppendLine(type);
        }

        source.AppendLine(types);

        source.AppendLine("public class __Sample");
        source.AppendLine("{");
        source.AppendLine(members);
        source.AppendLine("    public async Task __RunAsync()");
        source.AppendLine("    {");
        source.AppendLine("        await Task.Yield();");
        source.AppendLine(locals);
        source.AppendLine(statements);
        source.AppendLine("    }");
        source.AppendLine("}");

        return source.ToString();
    }

    /// <summary>
    /// Splits a sample into type declarations, loose members and statements so each part can be
    /// placed where C# requires it.
    /// </summary>
    public static (string Types, string Members, string Statements) Partition(string body)
    {
        var unit = CSharpSyntaxTree.ParseText(body, new CSharpParseOptions(LanguageVersion.Latest))
            .GetCompilationUnitRoot();

        var types = new StringBuilder();
        var members = new StringBuilder();
        var statements = new StringBuilder();

        foreach (var member in unit.Members)
        {
            switch (member)
            {
                case BaseTypeDeclarationSyntax:
                case BaseNamespaceDeclarationSyntax:
                case DelegateDeclarationSyntax:
                    types.AppendLine(member.ToFullString());
                    break;

                case GlobalStatementSyntax global
                    when global.Statement is LocalFunctionStatementSyntax local:
                    members.AppendLine(local.ToFullString());
                    break;

                case GlobalStatementSyntax global:
                    statements.AppendLine(global.ToFullString());
                    break;

                default:
                    members.AppendLine(member.ToFullString());
                    break;
            }
        }

        return (types.ToString(), members.ToString(), statements.ToString());
    }

    /// <summary>Names of the types a sample declares, used to avoid duplicate definitions.</summary>
    public static IEnumerable<string> DeclaredTypeNames(string body)
        => CSharpSyntaxTree.ParseText(body, new CSharpParseOptions(LanguageVersion.Latest))
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(t => t.Identifier.Text);

    private static List<MetadataReference> LoadReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The test output directory carries the Stripe and Aspire assemblies, but framework
        // references such as ASP.NET Core resolve out of the shared framework instead, so both
        // locations have to be probed.
        var directories = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(typeof(object).Assembly.Location),
            Path.GetDirectoryName(typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location),
        };

        foreach (var directory in directories)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(directory, "*.dll"))
            {
                if (!seen.Add(Path.GetFileName(path)))
                {
                    continue;
                }

                try
                {
                    AssemblyName.GetAssemblyName(path);
                }
                catch (BadImageFormatException)
                {
                    continue;
                }
                catch (FileLoadException)
                {
                    continue;
                }

                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return references;
    }
}
