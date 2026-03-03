# Claude Instructions for Stripe.NET Extensions

This repository contains .NET extension packages for the [Stripe.net SDK](https://github.com/stripe/stripe-dotnet), providing Dependency Injection and ASP.NET Core webhook helpers.

## Prerequisites & Setup

- **.NET SDK**: Latest LTS version (specified in `global.json`)
- **Just**: Build automation tool (install via `brew install just` on macOS, or download from [just.systems](https://just.systems/))
- **Local Tools**: Run `dotnet tool restore` to restore local tool dependencies (MinVer for versioning)
- See [CONTRIBUTING.md](../CONTRIBUTING.md) for complete developer setup instructions.

## Folder Structure

```
stripe-dotnet-extensions/
├── src/                                          # Source libraries
│   ├── Stripe.Extensions.DependencyInjection/    # Core DI extension package
│   ├── Stripe.Extensions.AspNetCore/             # ASP.NET Core webhook helpers
│   └── Stripe.Extensions.AspNetCore.SourceGenerators/  # Source generators for type-safe webhooks
├── tests/                                        # Unit tests
│   ├── Stripe.Extensions.DependencyInjection.Tests/
│   ├── Stripe.Extensions.AspNetCore.Tests/
│   └── Stripe.Extensions.AspNetCore.SourceGenerators.Tests/
├── samples/                                      # Example projects
│   └── SampleCheckout/                           # Demo ASP.NET Core application
├── artifacts/                                    # Build outputs (gitignored)
│   └── packages/                                 # NuGet packages from `just pack`
├── .github/                                      # GitHub workflows and configuration
├── Stripe.Extensions.sln                         # Solution file
├── justfile                                      # Just build automation recipes
├── Directory.Build.props                         # Shared MSBuild properties
├── global.json                                   # .NET SDK version constraint
├── dotnet-tools.json                             # Local tool manifest
├── NuGet.config                                  # NuGet feed configuration
└── README.md                                     # Project documentation
```

## Build System: Just

This project uses [Just](https://just.systems/) for build automation. Run `just` with no arguments to see all available recipes.

### Common Recipes

**Build & Clean**:
- `just build` - Build solution in Release configuration
- `just build-debug` - Build in Debug configuration
- `just compile` - Alias for `just build`
- `just clean` - Remove all build artifacts (`bin/`, `obj/`, `artifacts/`)
- `just restore` - Restore NuGet packages

**Testing**:
- `just test` - Run all unit tests (Release configuration)
- `just verify` - Alias for `just test`
- `just test-filter "<filter>"` - Run tests matching filter (e.g., `just test-filter "FullyQualifiedName~ServiceCollectionExtensionsTest"`)
- `just test-coverage` - Run tests with code coverage metrics

**Packaging**:
- `just pack` - Create NuGet packages (.nupkg and .snupkg symbol packages)
- `just publish-nuget` - Publish packages to NuGet.org (requires `NUGET_API_KEY` environment variable)
- `just publish-github` - Publish packages to GitHub Package Registry (requires `NUGET_GITHUB_TOKEN` environment variable)

**OpenAPI Source Generator**:
- `just fetch-openapi-with-validation` - Download latest Stripe OpenAPI spec with validation (requires `jq`)
- `just fetch-openapi` - Download latest Stripe OpenAPI spec without validation

**Pipeline Shortcuts**:
- `just validate` - Build and run tests
- `just ci` - Full CI pipeline: clean → build → test → pack
- `just info` - Display build information and metadata

### Alternative: Direct dotnet CLI

All Just recipes wrap `dotnet` commands. You can call dotnet directly:
- `dotnet build` - Build solution
- `dotnet test` - Run tests
- `dotnet pack` - Create packages
- `dotnet tool restore` - Restore local tools (MinVer)

## Testing

Tests are organized by functionality:
- **DependencyInjection Tests**: `tests/Stripe.Extensions.DependencyInjection.Tests/`
  - Tests for `AddStripe()` extension methods
  - Tests for `StripeClient` lifecycle and keyed services
- **AspNetCore Tests**: `tests/Stripe.Extensions.AspNetCore.Tests/`
  - Tests for webhook handling and middleware
- **SourceGenerator Tests**: `tests/Stripe.Extensions.AspNetCore.SourceGenerators.Tests/`
  - Tests for code generation behavior

Run tests with:
```bash
just test              # All tests, Release config
just test-filter "YourTest"  # Filtered test
just test-coverage     # With code coverage
```

## Packaging & Versioning

**Versioning**: Uses [MinVer](https://github.com/adamralph/minver) for semantic versioning based on git tags and commits.
- Version is determined dynamically from: git history, tags, and commit count
- Run `dotnet tool run minver --default-pre-release-identifiers preview` to see current version
- Version format: `major.minor.patch[-prerelease-identifier]`

**NuGet Packages**:
- Created in `artifacts/packages/` via `just pack`
- Includes both `.nupkg` (library) and `.snupkg` (symbols) packages
- Packages include:
  - README.md with documentation
  - Stripe logo icon (`stripe_logo_blurple.png`)
  - Symbol files for debugging with embedded sources

**Package Metadata**:
- Authors: Cecil Phillip, Pavel Krymets
- Company: Stripe
- License: MIT
- Repository: https://github.com/cecilphillip/stripe-dotnet-extensions

**Publishing**:
- **NuGet.org**: `just publish-nuget` (requires `NUGET_API_KEY` environment variable)
- **GitHub Registry**: `just publish-github` (requires `NUGET_GITHUB_TOKEN` environment variable)

## Build Configuration

**Shared Properties** (`Directory.Build.props`):
- Language version: Latest C# features
- Implicit usings: Enabled
- Nullable reference types: Enabled
- Code analyzers: Enabled by default
- Documentation generation: Disabled for libraries
- .NET analyzer checks: Enabled (EOL target framework detection)
- Auto-generate binding redirects: Enabled
- Assembly info generation: Enabled

**Project Types**:
- Test projects detected automatically (any project name containing "Test")
- Symbol packages enabled with embedded sources for debugging
- Continuous integration detection: Sets `ContinuousIntegrationBuild=true` when running in GitHub Actions
- Non-packable projects (tests, samples) excluded from packing

**Output Artifacts**:
- Release builds: Optimized assemblies in `artifacts/packages/`
- Debug builds: Unoptimized assemblies for local development
- All configurations: Symbol files (.pdb) included for debugging

## High-Level Architecture

### Core Extensions (`Stripe.Extensions.DependencyInjection`)
- Provides `AddStripe()` extension methods on `IServiceCollection`
- Manages `StripeClient` lifecycle (registered as Scoped service)
- Supports **Keyed Services** for managing multiple named Stripe clients
- Configuration binding from `Stripe` section of configuration
- Allows both default and named/keyed client instances

### ASP.NET Core Helpers (`Stripe.Extensions.AspNetCore`)
- Simplifies Stripe webhook handling in ASP.NET Core
- Users implement `StripeWebhookHandler<T>` to handle specific event types
- `MapStripeWebhookHandler<T>` extension registers webhook routes
- Automatic webhook signature verification using `StripeOptions.WebhookSecret`
- Validates incoming webhook requests before processing

### Source Generators (`Stripe.Extensions.AspNetCore.SourceGenerators`)
- Generates type-safe webhook handler base classes from OpenAPI spec
- Automatically fetches latest Stripe API schema (OpenAPI 3.0 format)
- Generates handler method stubs for all Stripe event types
- Improves developer experience with IntelliSense and type safety
- Spec location: `src/Stripe.Extensions.AspNetCore.SourceGenerators/stripeapi.spec3.sdk.json`

## Key Conventions

**Dependency Injection**:
- Prefer `StripeClient` (concrete class) over `IStripeClient` interface for injection
- For named/keyed clients, use `[FromKeyedServices("clientName")]` attribute in constructors
- Keyed services registered with `AddStripe("clientName")` in service collection

**Configuration**:
- Configuration bound from the `Stripe` section (e.g., `Stripe:Default`, `Stripe:ClientName`)
- `StripeOptions` class holds configuration values: `ApiKey`, `WebhookSecret`
- Environment variable format: `Stripe__Default__ApiKey=sk_test_...` (double underscore for nested config)
- Example in appsettings.json:
  ```json
  {
    "Stripe": {
      "Default": {
        "ApiKey": "sk_test_...",
        "WebhookSecret": "whsec_..."
      },
      "Secondary": {
        "ApiKey": "sk_test_...",
        "WebhookSecret": "whsec_..."
      }
    }
  }
  ```

**Webhook Handlers**:
- Inherit from `StripeWebhookHandler<T>` where `T` is the strongly-typed event class
- Override specific `On*Async` methods (e.g., `OnCustomerCreatedAsync`) instead of a generic handle method
- Dependencies injected via constructor DI
- Handler methods are async: `Task OnEventTypeAsync(EventType @event)`
- Handler methods receive the deserialized event object with full type information
- Route registered with `MapStripeWebhookHandler<THandler>(pattern)` in ASP.NET Core

**Code Analysis**:
- Default .NET analyzers enabled in `Directory.Build.props`
- No external linting tools required; analyzer warnings are part of build output
- Treat analyzer warnings as build errors in CI pipelines
- All public API should pass nullable reference type analysis

**Git & Versioning**:
- Semantic versioning via MinVer based on git tags
- Tag format: `v1.2.3` for releases
- Pre-release identifiers: `preview`, `alpha`, `beta` (default: `preview`)
- Version automatically incremented based on commit history since last tag
