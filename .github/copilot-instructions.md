# Copilot Instructions for Stripe.NET Extensions

This repository contains .NET extension packages for the [Stripe.net SDK](https://github.com/stripe/stripe-dotnet), providing Dependency Injection and ASP.NET Core webhook helpers.

## Build, Test, and Lint

- **Build**: Run `./build.sh` (macOS/Linux) or `.\build.ps1` (Windows).
  - Alternatively: `dotnet build`
- **Run Tests**: Run `./build.sh --target Test` or `dotnet test`
- **Run Single Test**: Use `dotnet test --filter` with the fully qualified name.
  - Example: `dotnet test --filter "FullyQualifiedName~Stripe.Extensions.DependencyInjection.Tests.ServiceCollectionExtensionsTest.CanResolveStripeOptions"`
- **Lint/Format**: The project uses default .NET analyzers enabled in `Directory.Build.props`.

## High-Level Architecture

- **Core Extensions** (`Stripe.Extensions.DependencyInjection`): 
  - Provides `AddStripe()` extension methods on `IServiceCollection`.
  - Manages `StripeClient` lifecycle (registered as Scoped).
  - Supports **Keyed Services** for managing multiple named Stripe clients.
- **ASP.NET Core Helpers** (`Stripe.Extensions.AspNetCore`):
  - Simplifies webhook handling.
  - Users implement `StripeWebhookHandler<T>` to handle events.
  - `MapStripeWebhookHandler<T>` registers the route.
- **Build System**: Uses [Nuke](https://nuke.build/) for build automation (defined in `build/Build.cs`).

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
