using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Stripe.Extensions.AspNetCore.SourceGenerators;

[Generator]
public class StripeWebhookHandlerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            var handlerCode = GenerateEventHandlerCode();

            var generatedCode = SourceText.From($@"
using Stripe;

namespace Stripe.Extensions.AspNetCore;

public abstract partial class StripeWebhookHandler<T>
{{
 
    // generated code
{handlerCode}
}}
", Encoding.UTF8);

            spc.AddSource("Stripe.Extensions.AspNetCore.g.cs", generatedCode);
        });
    }

    private string GetEmbeddedResourceSpecJson()
    {
        const string resourceName = "Stripe.Extensions.AspNetCore.SourceGenerators.stripeapi.spec3.sdk.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)!;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private IEnumerable<string> GetStripeEventNames()
    {
        var stripeApiSpec = GetEmbeddedResourceSpecJson();
        var specJson = JsonNode.Parse(stripeApiSpec);
        var events = specJson!
            ["paths"]!
            ["/v1/webhook_endpoints"]!
            ["post"]!
            ["requestBody"]!
            ["content"]!
            ["application/x-www-form-urlencoded"]!
            ["schema"]!
            ["properties"]!
            ["enabled_events"]!
            ["items"]!
            ["enum"]!.AsArray().Select(e => e!.ToString());

        return events;
    }

    private string GenerateEventHandlerCode()
    {
        var eventNames = GetStripeEventNames();
        var info = CultureInfo.InvariantCulture.TextInfo;
        var builder = new StringBuilder();

        var methods = eventNames.Where(e => e != "*").Select(e =>
        {
            var methodName = "On" + info.ToTitleCase(e)
                .Replace("_", string.Empty)
                .Replace(".", string.Empty) + "Async";

            return new { MethodName = methodName, EventName = e };
        }).ToArray();

        foreach (var method in methods)
        {
            builder.AppendLine($"    // Fired when the {method.EventName} event is received.");
            builder.AppendLine($"    public virtual Task {method.MethodName}(Event e) => UnhandledEventAsync(e);");
            builder.AppendLine();
        }

        builder.AppendLine("    protected virtual Task ExecuteAsync(Event e) => e.Type switch");
        builder.AppendLine("    {");
        foreach (var method in methods)
        {
            builder.AppendLine($@"        ""{method.EventName}"" => {method.MethodName}(e),");
        }

        builder.AppendLine("        _ => UnknownEventAsync(e),");
        builder.AppendLine("    };");

        return builder.ToString();
    }
}