# Poly Platform — Project Summary for Agents

**Date:** 2026-07-26  
**Purpose:** Quick onboarding for any agent new to the Poly codebase.

---

## What Poly Is

A **domain modeling platform** that turns a declarative DSL into analyzable domain models, runtime instances, and production-ready C# infrastructure code (EF Core DbContext, Minimal API, .http file).

```poly
domain Library
Book: entity { Title: Text required; ISBN: Text unique }
Patron: entity {
  loans: many Loan
  GoodStanding: policy { Status is "Active" }
  Active: stage {
    CheckOut: action (book: Book) -> Loan
      require GoodStanding
    { create in loans { book: book } }
  }
}
Loan: entity { book: Book; borrower: Patron; Active: stage { Return: action { } } }
```

## Key Architectural Principles

From [`AGENTS.md`](../AGENTS.md) — always read before non-trivial changes:

1. **Domain model is the key artifact** — tools serve domain expression, not fashion
2. **Placement rules** — module boundaries are strict one-way deps (Syntax → Interpretation, DomainModeling → Syntax, etc.)
3. **Go well to go fast** — smallest fix that passes a failing test; production gets simpler, tests get more specific
4. **Working code before abstractions** — no "for the future" interfaces without a second consumer

## Module Map

| Module | Concern | Key files |
|--------|---------|-----------|
| `Poly/Syntax/` | AST nodes, analysis framework, analysis pipeline | `Nodes/*.cs`, `Analysis/Analyzer.cs`, `Analysis/AnalyzerBuilder.cs`, `Analysis/INodeAnalyzer.cs` |
| `Poly/DomainModeling/` | Domain model records, DSL parsing, analysis passes, evolution, runtime | `Entity.cs`, `Domain.cs`, `Parsing/PolyDslParser.cs`, `Analysis/*.cs`, `Evolution/DomainEvolution.cs`, `DomainEntityInstance.cs`, `DomainInstanceStore.cs` |
| `Poly/Interpretation/` | Expression lowering, C# export, VM execution | `CSharp/`, `LinqExpressions/`, `Vm/` |
| `Poly/Introspection/` | CLR type system bridge | `TypeMember.cs`, `ClrTypeDefinitionRegistry.cs` |
| `Poly.Mcp/` | MCP server tools for agents | `Tools/RuntimeTool.cs`, `Tools/DomainTools.cs`, `Tools/OracleTool.cs`, `Sessions/McpSessionStore.cs` |
| `src/Poly.DslCompiler/` | CLI compiler (poly → C#) | `DslCompiler.cs`, `MinimalApiGenerator.cs`, `DbContextGenerator.cs` |
| `src/Poly.Packs.Sqlite/` | Sqlite pack (type maps, conventions) | `SqliteDefaults.cs` |

## Pipeline Flow

```
.poly DSL → PolyDslParser → DomainEvolution (apply changes, gate by analysis)
  → DomainModelAnalyzer.Analyze (18-pass analysis pipeline)
    → metadata: topology, aggregate, behavior, storage, transport, ...
  → DslCompiler.Compile (emits _all.cs, DbContext.cs, Program.cs, demo.http)
```

MCP path:
```
apply_dsl → create_instance → link_instances → invoke_action → evaluate_policy
```

## What's Shipped (1637 tests passing)

### Domain Modeling
- DSL parsing (entities, enums, properties with constraints, navigation properties, stages, actions, policies, effects, `create in`, subscriptions)
- Evolution with analysis rollback gating
- 18-pass domain analysis pipeline (structural, semantic, effect, capability, ownership, storage, transport, etc.)
- `export_dsl` round-trip fidelity

### Runtime
- `DomainEntityInstance` with property bag, stages, actions, policy evaluation
- `DomainInstanceStore` with link/unlink, subscription fan-out on stage transitions
- Q3′ quantifiers (`any`, `all`, `none`, `count`) with store-linked resolution
- To-one path-prefix nav resolution (`profile City is "Metropolis"`)

### Codegen (DslCompiler)
- Entity type definitions with `Create()` factories, `DomainResult<T>`
- EF Core DbContext with full column mapping, navigation field access, table names
- Minimal API (CRUD + action endpoints with DTOs, error handling, seed data)
- `.http` REST Client test file
- Modes: Entities-only, Db (entities + DbContext), All (everything)
- DBMS packs: Generic, Sqlite

### MCP Tools
- Session management, DSL apply/export, evolve micro-tools
- Oracle tools: `simulate_policy`, `describe_expression`, `lower_expression`
- Runtime tools: `create_instance`, `link_instances`, `unlink_instances`, `invoke_action`, `get_instance`, `list_instances`, `evaluate_policy`
- `get_domain_analysis` with structured facts (root entities, action summary, storage/transport booleans)

## Current State (end of dogfood Wave 1)

All three scenarios resolved:

| Scenario | Status | Fix shipped |
|----------|--------|-------------|
| S1: Library checkout lifecycle | ✅ PASS | `require not` negation (entity-level guard skip) |
| S2: Reassign via link/unlink | ✅ PASS | `unlink_instances` tool, create-in store reg, `get_instance` nav links |
| S3: Owned nested profile | ✅ PASS | Guide honesty, JSON `"relationship"` key, to-one RelationshipNavigation runtime resolution |

## 2-Day Alpha Gap

Per discussion 2026-07-26, Poly is ~2 days from an end-to-end demo alpha. The gaps:

1. **Codegen emits `UseInMemoryDatabase` instead of `UseSqlite`** when Sqlite pack selected — one flag in `MinimalApiGenerator`
2. **No single "build the demo" script** — compile the library domain, generate output, start the API
3. **No published tool** — must clone and run from source
4. **No walkthrough README** — outsider needs to know the CLI incantation

## Key Recent Files Changed (last 48h)

- `Poly/DomainModeling/DomainEntityInstance.cs` — to-one RelationshipNavigation resolution, `require not` guard skip
- `Poly/DomainModeling/Lowering/DomainExpressionJsonParser.cs` — `"relationship"` key support
- `Poly.Mcp/Tools/RuntimeTool.cs` — `unlink_instances`, create-in InstanceMap registration, `get_instance` nav links
- `Poly.Mcp/Docs/poly-dsl-guide.md` — owned access promoted to shipped
- `docs/plans/simple-agent-tasks/dogfood-fix-README.md` — micro-task queue for fixes
- `docs/plans/simple-agent-tasks/dogfood-owned-README.md` — owned access build slice

## Always Read Before Changes

- `AGENTS.md` — placement rules, principles, coding style
- `docs/CORE.md` — module boundaries, pipeline maps, anti-reinvention rules
- `.github/copilot-instructions.md` — DSL guide maintenance rule
