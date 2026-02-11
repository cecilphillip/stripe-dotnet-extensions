using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Stripe.Extensions.AspNetCore.SourceGenerators;

[Generator]
public class StripeThinEventHandlerGenerator : IIncrementalGenerator
{
    // Known SDK EventNotification types mapping event type to SDK class name (without Event/EventNotification suffix)
    // Generated from Stripe.net v50.3.0 SDK - these are all thin events with typed classes
    private static readonly Dictionary<string, string> KnownSdkTypes = new()
    {
        ["v1.billing.meter.error_report_triggered"] = "V1BillingMeterErrorReportTriggered",
        ["v1.billing.meter.no_meter_found"] = "V1BillingMeterNoMeterFound",
        ["v2.core.account.closed"] = "V2CoreAccountClosed",
        ["v2.core.account.created"] = "V2CoreAccountCreated",
        ["v2.core.account.updated"] = "V2CoreAccountUpdated",
        ["v2.core.account[configuration.customer].capability_status_updated"] = "V2CoreAccountIncludingConfigurationCustomerCapabilityStatusUpdated",
        ["v2.core.account[configuration.customer].updated"] = "V2CoreAccountIncludingConfigurationCustomerUpdated",
        ["v2.core.account[configuration.merchant].capability_status_updated"] = "V2CoreAccountIncludingConfigurationMerchantCapabilityStatusUpdated",
        ["v2.core.account[configuration.merchant].updated"] = "V2CoreAccountIncludingConfigurationMerchantUpdated",
        ["v2.core.account[configuration.recipient].capability_status_updated"] = "V2CoreAccountIncludingConfigurationRecipientCapabilityStatusUpdated",
        ["v2.core.account[configuration.recipient].updated"] = "V2CoreAccountIncludingConfigurationRecipientUpdated",
        ["v2.core.account[defaults].updated"] = "V2CoreAccountIncludingDefaultsUpdated",
        ["v2.core.account[future_requirements].updated"] = "V2CoreAccountIncludingFutureRequirementsUpdated",
        ["v2.core.account[identity].updated"] = "V2CoreAccountIncludingIdentityUpdated",
        ["v2.core.account[requirements].updated"] = "V2CoreAccountIncludingRequirementsUpdated",
        ["v2.core.account_link.returned"] = "V2CoreAccountLinkReturned",
        ["v2.core.account_person.created"] = "V2CoreAccountPersonCreated",
        ["v2.core.account_person.deleted"] = "V2CoreAccountPersonDeleted",
        ["v2.core.account_person.updated"] = "V2CoreAccountPersonUpdated",
        ["v2.core.event_destination.ping"] = "V2CoreEventDestinationPing",
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, (spc, compilation) =>
        {
            var handlerCode = GenerateEventHandlerCode();

            var generatedCode = SourceText.From($@"
using Stripe;
using Stripe.Events;
using Stripe.V2.Core;

namespace Stripe.Extensions.AspNetCore;

public abstract partial class StripeThinEventHandler<T>
{{
    // generated code
{handlerCode}
}}
", Encoding.UTF8);

            spc.AddSource("Stripe.Extensions.AspNetCore.ThinEvents.g.cs", generatedCode);
        });
    }

