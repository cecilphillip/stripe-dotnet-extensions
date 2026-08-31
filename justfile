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

# Point git at the tracked hooks in .githooks (runs release-check before pushing a tag)
install-hooks:
    @git config core.hooksPath .githooks
    @chmod +x .githooks/*
    @echo "✓ git hooks installed from .githooks"

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

# Compile every C# sample in the documentation against the real assemblies
verify-docs: build
    @echo "Verifying documentation samples..."
    @dotnet test {{ TESTS_DIR }}/Stripe.Extensions.Docs.Tests/Stripe.Extensions.Docs.Tests.csproj \
        --configuration Release --no-build --verbosity normal

# Compile the C# samples in a release-notes file before publishing it.
# Release notes are published to GitHub, so they are not covered by `verify-docs`.
# Usage: just verify-notes notes.md
verify-notes FILE: build
    @echo "Verifying samples in {{ FILE }}..."
    @DOCS_VERIFY_EXTRA_FILES="$(cd "$(dirname {{ FILE }})" && pwd)/$(basename {{ FILE }})" \
        dotnet test {{ TESTS_DIR }}/Stripe.Extensions.Docs.Tests/Stripe.Extensions.Docs.Tests.csproj \
        --configuration Release --no-build --verbosity normal

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
        -p:RepositoryUrl="https://github.com/cecilphillip/stripe-dotnet-extensions"
    @echo ""
    @echo "✓ Packages created:"
    @ls -lh {{ PACKAGES_DIR }}/*.nupkg || true

# Publish packages to NuGet.org — builds first (requires NUGET_API_KEY environment variable)
publish-nuget: pack
    @just -q push-nuget

# Push already-built packages to NuGet.org — skips pack (requires NUGET_API_KEY environment variable)
push-nuget:
    #!/usr/bin/env bash
    set -euo pipefail
    echo "Publishing to NuGet.org..."
    if [ -z "${NUGET_API_KEY:-}" ]; then
        echo "❌ NUGET_API_KEY environment variable is not set"
        exit 1
    fi
    # dotnet nuget push automatically uploads the matching .snupkg alongside each .nupkg
    for pkg in {{ PACKAGES_DIR }}/*.nupkg; do
        dotnet nuget push "$pkg" \
            --source https://api.nuget.org/v3/index.json \
            --api-key "${NUGET_API_KEY}" \
            --skip-duplicate
    done
    echo "✓ Packages published to NuGet.org"

# Publish packages to GitHub Package Registry — builds first (requires NUGET_GITHUB_TOKEN environment variable)
publish-github: pack
    @just -q push-github

# Push already-built packages to GitHub Package Registry — skips pack (requires NUGET_GITHUB_TOKEN environment variable)
push-github:
    #!/usr/bin/env bash
    set -euo pipefail
    echo "Publishing to GitHub Package Registry..."
    if [ -z "${NUGET_GITHUB_TOKEN:-}" ]; then
        echo "❌ NUGET_GITHUB_TOKEN environment variable is not set"
        exit 1
    fi
    for pkg in {{ PACKAGES_DIR }}/*.nupkg; do
        dotnet nuget push "$pkg" \
            --source https://nuget.pkg.github.com/cecilphillip-stripe/index.json \
            --api-key "${NUGET_GITHUB_TOKEN}" \
            --skip-duplicate
    done
    echo "✓ Packages published to GitHub"

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

# ==============================================================================
# RELEASE GATE
# ==============================================================================

# Must pass before tagging a release. See RELEASING.md for the full checklist.
#
# Starts from clean on purpose: a stale obj/ directory once hid a dropped
# ProjectReference through a green `just build` and `just test`, and only
# surfaced during `just pack`.
release-check:
    #!/usr/bin/env bash
    set -euo pipefail

    echo "=== 1/5 Clean ==="
    just -q clean

    echo "=== 2/5 Build (warnings are errors) ==="
    dotnet restore
    dotnet build --configuration Release --no-restore -warnaserror

    echo "=== 3/5 Tests, including documentation samples ==="
    dotnet test --configuration Release --no-build --verbosity normal

    echo "=== 4/5 Pack ==="
    mkdir -p {{ PACKAGES_DIR }}
    dotnet pack --configuration Release --no-build \
        --output {{ PACKAGES_DIR }} \
        -p:Version={{ version }} \
        -p:RepositoryUrl="https://github.com/cecilphillip/stripe-dotnet-extensions"

    echo "=== 5/5 Working tree ==="
    # Packing regenerates files, and an editor with stale buffers has silently
    # reverted tracked files after a commit before. Anything dirty here means the
    # bytes about to be tagged are not the bytes that were just verified.
    if [ -n "$(git status --porcelain)" ]; then
        echo "❌ Working tree is dirty after a full verification run:"
        git status --short
        exit 1
    fi

    echo ""
    echo "✓ release-check passed for version {{ version }}"
    echo "  Packages: $(ls {{ PACKAGES_DIR }}/*.nupkg | wc -l | tr -d ' ')"
    echo "  Commit:   $(git rev-parse --short HEAD)"
