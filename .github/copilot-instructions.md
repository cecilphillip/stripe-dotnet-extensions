# Copilot Instructions for Stripe.NET Extensions

This repository contains .NET extension packages for the [Stripe.net SDK](https://github.com/stripe/stripe-dotnet), providing Dependency Injection and ASP.NET Core webhook helpers.

## Build, Test, and Packaging

This project uses [Just](https://just.systems/) for build automation.

### Build System Commands

**Using Just** (recommended):
- **Build**: `just build` - Build solution in Release configuration
- **Build Debug**: `just build-debug` - Build in Debug configuration
- **Run Tests**: `just test` - Run all unit tests
- **Run Single Test**: `just test-filter "FullyQualifiedName~Stripe.Extensions.DependencyInjection.Tests.ServiceCollectionExtensionsTest.CanResolveStripeOptions"`
- **Create Packages**: `just pack` - Build and create NuGet packages
- **Clean**: `just clean` - Clean all build artifacts
- **Full Pipeline**: `just ci` - Clean → build → test → pack

**Using dotnet CLI directly**:
- Build: `dotnet build`
- Test: `dotnet test`
- Pack: `dotnet pack`

### Code Analysis & Formatting
- The project uses default .NET analyzers enabled in `Directory.Build.props`.
- No external linting/formatting tools required; analyzer warnings treated as build output.

### Setup
- Install Just: `brew install just` (macOS/Linux) or download from [just.systems](https://just.systems/)
- See [CONTRIBUTING.md](../CONTRIBUTING.md) for complete developer setup instructions.

## High-Level Architecture

- **Core Extensions** (`Stripe.Extensions.DependencyInjection`): 
  - Provides `AddStripe()` extension methods on `IServiceCollection`.
  - Manages `StripeClient` lifecycle (registered as Scoped).
  - Supports **Keyed Services** for managing multiple named Stripe clients.
- **ASP.NET Core Helpers** (`Stripe.Extensions.AspNetCore`):
  - Simplifies webhook handling.
  - Users implement `StripeWebhookHandler<T>` to handle events.
  - `MapStripeWebhookHandler<T>` registers the route.
- **Build System**: Uses [Just](https://just.systems/) for build automation (recipes defined in `justfile`). Versioning handled by [MinVer](https://github.com/adamralph/minver).

## Key Conventions

- **Dependency Injection**:
  - Prefer `StripeClient` over `IStripeClient` for injection.
  - For named clients, use `[FromKeyedServices("clientName")]` in constructors.
- **Configuration**:
  - Configuration is bound from the `Stripe` section (e.g., `Stripe:Default`, `Stripe:ClientName`).
  - `StripeOptions` class holds configuration values (ApiKey, WebhookSecret).
- **Webhook Handlers**:
  - Inherit from `StripeWebhookHandler<T>`.
  - Override specific `On*Async` methods (e.g., `OnCustomerCreatedAsync`) instead of a generic handle method.
  - Dependencies should be injected via constructor.
- **Project Structure**:
  - Shared build properties are in `Directory.Build.props`.
  - Source code in `src/`, tests in `tests/`.
  - Global SDK version pinned in `global.json`.
