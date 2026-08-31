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
│   ├── Stripe.Extensions.AspNetCore.SourceGenerators/  # Source generators for type-safe webhooks
│   └── Stripe.Hosting.Aspire/                    # Aspire hosting integration for Stripe CLI
├── tests/                                        # Unit tests
│   ├── Stripe.Extensions.DependencyInjection.Tests/
│   ├── Stripe.Extensions.AspNetCore.Tests/
│   ├── Stripe.Extensions.AspNetCore.SourceGenerators.Tests/
│   └── Stripe.Hosting.Aspire.Tests/
├── samples/                                      # Example projects
│   ├── SampleCheckout/                           # Demo ASP.NET Core application
│   └── SampleCheckout.AppHost/                   # Aspire AppHost for local development
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
- `just publish-nuget` - Pack then publish packages to NuGet.org (requires `NUGET_API_KEY` environment variable)
- `just push-nuget` - Push already-built packages to NuGet.org without packing (requires `NUGET_API_KEY`)
- `just publish-github` - Pack then publish packages to GitHub Package Registry (requires `NUGET_GITHUB_TOKEN` environment variable)
- `just push-github` - Push already-built packages to GitHub without packing (requires `NUGET_GITHUB_TOKEN`)

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

Tests use **xUnit** as the test framework across all test projects.

Tests are organized by functionality:
- **DependencyInjection Tests**: `tests/Stripe.Extensions.DependencyInjection.Tests/`
  - Tests for `AddStripe()` extension methods
  - Tests for `StripeClient` lifecycle and keyed services
- **AspNetCore Tests**: `tests/Stripe.Extensions.AspNetCore.Tests/`
  - Tests for webhook handling and middleware
- **SourceGenerator Tests**: `tests/Stripe.Extensions.AspNetCore.SourceGenerators.Tests/`
  - Tests for code generation behavior
- **Aspire Tests**: `tests/Stripe.Hosting.Aspire.Tests/`
  - Tests for `AddStripeCli` and `AddStripeCliContainer` builder extension methods

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
- Library projects target **net8.0**, **net9.0**, and **net10.0** (multi-targeted)
- Symbol packages enabled with embedded sources for debugging
- Continuous integration detection: Sets `ContinuousIntegrationBuild=true` when running in GitHub Actions
- Non-packable projects (tests, samples) excluded from packing

## CI/CD (GitHub Actions)

The `.github/workflows/build.yml` workflow:
- **Triggers**: Push to `main`, or `workflow_dispatch` (manual) for publishing
- **Build job**: Runs on both `ubuntu-latest` and `macOS-latest` with .NET 10
- **Package job**: Creates NuGet packages on `ubuntu-latest`; uploads as workflow artifacts
- **Publish job**: Triggered only by `workflow_dispatch` from `main`; publishes to GitHub or NuGet based on user input (`packregistry` choice)
- Requires `fetch-depth: 0` for MinVer to compute version from full git history

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
- **v1 snapshot events**: `StripeWebhookHandler<T>` (uses `EventUtility.ConstructEvent` for signature verification), registered with `MapStripeWebhookHandler<T>` (default path: `/stripe/webhook`). Supports an optional `namedConfiguration` parameter to use a non-default named client
- **v2 thin events**: DI-registered subscribers implementing `IStripeEventSubscriber<TNotification>`, registered with `AddStripeEventSubscriber<T>()` and mapped with `MapStripeEventNotifications`. Built on the Stripe.net SDK's own `StripeEventNotificationHandler`
- **`StripeThinEventHandler<T>` / `MapStripeThinEventHandler<T>` are `[Obsolete]`** — superseded by the subscriber model. Do not use in new code or docs; they cover 20 event types where subscribers cover 24
- **`StripeWebhookContext`**: injected into handlers, provides `HttpContext`, `StripeOptions`, `StripeClient`, and `ILoggerFactory`
- **`IStripeWebhookExecutor`**: internal interface; both handler bases implement it for route dispatch
- Automatic webhook signature verification using `StripeOptions.WebhookSecret`
- Validates incoming webhook requests before processing; returns `400 Bad Request` on signature errors, `500` on handler exceptions

### Source Generators (`Stripe.Extensions.AspNetCore.SourceGenerators`)
- Generates type-safe webhook handler base classes from OpenAPI spec
- Contains two generators: `StripeWebhookHandlerGenerator` (v1 events) and `StripeThinEventHandlerGenerator` (v2 thin events)
- Automatically fetches latest Stripe API schema (OpenAPI 3.0 format)
- Generates handler method stubs for all Stripe event types
- Improves developer experience with IntelliSense and type safety
- Spec location: `src/Stripe.Extensions.AspNetCore.SourceGenerators/stripeapi.spec3.sdk.json`
- Referenced by `Stripe.Extensions.AspNetCore.csproj` as `OutputItemType="Analyzer"` (not a runtime dependency)

