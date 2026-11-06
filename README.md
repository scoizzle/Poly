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

The MCP server (`Poly.Mcp`) provides 30 tools for agents to author, inspect, and simulate domain models in an interactive session. Two authoring paths:

- **Bulk:** `apply_dsl` — write a `.poly` document, apply in one shot; `validate_dsl` is the dry run that never touches the session.
- **Incremental:** `add` / `remove` — build or modify a model one element at a time.

Runtime tools let agents create instances, invoke actions, and observe state changes — all within the session. Read-only export tools hand the same model to other toolchains: `export_dsl`, `export_domain_to_csharp`, `export_domain_to_json_schema`, `export_domain_to_json_ld`, and `export_domain_to_shacl`; `get_domain_report` renders it as Markdown and `export_domain_to_html` as a self-contained page a person can open.

See [`Poly.Mcp/README.md`](Poly.Mcp/README.md) for the full tool surface and usage details.

## Architecture

- **[`docs/CORE.md`](docs/CORE.md)** — Platform map: module boundaries, critical machinery, "use this / not that"
- **[`docs/decisions/`](docs/decisions/)** — Architectural decision records (ADRs)
- **[`AGENTS.md`](AGENTS.md)** — Agent and contributor conventions, placement rules, build/test instructions
- **[`docs/plans/`](docs/plans/)** — Execution plans and work tracking

## License

[MIT](LICENSE.txt)
