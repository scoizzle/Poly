# File-Based C# Apps (Quick Standalone Tests)

File-based apps are single `.cs` files that can be built and executed with `dotnet <file>.cs` — no `.csproj`, no solution, no project scaffolding needed.

They are ideal when you need to **quickly test an idea in isolation** from the existing Poly projects (e.g. validating a parsing snippet, prototyping an algorithm, reproducing a runtime behavior).

## When to use

- Prototyping a stand-alone algorithm or snippet before integrating into Poly.
- Reproducing a bug or behavior in complete isolation.
- Experimenting with a NuGet package without wiring it into the main build.

For anything that needs to reference `Poly` types or run against the existing test infrastructure, use `Poly.Tests/` instead.

## Basic usage

```bash
# Create a single .cs file
echo 'Console.WriteLine("Hello from file-based app!");' > test.cs

# Run it — no project file needed
dotnet test.cs
```

The SDK caches the build; subsequent runs skip the build if the source is unchanged.

## Adding NuGet packages

Use the `#:package` directive:

```csharp
#:package Some.Package@1.0.0
```

## Unix shebang

On Unix/macOS, make the file executable and add a shebang:

```csharp
#!/usr/bin/env dotnet
```

Then `./test.cs` works directly.

## Structure rules

- Everything must live in one `.cs` file (no multi-file projects).
- Top-level statements, local functions, and type declarations at the bottom all work.
- No `csproj` settings — TFM defaults to `net10.0`, nullable is enabled.