### Aspire Hosting Integration (`Stripe.Hosting.Aspire`)
- Provides .NET Aspire AppHost extensions to run the Stripe CLI alongside app services during local development
- Two modes: **local CLI** (`AddStripeCli`) and **Docker container** (`AddStripeCliContainer`)
- Extension methods `WithWebhookForwardTo` (maps `--forward-to`) and `WithWebhookConnectForwardTo` (maps `--forward-connect-to`) for Stripe Connect
- Supports `skipVerify: true` for self-signed local HTTPS certs
- Supports forwarding to multiple endpoints simultaneously
- After startup, extracts the `whsec_...` webhook signing secret from CLI stdout output
- **`WithReference(stripeResource)`** injects environment variables into dependent services:
  - `STRIPE_SECRET_KEY` — the secret API key
  - `STRIPE_PUBLISHABLE_KEY` — publishable key (if provided)
  - `STRIPE_WEBHOOK_SECRET` — signing secret captured from CLI startup
  - `Stripe__Default__ApiKey` — maps to `Stripe:Default:ApiKey` for `AddStripe()` config binding
  - `Stripe__Default__PublicKey` — maps to `Stripe:Default:PublicKey`
  - `Stripe__Default__WebhookSecret` — maps to `Stripe:Default:WebhookSecret`
- Use `WaitFor(stripeCliResource)` to ensure the signing secret is captured before the dependent service starts
- Docker container mode uses `docker.io/stripe/stripe-cli:v1.33.0`; on Linux adds `--add-host=host.docker.internal:host-gateway` automatically

## Key Conventions

**Dependency Injection**:
- `AddStripe()` returns `IStripeClientBuilder` (extends `IHttpClientBuilder`) — use it to further configure the underlying `HttpClient` (e.g., add delegating handlers, set timeouts)
- Prefer `StripeClient` (concrete class) over `IStripeClient` interface for injection
- For named/keyed clients, use `[FromKeyedServices("clientName")]` attribute in constructors
- Keyed services registered with `AddStripe("clientName")` in service collection
- The default client name is `"Default"` (constant `StripeOptions.DefaultClientConfigurationSectionName`)

**Configuration**:
- Configuration bound from the `Stripe` section (e.g., `Stripe:Default`, `Stripe:ClientName`)
- `StripeOptions` class holds all configuration values:
  - `ApiKey` / `SecretKey` (alias) — Stripe secret key
  - `PublicKey` — Stripe publishable key
  - `WebhookSecret` — webhook signing secret
  - `WebhookTimestampTolerance` — seconds tolerance for webhook timestamps (default: `300`)
  - `ThrowOnWebhookApiVersionMismatch` — throw if event API version doesn't match (default: `true`)
  - `EnableTelemetry` — enable Stripe SDK telemetry (default: `true`)
  - `MaxNetworkRetries` — max HTTP retries (default: `SystemNetHttpClient.DefaultMaxNumberRetries`)
  - `AppInfo` — auto-set from assembly name/version; identifies this extension library to Stripe
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
- For **v1 events**: inherit from `StripeWebhookHandler<T>`, register with `MapStripeWebhookHandler<T>("/path")`
- For **v2 thin events**: implement `IStripeEventSubscriber<TNotification>`, register with `AddStripeEventSubscriber<T>()`, and map once with `MapStripeEventNotifications("/path")`
- For v1 handlers, override specific `On*Async` methods (e.g., `OnCustomerCreatedAsync`) instead of a generic handle method
- Override `UnknownEventAsync` to handle events not covered by generated overrides
- Dependencies injected via constructor DI (in addition to the required `StripeWebhookContext` parameter)
- Handler methods are async: `Task OnEventTypeAsync(EventType @event)`
- Handler methods receive the deserialized event object with full type information
- Route registered with `MapStripeWebhookHandler<THandler>(pattern)` in ASP.NET Core
- `WebhookSecret` must be set in `StripeOptions` — throws `InvalidOperationException` at request time if missing

**Partial Classes & Source Generators**:
- `StripeWebhookHandler<T>` and the obsolete `StripeThinEventHandler<T>` are declared `partial` — the source generator contributes `ExecuteAsync` and all `On*Async` method stubs
- **User-defined handler classes do NOT need to be partial** — only the base classes are partial
- Generated method naming: dot/underscore-separated event names are title-cased and wrapped: `payment_intent.created` → `OnPaymentIntentCreatedAsync`
- Thin event bracket notation: `[requirements]` → `IncludingRequirements`, e.g. `v2.core.account[requirements].updated` → `OnV2CoreAccountIncludingRequirementsUpdatedAsync`

**Namespaces**:
- DI extension methods live in `Microsoft.Extensions.DependencyInjection` namespace (not the library's own namespace) — enables auto-discovery without extra `using`
- Aspire extension methods live in `Aspire.Hosting` namespace — auto-discovered when the package is referenced in an AppHost
- Library namespaces: `Stripe.Extensions.DependencyInjection`, `Stripe.Extensions.AspNetCore`, `Stripe.Hosting.Aspire`

**Naming Conventions**:
- Extension classes: `{Feature}Extensions` (e.g., `StripeServiceCollectionExtensions`, `StripeAppBuilderExtensions`, `StripeCliBuilderExtensions`)
- Resources (Aspire): `{Feature}Resource` / `{Feature}ContainerResource`
- Builders: `{Feature}Builder`; Generators: `{Class}Generator`
- One public type per file; flat namespace structure (no nesting)

**Testing**:
- Mocking library: **FakeItEasy** (used in `Stripe.Extensions.AspNetCore.Tests`)
- Integration testing: `Microsoft.AspNetCore.TestHost`
- Source generator testing: `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit`

**Configuration Layering** (in order of precedence):
1. Default `StripeOptions` values (hardcoded in class)
2. Configuration section binding (`Stripe:{clientName}:*` in appsettings)
3. `PostConfigure` delegate passed to `AddStripe(o => ...)` — consumer can override anything

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