    private string GetEmbeddedResourceSpecJson()
    {
        const string resourceName = "Stripe.Extensions.AspNetCore.SourceGenerators.stripeapi.spec3.sdk.json";
        using var stream = GetType().Assembly.GetManifestResourceStream(resourceName)!;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private IEnumerable<string> GetThinEventNames()
    {
        // Start with all known SDK types - these are guaranteed to have typed notification classes
        foreach (var knownType in KnownSdkTypes.Keys)
        {
            yield return knownType;
        }

        // Also extract from component schemas for any additional thin events
        var stripeApiSpec = GetEmbeddedResourceSpecJson();
        var specJson = JsonNode.Parse(stripeApiSpec);

        var schemas = specJson!["components"]?["schemas"]?.AsObject();
        if (schemas != null)
        {
            foreach (var schema in schemas)
            {
                var name = schema.Key;
                // Skip if already in known types, and check if it's a thin event
                if (!KnownSdkTypes.ContainsKey(name) && IsThinEventType(name) && IsCanonicalEventFormat(name))
                {
                    yield return name;
                }
            }
        }
    }

    private static bool IsThinEventType(string eventType)
    {
        // Thin events start with v1. or v2. and have an action suffix
        return (eventType.StartsWith("v1.") || eventType.StartsWith("v2.")) &&
               !eventType.StartsWith("v2.error.") &&
               eventType != "v2.deleted_object" &&
               (eventType.EndsWith(".created") || 
                eventType.EndsWith(".updated") || 
                eventType.EndsWith(".deleted") ||
                eventType.EndsWith(".closed") ||
                eventType.EndsWith(".returned") ||
                eventType.EndsWith(".ping") ||
                eventType.EndsWith("_triggered") ||  // e.g., error_report_triggered
                eventType.EndsWith(".capability_status_updated") ||
                eventType.EndsWith("_found"));  // e.g., no_meter_found
    }

    private static bool IsCanonicalEventFormat(string eventType)
    {
        // Filter out underscore-format duplicates like v2.core.account_requirements_.updated
        // Keep bracket format like v2.core.account[requirements].updated
        // Also keep simple formats like v2.core.account.created
        
        // If it contains brackets, it's canonical
        if (eventType.Contains("["))
            return true;
            
        // If it has pattern like name_. before action (e.g., account_requirements_.updated), it's legacy
        if (Regex.IsMatch(eventType, @"_\.(created|updated|deleted|closed|returned|ping|triggered|capability_status_updated)$"))
            return false;
            
        return true;
    }

    private string GenerateEventHandlerCode()
    {
        var eventNames = GetThinEventNames().Distinct().OrderBy(e => e).ToArray();
        var info = CultureInfo.InvariantCulture.TextInfo;
        var builder = new StringBuilder();

        var methods = eventNames.Select(e =>
        {
            var hasTypedClass = KnownSdkTypes.TryGetValue(e, out var sdkTypeName);
            var methodName = "On" + (hasTypedClass ? sdkTypeName : EventTypeToMethodName(e, info)) + "Async";
            var notificationTypeName = hasTypedClass 
                ? sdkTypeName + "EventNotification" 
                : "EventNotification";

            return new 
            { 
                MethodName = methodName, 
                EventName = e,
                HasTypedNotification = hasTypedClass,
                NotificationTypeName = notificationTypeName
            };
        }).ToArray();

        // Generate handler methods
        foreach (var method in methods)
        {
            builder.AppendLine($"    /// <summary>");
            builder.AppendLine($"    /// Fired when the {method.EventName} thin event notification is received.");
            builder.AppendLine($"    /// </summary>");
            builder.AppendLine($"    public virtual Task {method.MethodName}({method.NotificationTypeName} notification) => UnhandledEventAsync(notification);");
            builder.AppendLine();
        }

        // Generate ExecuteAsync switch using pattern matching
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Dispatches the event notification to the appropriate handler method.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    protected virtual Task ExecuteAsync(EventNotification notification) => notification switch");
        builder.AppendLine("    {");
        
        // First, handle strongly-typed notifications with type pattern
        foreach (var method in methods.Where(m => m.HasTypedNotification))
        {
            builder.AppendLine($"        {method.NotificationTypeName} n => {method.MethodName}(n),");
        }
        
        // Then, handle non-typed notifications with property pattern on Type
        foreach (var method in methods.Where(m => !m.HasTypedNotification))
        {
            builder.AppendLine($@"        {{ Type: ""{method.EventName}"" }} n => {method.MethodName}(n),");
        }

        builder.AppendLine("        UnknownEventNotification n => UnknownEventAsync(n),");
        builder.AppendLine("        _ => UnknownEventAsync(notification),");
        builder.AppendLine("    };");

        return builder.ToString();
    }

    private static string EventTypeToMethodName(string eventType, TextInfo textInfo)
    {
        // Fallback for events not in KnownSdkTypes
        // v1.billing.meter.error_report_triggered -> V1BillingMeterErrorReportTriggered
        // v2.core.account[requirements].updated -> V2CoreAccountIncludingRequirementsUpdated
        
        // Handle bracket notation: [xxx] becomes Including{Xxx}
        var normalized = Regex.Replace(eventType, @"\[([^\]]+)\]", match =>
        {
            var content = match.Groups[1].Value;
            // Title case the content and prefix with Including
            var titleCased = textInfo.ToTitleCase(content.Replace(".", " ").Replace("_", " "))
                .Replace(" ", string.Empty);
            return "Including" + titleCased;
        });
        
        // Title case and remove separators
        return textInfo.ToTitleCase(normalized.Replace("_", " ").Replace(".", " "))
            .Replace(" ", string.Empty);
    }
}
