# Just Build Automation

# Directories
SOURCE_DIR := "src"
SG_DIR := "src/Stripe.Extensions.AspNetCore.SourceGenerators"
TESTS_DIR := "tests"
SAMPLES_DIR := "samples"
ARTIFACTS_DIR := "artifacts"
PACKAGES_DIR := ARTIFACTS_DIR / "packages"

# Default shell for recipes (bash with error handling)
set shell := ["bash", "-c"]

# Extract version from MinVer
version := `dotnet tool run minver --default-pre-release-identifiers preview`

# OpenAPI spec settings for source generator
OPENAPI_URL := "https://raw.githubusercontent.com/stripe/openapi/refs/heads/master/latest/openapi.spec3.sdk.json"
SG_SPEC := SG_DIR / "stripeapi.spec3.sdk.json"

# Display all available recipes
default:
    @just --list

# ==============================================================================
# INFO & HELP
# ==============================================================================

# Print build information and metadata
info:
    #!/usr/bin/env bash
    echo "=== Stripe.NET Extensions - Build Information ==="
    echo ""
    echo "Solution Path: Stripe.Extensions.sln"
    echo "Solution Directory: $(pwd)"
    echo ""
    dotnet --version
    echo ""
    echo "Available recipes:"
    just --list | tail -n +2
    echo ""

# ==============================================================================
# CLEAN TARGETS
# ==============================================================================

# Clean all build artifacts
clean: clean-source clean-tests clean-samples
    @mkdir -p {{ ARTIFACTS_DIR }}
    @rm -rf {{ ARTIFACTS_DIR }}/*
    @echo "✓ All artifacts cleaned"

# Clean source project directories
clean-source:
    @find {{ SOURCE_DIR }} -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
    @echo "✓ Source directories cleaned"

# Clean test project directories
clean-tests:
    @find {{ TESTS_DIR }} -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
    @echo "✓ Test directories cleaned"

# Clean sample project directories
clean-samples:
    @find {{ SAMPLES_DIR }} -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
    @echo "✓ Sample directories cleaned"

# ==============================================================================
# OPENAPI SOURCE GENERATOR
# ==============================================================================

# Internal: core fetch logic. Parameter `validate` should be "true" or "false".
[private]
fetch-openapi-core validate:
    @echo "Fetching latest Stripe OpenAPI spec..." && \
    mkdir -p {{ SG_DIR }} && \
    TMP="$(mktemp)" && \
    if command -v curl >/dev/null 2>&1; then \
        curl -fsSL "{{ OPENAPI_URL }}" -o "$TMP"; \
    else \
        wget -qO "$TMP" "{{ OPENAPI_URL }}"; \
    fi && \
    echo "Downloaded to $TMP" && \
    if [ "{{ validate }}" = "true" ]; then \
        echo "Validating JSON with jq..." && \
        if command -v jq >/dev/null 2>&1; then \
            jq -e 'has("openapi") and (has("paths") or has("components"))' "$TMP" >/dev/null || { echo "❌ JSON validation failed (jq)"; rm -f "$TMP"; exit 2; }; \
        else \
            echo "❌ jq is required for validation but not installed. Please install jq or run the no-validate fetch."; rm -f "$TMP"; exit 2; \
        fi; \
    fi && \
    mv "$TMP" "{{ SG_SPEC }}" && \
    echo "✓ Updated {{ SG_SPEC }}"

# Public wrappers
# fetch-openapi-with-validation: validates using jq
fetch-openapi-with-validation:
    @just -q fetch-openapi-core true

# fetch-openapi: backward-compatible no-validation fetch (unsafe)
fetch-openapi:
    @just -q fetch-openapi-core false

# ==============================================================================
# RESTORE & BUILD
# ==============================================================================

# Restore NuGet packages
restore:
    @echo "Restoring NuGet packages..."
    @dotnet restore

# Build the solution (Release configuration)
build: restore
    @echo "Building Stripe.Extensions solution..."
    @dotnet build --configuration Release --no-restore

# Build the solution (Debug configuration - default)
build-debug: restore
    @echo "Building Stripe.Extensions solution (Debug)..."
    @dotnet build --configuration Debug --no-restore

# ==============================================================================
# TEST & VALIDATION
# ==============================================================================

# Run all unit tests
test: build
    @echo "Running tests..."
    @dotnet test --configuration Release --no-build --verbosity normal

# Run tests with coverage (requires coverlet if you have it installed)
test-coverage: build
    @echo "Running tests with coverage..."
    @dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"

# Run a specific test by filter (usage: just test-filter "FullyQualifiedName~YourTest")
test-filter filter: build
    @echo "Running filtered test: {{ filter }}"
    @dotnet test --configuration Release --no-build --filter "{{ filter }}"

# ==============================================================================
# PACKAGE & PUBLISH
# ==============================================================================


# Create NuGet packages (Release configuration)
pack: clean build
    @echo "Creating NuGet packages with version: {{ version }}"
    @mkdir -p {{ PACKAGES_DIR }}
    @dotnet pack --configuration Release \
        --no-build \
        --output {{ PACKAGES_DIR }} \
        -p:Version={{ version }} \
        -p:RepositoryUrl="https://github.com/cecilphillip-stripe/stripe-dotnet-extensions"
    @echo ""
    @echo "✓ Packages created:"
    @ls -lh {{ PACKAGES_DIR }}/*.nupkg || true

# Publish packages to NuGet.org (requires NUGET_API_KEY environment variable)
publish-nuget: pack
    @echo "Publishing to NuGet.org..."
    @if [ -z "$${NUGET_API_KEY:-}" ]; then \
        echo "❌ NUGET_API_KEY environment variable is not set"; \
        exit 1; \
    fi
    @dotnet nuget push {{ PACKAGES_DIR }}/*.nupkg \
        --source https://api.nuget.org/v3/index.json \
        --api-key "$${NUGET_API_KEY}" \
        --skip-duplicate
    @echo "✓ Packages published to NuGet.org"

# Publish packages to GitHub Package Registry (requires NUGET_GITHUB_TOKEN environment variable)
publish-github: pack
    @echo "Publishing to GitHub Package Registry..."
    @if [ -z "$${NUGET_GITHUB_TOKEN:-}" ]; then \
        echo "❌ NUGET_GITHUB_TOKEN environment variable is not set"; \
        exit 1; \
    fi
    @dotnet nuget push {{ PACKAGES_DIR }}/*.nupkg \
        --source https://nuget.pkg.github.com/cecilphillip-stripe/index.json \
        --api-key "$${NUGET_GITHUB_TOKEN}"
    @echo "✓ Packages published to GitHub"

# ==============================================================================
# CONTINUOUS INTEGRATION / SHORTCUTS
# ==============================================================================

# Alias for build (commonly used)
compile: build

# Alias for test (commonly used)
verify: test

# Build and test (quick validation)
validate: build test
    @echo "✓ Validation passed"

# Full CI pipeline: clean, build, test, pack
ci: clean build test pack
    @echo "✓ CI pipeline completed successfully"
