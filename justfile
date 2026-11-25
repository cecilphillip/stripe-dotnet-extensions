# Directories
SOURCE_DIR := "src"
SG_DIR := "src/Stripe.Extensions.AspNetCore.SourceGenerators"
TESTS_DIR := "tests"
SAMPLES_DIR := "samples"
ARTIFACTS_DIR := "artifacts"
PACKAGES_DIR := ARTIFACTS_DIR / "packages"
PRERELEASE := "preview"

set shell := ["bash", "-c"]

# OpenAPI spec
OPENAPI_URL := "https://raw.githubusercontent.com/stripe/openapi/refs/heads/master/latest/openapi.spec3.sdk.json"
SG_SPEC := SG_DIR / "stripeapi.spec3.sdk.json"

# List recipes
default:
    @just --list

# Show build metadata
info:
    #!/usr/bin/env bash
    set -e
    version="$(dotnet tool run minver --default-pre-release-identifiers {{ PRERELEASE }} --verbosity error)"
    echo "Solution:      Stripe.Extensions.sln"
    echo "Packages:      Stripe.Extensions.DependencyInjection"
    echo "               Stripe.Extensions.AspNetCore"
    echo "               Stripe.Hosting.Aspire"
    echo "Version:       $version"
    echo "Prerelease:    {{ PRERELEASE }}"
    echo "Configuration: Release"
    echo "Artifacts:     {{ PACKAGES_DIR }}"

# Clean build artifacts
clean:
    @find {{ SOURCE_DIR }} {{ TESTS_DIR }} {{ SAMPLES_DIR }} -type d \( -name "bin" -o -name "obj" \) -prune -exec rm -rf {} +
    @mkdir -p {{ ARTIFACTS_DIR }}
    @rm -rf {{ ARTIFACTS_DIR }}/*
    @echo "✓ All artifacts cleaned"

# Download OpenAPI spec; pass true to validate with jq
fetch-openapi validate="false":
    #!/usr/bin/env bash
    set -euo pipefail
    validate="{{ validate }}"
    if [[ "$validate" != "true" && "$validate" != "false" ]]; then
        echo "Validation must be true or false." >&2
        exit 2
    fi
    if [[ "$validate" == "true" ]] && ! command -v jq >/dev/null 2>&1; then
        echo "jq is required for validation." >&2
        exit 2
    fi
    echo "Fetching latest Stripe OpenAPI spec..."
    mkdir -p {{ SG_DIR }}
    tmp="$(mktemp)"
    trap 'rm -f "$tmp"' EXIT
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "{{ OPENAPI_URL }}" -o "$tmp"
    else
        wget -qO "$tmp" "{{ OPENAPI_URL }}"
    fi
    if [[ "$validate" == "true" ]]; then
        echo "Validating JSON with jq..."
        jq -e 'has("openapi") and (has("paths") or has("components"))' "$tmp" >/dev/null
    fi
    mv "$tmp" "{{ SG_SPEC }}"
    echo "✓ Updated {{ SG_SPEC }}"

# Build (Release by default; pass Debug for a debug build)
build configuration="Release":
    @echo "Building Stripe.Extensions solution ({{ configuration }})..."
    @dotnet build --configuration "{{ configuration }}"

# Run all tests
test: build
    @echo "Running tests..."
    @dotnet test --configuration Release --no-build --verbosity normal

# Run tests with coverage
test-coverage: build
    @echo "Running tests with coverage..."
    @dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"

# Run tests matching a filter
test-filter filter: build
    @echo "Running filtered test: {{ filter }}"
    @dotnet test --configuration Release --no-build --filter "{{ filter }}"

# Compile documentation samples
verify-docs: build
    @echo "Verifying documentation samples..."
    @dotnet test {{ TESTS_DIR }}/Stripe.Extensions.Docs.Tests/Stripe.Extensions.Docs.Tests.csproj \
        --configuration Release --no-build --verbosity normal

# Compile samples in a release-notes file
verify-notes FILE: build
    @echo "Verifying samples in {{ FILE }}..."
    @DOCS_VERIFY_EXTRA_FILES="$(cd "$(dirname {{ FILE }})" && pwd)/$(basename {{ FILE }})" \
        dotnet test {{ TESTS_DIR }}/Stripe.Extensions.Docs.Tests/Stripe.Extensions.Docs.Tests.csproj \
        --configuration Release --no-build --verbosity normal

# Create Release NuGet packages
pack: clean build
    #!/usr/bin/env bash
    set -euo pipefail
    version="$(dotnet tool run minver --default-pre-release-identifiers {{ PRERELEASE }})"
    echo "Creating NuGet packages with version: $version"
    mkdir -p {{ PACKAGES_DIR }}
    dotnet pack --configuration Release \
        --no-build \
        --output {{ PACKAGES_DIR }} \
        -p:Version="$version" \
        -p:RepositoryUrl="https://github.com/cecilphillip/stripe-dotnet-extensions"
    echo ""
    echo "✓ Packages created:"
    ls -lh {{ PACKAGES_DIR }}/*.nupkg || true

# Build and publish to NuGet.org (requires NUGET_API_KEY)
publish-nuget: pack
    @just -q push-nuget

# Push existing packages to NuGet.org (requires NUGET_API_KEY)
push-nuget:
    #!/usr/bin/env bash
    set -euo pipefail
    echo "Publishing to NuGet.org..."
    if [ -z "${NUGET_API_KEY:-}" ]; then
        echo "❌ NUGET_API_KEY environment variable is not set"
        exit 1
    fi
    # Matching symbol packages are pushed automatically.
    for pkg in {{ PACKAGES_DIR }}/*.nupkg; do
        dotnet nuget push "$pkg" \
            --source https://api.nuget.org/v3/index.json \
            --api-key "${NUGET_API_KEY}" \
            --skip-duplicate
    done
    echo "✓ Packages published to NuGet.org"

# Clean, build, test, and pack
ci: clean build test pack
    @echo "✓ CI pipeline completed successfully"

# Verify a release from a clean tree; see RELEASING.md
release-check:
    #!/usr/bin/env bash
    set -euo pipefail
    version="$(dotnet tool run minver --default-pre-release-identifiers preview)"

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
        -p:Version="$version" \
        -p:RepositoryUrl="https://github.com/cecilphillip/stripe-dotnet-extensions"

    echo "=== 5/5 Working tree ==="
    # Ensure the verified files still match the commit to be tagged.
    if [ -n "$(git status --porcelain)" ]; then
        echo "❌ Working tree is dirty after a full verification run:"
        git status --short
        exit 1
    fi

    echo ""
    echo "✓ release-check passed for version $version"
    echo "  Packages: $(ls {{ PACKAGES_DIR }}/*.nupkg | wc -l | tr -d ' ')"
    echo "  Commit:   $(git rev-parse --short HEAD)"
