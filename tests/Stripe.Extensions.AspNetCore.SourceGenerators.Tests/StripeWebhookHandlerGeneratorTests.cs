using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace Stripe.Extensions.AspNetCore.SourceGenerators.Tests;

public class StripeWebhookHandlerGeneratorTests
{
    [Fact]
    public void GeneratedWebhookHandlerCompilesAgainstStripeSdk()
    {
        IIncrementalGenerator generator = new StripeWebhookHandlerGenerator();
        var source = $$"""
            namespace Stripe.Extensions.AspNetCore;
            public abstract partial class StripeWebhookHandler<T>
            {
                private System.Threading.Tasks.Task UnhandledEventAsync(Stripe.Event notification)
                    => System.Threading.Tasks.Task.CompletedTask;
                protected virtual System.Threading.Tasks.Task UnknownEventAsync(Stripe.Event notification)
                    => System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Stripe.Event).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "GeneratedHandlers",
            [
                CSharpSyntaxTree.ParseText("global using System.Threading.Tasks;"),
                CSharpSyntaxTree.ParseText(source)
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(generator)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var assembly = new MemoryStream();
        var result = output.Emit(assembly);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [Fact]
    public void GeneratorOutputsAspNetCore()
    {
        // Run generator using a driver with an empty compilation
        var generator = new StripeWebhookHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeWebhookHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);
        
        // Retrieve all files in the compilation.
        var generatedFiles = newCompilation.SyntaxTrees
            .Select(t => Path.GetFileName(t.FilePath))
            .ToArray();

        Assert.Equivalent(new[] { "Stripe.Extensions.AspNetCore.g.cs" }, generatedFiles);
        
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}