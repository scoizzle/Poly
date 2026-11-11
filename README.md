# Poly

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](global.json)

Deterministic code generation from non-deterministic sources.

Domain models, discovered heuristics, and natural-language specifications all express *what could be*. Poly's analysis, lowering, and VM pipeline determines *what actually happens* — validating constraints, resolving ambiguities, expanding macros, and producing executable code through a canonical stack VM. The source is the ground truth; the pipeline turns intention into provably correct behavior.

## Key Concepts

- **Domain-first.** The `.poly` DSL, entities, policies, stages, and actions are the ground truth. Tools, languages, and infrastructure are evaluated by how faithfully they express the domain.
- **Analysis pipeline.** A multi-pass analyzer validates types, scopes, control flow, side effects, and structural well-formedness before anything executes. Node rewrites happen in analysis metadata — never by mutating the tree.
- **Canonical VM.** Every lowered program's meaning is determined by execution on a stack VM — the single source of truth for behavior. The same AST compiles to LINQ expressions, C# source, or runs directly through the VM.
- **Domain as library.** A domain is a library of legal operations, not a process with a required `Main`. Product entry points (REST, HTTP) are opt-in extensions loaded via `uses`.
- **Interactive harness.** The MCP server lets agents author, inspect, and simulate domain models — evaluating policies and invoking actions against runtime instances.
- **Ontology terms.** Entities, properties, and enum members carry `iri`, `label`, `description`, and `term` metadata in `.poly`; the JSON Schema export keeps the model linked to its ontology instead of flattening it to a bare schema.

## Repository Layout

```
Poly/                      Core library (zero external dependencies)
  Ast/                     Node records, NodeId, fluent construction API
  Analysis/                Analysis framework, metadata, node replacement
  Interpretation/          Semantic passes, VM runtime, C# code generation
    Analysis/              Type resolution, scope validation, CFG, folding
    Vm/                    Stack VM, DirectVmAbiEmitter, heap/ring ABI
    CSharp/                C# source code generation
  DomainModeling/          Domain model, evolution, DE→AST lowering, contracts
  Introspection/           Platform-agnostic type/member model + CLR provider
  Grammar/                 Pattern-table grammar engine (parse, match, print)
  Extensions/              Shared helpers

Poly.Tests/                Test suite (TUnit)
Poly.Mcp/                  MCP interactive harness for agents
Poly.Benchmarks/           BenchmarkDotNet benchmarks
Poly.Runtime/              BCL-only contract the emitted C# names (storage port, constraint failure)

src/                       Extension packs
  Poly.DslCompiler/        DSL compiler
  Poly.Packs.Product/      Product door set (packs + HTTP), the MCP catalog
  Poly.Packs.Sqlite/       SQLite persistence pack
  Poly.Packs.SqlServer/    SQL Server persistence pack
  Poly.Packs.MySql/        MySQL persistence pack

demo/                      Demo applications
  Poly.RestApi/            REST API demo

Poly.Demo/                 MAUI + Blazor IDE demo (Poly.Demo.Shared is in Poly.slnx;
                           the MAUI host needs the maui workload and is not gated)
  Poly.Demo.Shared/        Razor IDE/REPL pages and the in-browser compiler
  Poly.Demo/               MAUI host
  Poly.Demo.Web/           Blazor Web host
```

## Quick Start

### Define a domain with the DSL

```poly
domain Orders

Customer: entity {
  Name: Text
  Email: Text unique
  Places: many Order
}

Order: entity {
  Total: Number
  Draft: stage {
    Submit: action { transition to Active }
  }
  Active: stage {}
}
```

### Check and build with polyc

```bash
dotnet run --project src/polyc -- check Orders.poly
dotnet run --project src/polyc -- build Orders.poly --out ./polyc-out
```

`polyc check` runs the same pipeline as `build` (load, parse, evolve, analyze, lower) and writes
nothing. `build` writes `<out>/<DomainName>/`. A directory means every `*.poly` directly in it,
each its own compilation unit; with no path and piped stdin, stdin is one document.

