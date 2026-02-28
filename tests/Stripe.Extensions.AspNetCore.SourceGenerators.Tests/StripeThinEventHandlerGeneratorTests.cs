using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace Stripe.Extensions.AspNetCore.SourceGenerators.Tests;

public class StripeThinEventHandlerGeneratorTests
{
    [Fact]
    public void GeneratorOutputsThinEventsFile()
    {
        // Run generator using a driver with an empty compilation
        var generator = new StripeThinEventHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeThinEventHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out var diagnostics);
        
        // Retrieve all files in the compilation.
        var generatedFiles = newCompilation.SyntaxTrees
            .Select(t => Path.GetFileName(t.FilePath))
            .ToArray();

        Assert.Equivalent(new[] { "Stripe.Extensions.AspNetCore.ThinEvents.g.cs" }, generatedFiles);
        
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void GeneratedCodeContainsExpectedMethods()
    {
        var generator = new StripeThinEventHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeThinEventHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out _);
        
        var generatedCode = newCompilation.SyntaxTrees.First().ToString();

        // Verify strongly-typed handler methods exist
        Assert.Contains("OnV1BillingMeterErrorReportTriggeredAsync", generatedCode);
        Assert.Contains("OnV1BillingMeterNoMeterFoundAsync", generatedCode);
        Assert.Contains("OnV2CoreAccountCreatedAsync", generatedCode);
        Assert.Contains("OnV2CoreAccountUpdatedAsync", generatedCode);
        Assert.Contains("OnV2CoreAccountClosedAsync", generatedCode);
        Assert.Contains("OnV2CoreEventDestinationPingAsync", generatedCode);
        
        // Verify bracket notation events are handled correctly
        Assert.Contains("OnV2CoreAccountIncludingRequirementsUpdatedAsync", generatedCode);
        Assert.Contains("OnV2CoreAccountIncludingConfigurationCustomerUpdatedAsync", generatedCode);
    }

    [Fact]
    public void GeneratedCodeContainsExecuteAsyncSwitch()
    {
        var generator = new StripeThinEventHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeThinEventHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out _);
        
        var generatedCode = newCompilation.SyntaxTrees.First().ToString();

        // Verify ExecuteAsync switch exists
        Assert.Contains("protected virtual Task ExecuteAsync(EventNotification notification) => notification switch", generatedCode);
        
        // Verify pattern matching for strongly-typed notifications
        Assert.Contains("V1BillingMeterErrorReportTriggeredEventNotification n =>", generatedCode);
        Assert.Contains("V2CoreAccountCreatedEventNotification n =>", generatedCode);
        
        // Verify UnknownEventNotification handling
        Assert.Contains("UnknownEventNotification n => UnknownEventAsync(n)", generatedCode);
    }

    [Fact]
    public void GeneratedCodeUsesCorrectNotificationTypes()
    {
        var generator = new StripeThinEventHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeThinEventHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out _);
        
        var generatedCode = newCompilation.SyntaxTrees.First().ToString();

        // Verify notification types follow SDK naming convention
        Assert.Contains("V1BillingMeterErrorReportTriggeredEventNotification notification", generatedCode);
        Assert.Contains("V2CoreAccountCreatedEventNotification notification", generatedCode);
        Assert.Contains("V2CoreAccountIncludingConfigurationMerchantCapabilityStatusUpdatedEventNotification notification", generatedCode);
    }

    [Fact]
    public void GeneratedCodeContainsRequiredUsings()
    {
        var generator = new StripeThinEventHandlerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        var compilation = CSharpCompilation.Create(nameof(StripeThinEventHandlerGeneratorTests));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var newCompilation, out _);
        
        var generatedCode = newCompilation.SyntaxTrees.First().ToString();

        // Verify required using statements
        Assert.Contains("using Stripe;", generatedCode);
        Assert.Contains("using Stripe.Events;", generatedCode);
        Assert.Contains("using Stripe.V2.Core;", generatedCode);
    }
}
