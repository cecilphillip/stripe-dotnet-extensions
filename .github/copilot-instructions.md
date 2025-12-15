# Stripe .NET Extensions - AI Coding Agent Instructions

## Project Overview
This repository provides extension libraries for integrating the Stripe .NET SDK into .NET applications, focusing on dependency injection and webhook handling for ASP.NET Core. The project consists of three packages that work together:

- **Stripe.Extensions.DependencyInjection** - Core DI registration for `StripeClient` with named client support
- **Stripe.Extensions.AspNetCore** - Webhook handling framework with signature validation  
- **Stripe.Extensions.AspNetCore.SourceGenerators** - Generates event handler methods from Stripe OpenAPI spec

## Architecture Patterns

### Named Clients
The library supports both default and named `StripeClient` instances. Named clients enable multi-tenant scenarios where each tenant has separate API keys:

- Default client: `services.AddStripe()` → inject `StripeClient` directly
- Named clients: `services.AddStripe("client1")` → inject with `[FromKeyedServices("client1")]`
- Configuration sections match client names: `Stripe:Default` or `Stripe:client1`
- See [StripeServiceCollectionExtensions.cs](../src/Stripe.Extensions.DependencyInjection/StripeServiceCollectionExtensions.cs) for registration logic

### Webhook Handler Helpers
Webhook handlers inherit from `StripeWebhookHandler<T>` (generic self-reference enables DI resolution). The framework:

1. Validates Stripe signature using `WebhookSecret` from configuration
2. Parses JSON payload into `Event` object
3. Routes to generated `On*Async` methods based on event type (e.g., `OnCustomerCreatedAsync`)
4. Handlers override specific event methods; unhandled events log warnings

Example: See [SampleCheckout/Program.cs](../samples/SampleCheckout/Program.cs) for minimal implementation.

### Source Generator Architecture
[StripeWebhookHandlerGenerator.cs](../src/Stripe.Extensions.AspNetCore.SourceGenerators/StripeWebhookHandlerGenerator.cs) generates partial class methods at compile-time:

- Reads embedded `stripeapi.spec3.sdk.json` OpenAPI spec
- Extracts event names from `/v1/webhook_endpoints` endpoint schema
- Generates virtual `On*Async` methods (e.g., `customer.created` → `OnCustomerCreatedAsync`)
- Generated code becomes part of `StripeWebhookHandler<T>` partial class

**Important**: To update event types, replace the embedded JSON spec file and rebuild.

## Build & Development Workflow

### NUKE Build System
This project uses NUKE (not standard dotnet CLI directly). All builds run through [build/Build.cs](../build/Build.cs) via the `dotnet nuke` local tool:

```bash
# Restore dependencies
dotnet nuke Restore

# Build project
dotnet nuke Compile --Configuration Release

# Run all tests
dotnet nuke Test --Configuration Release

# Create NuGet packages (includes compile + version resolution)
dotnet nuke Pack --Configuration Release

# Publish packages (updates NuGet/GitHub registries)
dotnet nuke Publish --Configuration Release

# Clean targets (remove bin/obj directories)
dotnet nuke CleanSource        # Clean src/ directories
dotnet nuke CleanTests         # Clean tests/ directories
dotnet nuke CleanSamples       # Clean samples/ directories
dotnet nuke Clean              # Clean all (src + tests + samples + artifacts/)
```


### Testing Patterns
Tests use in-memory test servers (`WebApplicationFactory` / `TestServer`) to test handlers end-to-end:

- [WebhookHandlerTests.cs](../tests/Stripe.Extensions.AspNetCore.Tests/WebhookHandlerTests.cs) shows signature validation testing
- use XUnit as the test framework
- Use `FakeItEasy` for mocking (not Moq) - see existing test patterns
- Test webhook handlers by invoking `On*Async` methods directly without HTTP overhead

### Multi-Targeting
All libraries target  `net8.0`, `net9.0`, and `net10.0` (see [Stripe.Extensions.AspNetCore.csproj](../src/Stripe.Extensions.AspNetCore/Stripe.Extensions.AspNetCore.csproj)). When adding features:

- Use `#if` directives if APIs differ between framework versions
- Test on all target frameworks in CI (GitHub Actions runs on Windows/Linux/macOS)

## Configuration & Options

### StripeOptions
[StripeOptions.cs](../src/Stripe.Extensions.DependencyInjection/StripeOptions.cs) extends `StripeClientOptions` from Stripe SDK with:

- `WebhookSecret` - Required for signature validation (throws at runtime if missing)
- `ThrowOnWebhookApiVersionMismatch` - Default `true` (fails fast on version skew)
- `ClientName` - Auto-populated during registration, matches DI service key
- `EnableTelemetry` - Default `true`

Configuration binding happens in two phases:
1. `IConfiguration` section binding: `Stripe:{clientName}:ApiKey`
2. Post-configure with optional delegate: `AddStripe(opts => opts.ApiKey = "...")`

## Key Files Reference

- **Webhook execution**: [StripeWebhookHandler.cs](../src/Stripe.Extensions.AspNetCore/StripeWebhookHandler.cs#L12-L60) - Main request handling + signature validation
- **Endpoint registration**: [StripeAppBuilderExtensions.cs](../src/Stripe.Extensions.AspNetCore/StripeAppBuilderExtensions.cs) - Maps webhook route with DI factory
- **Logging**: [StripeWebhookHandlerLogger.cs](../src/Stripe.Extensions.AspNetCore/StripeWebhookHandlerLogger.cs) - High-performance LoggerMessage source generators
- **Version info**: [global.json](../global.json) - Pins SDK to 10.0.100, `rollForward: latestMinor`

## Common Tasks

**Add new webhook event handling**: Override the corresponding `On*Async` method in your handler class. If the event doesn't exist, regenerate the spec file.
**Support new configuration option**: Add property to `StripeOptions`, update README examples, add test in `ServiceCollectionExtensionsTest`.
**Debug webhook signature failures**: Check `WebhookSecret` matches Stripe Dashboard value. Signature validation uses 300-second tolerance (hardcoded in [StripeWebhookHandler.cs](../src/Stripe.Extensions.AspNetCore/StripeWebhookHandler.cs)).
**Run sample app**: `cd samples/SampleCheckout && dotnet run` (requires valid Stripe keys in appsettings).