- **Exit codes:** `0` no errors, `1` errors or usage, `2` unexpected failure. Warnings do not fail
  unless `--warn-as-error`.
- **`--format json`** prints a JSON array of every diagnostic record (file, line, column,
  endLine, endColumn, severity, code, message, path, placed). It is not filtered. A build also
  prints each written path on stderr as `wrote <path>`. Text shows errors and warnings, and
  `--verbosity info|hint` shows more. Codes: `DSL0001` parse, `DSL0002` unknown extension,
  `DSL0003` duplicate extension, `DSL0004` lower or emit refused; analyzer codes pass through.
- **`uses` resolution:** the document's own `uses` wins. With none, `--seed authoring` (default,
  the storage annotations) or `--seed language` supplies the extension ids. An unknown id is
  `DSL0002` on its header token.
- **`--target <id>`** is the host target (for example `posix` or `windows`) forwarded to the
  compile. It is a host fact, never read from a `uses` line. A package that declares targets
  (for example `uses posix`) is `DMCON004` when the host target is absent or not one the package
  declares; the target does not change the surface fingerprint. `polyc build` and `polyc check`
  are the only doors that carry a target in this slice; `polyc lsp` and the MCP tools name none,
  so `uses posix` is refused there until they forward one.
- **`polyc lsp`** serves diagnostics, hover, go-to-definition and document symbols over stdio;
  its diagnostics are the `check --format json` records. A malformed notification is reported and
  the server keeps serving; a broken frame exits 2. **`polyc mcp`** serves the seven tools below.
- **Install:** `dotnet pack src/polyc/polyc.csproj` then
  `dotnet tool install --tool-path .tools --add-source <nupkg-dir> Poly.Polyc`, or
  `dotnet publish src/polyc/polyc.csproj -c Release -p:PublishSingleFile=true` for a single file.
  Framework-dependent is enough; there is no native AOT build.

### Build and run

```bash
# Build
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj

# Run tests (TUnit)
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

### Interactive playground

The Blazor IDE/REPL (`Poly.Demo.Web`) compiles and runs Poly snippets in the browser:

```bash
dotnet run --project Poly.Demo/Poly.Demo.Web/Poly.Demo.Web.csproj
```

For a human-readable view of an authored domain, `export_domain_to_html` produces a
single self-contained HTML document (no server, no scripts) from the same facts the
machine exports read.

For the full DSL syntax reference, see [`Poly.Mcp/Docs/poly-dsl-guide.md`](Poly.Mcp/Docs/poly-dsl-guide.md).

## MCP Tools

The MCP server (`polyc mcp`; `dotnet run --project Poly.Mcp` still launches the same host for one release) provides **seven outcome tools** for agents to describe the domain they are working in, prove it, and produce what the task needs:

- **Describe:** `poly_guide`, `poly_workspace`, `poly_define`.
- **Prove:** `poly_check` (`proven` / `violated` / `unknown`).
- **Read:** `poly_inspect`.
- **Produce:** `poly_export` (dsl, JSON Schema, C#, Markdown, HTML, JSON-LD, SHACL) and `poly_simulate` (ordered scenario with a trace).

See [`Poly.Mcp/README.md`](Poly.Mcp/README.md) for the full contract.

See [`Poly.Mcp/README.md`](Poly.Mcp/README.md) for the full tool surface and usage details.

## Architecture

- **[`docs/CORE.md`](docs/CORE.md)** — Platform map: module boundaries, critical machinery, "use this / not that"
- **[`docs/decisions/`](docs/decisions/)** — Architectural decision records (ADRs)
- **[`AGENTS.md`](AGENTS.md)** — Agent and contributor conventions, placement rules, build/test instructions
- **[`docs/reference/`](docs/reference/)** — Design records and research notes; work is tracked in GitHub issues

## License

[MIT](LICENSE.txt)
