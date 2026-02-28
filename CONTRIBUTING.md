# Contributing to Stripe.NET Extensions

Thank you for your interest in contributing to Stripe.NET Extensions!

## Development Setup

### Prerequisites
- .NET 10.0 SDK or later
- Git
- Just (optional, but recommended)

### Installing Just
Just is a command runner that simplifies build automation. Install it via:

**macOS/Linux:**
```bash
brew install just
```

**Windows:**
- Download from [just.systems](https://just.systems/)
- Or use WSL/Git Bash
- Or use `choco install just` if you have Chocolatey

**Verify installation:**
```bash
just --version
```

## Building

### Using Just (Recommended)
```bash
# List all available commands
just

# Build the solution
just build

# Run tests
just test

# Create packages
just pack

# Clean build artifacts
just clean
```

### Using dotnet CLI directly
```bash
dotnet build
dotnet test
dotnet pack
```

## Testing

### Run all tests
```bash
just test
```

### Run a specific test
```bash
just test-filter "FullyQualifiedName~TestClassName.TestMethodName"
```

Example:
```bash
just test-filter "Stripe.Extensions.DependencyInjection.Tests.ServiceCollectionExtensionsTest.CanResolveStripeOptions"
```

### Run tests with coverage
```bash
just test-coverage
```

## Project Structure

```
.
├── src/                                          # Source projects
│   ├── Stripe.Extensions.DependencyInjection/
│   ├── Stripe.Extensions.AspNetCore/
│   └── Stripe.Extensions.AspNetCore.SourceGenerators/
├── tests/                                        # Test projects
│   ├── Stripe.Extensions.DependencyInjection.Tests/
│   ├── Stripe.Extensions.AspNetCore.Tests/
│   └── Stripe.Extensions.AspNetCore.SourceGenerators.Tests/
├── samples/                                      # Sample applications
│   └── SampleCheckout/
├── justfile                                      # Build automation
└── Stripe.Extensions.sln                         # Solution file
```

## Code Style

- Follow standard C# naming conventions (PascalCase for classes/methods, camelCase for variables)
- Use IDE0005+ code analysis rules (enabled by default)
- Use `async`/`await` patterns where appropriate
- Add XML documentation comments for public APIs

## Pull Request Process

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Run tests: `just test`
5. Commit with clear messages
6. Push to your fork
7. Open a Pull Request

## Versioning

Versions are managed automatically using [MinVer](https://github.com/adamralph/minver). 

- Versions are derived from git tags
- Pre-release versions are automatically generated from commits since the last tag
- No manual version bumping needed—create a git tag to release a new version

## CI/CD

GitHub Actions automatically:
- Builds on push to main
- Runs all tests across Windows, macOS, and Linux
- Creates NuGet packages
- Publishes packages when triggered manually

## Questions?

Open an issue or discussion in the repository. We're here to help!
