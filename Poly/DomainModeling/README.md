# Poly.DomainModeling

This directory contains the **V3 immutable core** for domain modeling.

**Platform map:** [`docs/CORE.md`](../../docs/CORE.md) — domain → generic AST; extend via analysis + node replacement, not ABI forks.

## Quick Start (Direct API)

The domain model is an immutable record graph. All mutation happens through the
analysis-gated evolution pipeline. There is no mutable "workspace" type here —
workspace/session lives in MCP (see `Poly.Mcp`).

### Bootstrap

```csharp
using Poly.DomainModeling.Bootstrap;

// Creates a domain with the 9 canonical built-in primitive types
// (Boolean, Number, Text, Date, Time, DateTime, Duration, Uuid, Binary).
Domain domain = DomainFactory.Create("Orders");
```

### Evolve (single fluent path)

```csharp
using Poly.DomainModeling.Evolution;

var result = new DomainEvolution(domain).Evolve()
    .AddEntity("Order")
    .AddPropertyToEntity("Order", new Property("Status", new DomainTypeReference("Text"), []))
    .AddStage("Order", "Draft")
    .AddAction("Order", "Submit")
    .Apply();

if (result.WasRolledBack) {
    // Domain unchanged — check result.FailureSummary for diagnostics
    Console.WriteLine(result.FailureSummary);
} else {
    domain = result.Root; // new immutable root
}
```

### Query (model-optimized projections)

```csharp
using Poly.DomainModeling.Queries;

var overview = DomainQueries.Overview(domain);
// overview.EntityCount, overview.PrimitiveTypeCount, ...

var entity = DomainQueries.GetEntity(domain, "Order");
// entity.Properties, entity.Stages, entity.Actions, ...

var summary = DomainQueries.GetAnalysisSummary(analysis);
// summary.ErrorCount, summary.Warnings, ...
```

### Key rules

1. **Immutability**: Every `Apply()` returns a new `Domain` root. The original is untouched.
2. **Analysis gate**: All changes pass through domain model analysis before acceptance.
3. **Rollback**: On analysis failure, the result has `WasRolledBack = true` and
   the original root is returned unchanged.
4. **Batch efficiency**: Compose multiple changes in one `Evolve()` call for
   a single analysis pass.
5. **No workspace here**: Session/revision management belongs in the MCP layer.

## Architectural Decisions

Major decisions and design documents for this module have been moved to the centralized location:

- [docs/decisions/2026-05-31-immutable-core-domain-modeling.md](../docs/decisions/2026-05-31-immutable-core-domain-modeling.md) (summary)
- [docs/decisions/2026-05-31-immutable-core-detailed.md](../docs/decisions/2026-05-31-immutable-core-detailed.md)
- [docs/decisions/2026-05-31-evolution-layer-design.md](../docs/decisions/2026-05-31-evolution-layer-design.md)

See the root `AGENTS.md` for guidance on when to consult these documents.

## Directory overview

| Directory | Purpose |
|-----------|---------|
| `Bootstrap/` | `DomainFactory` and `CanonicalBuiltInTypeCatalog` — create domains with built-in types |
| `Queries/` | `DomainQueries` — model-optimized query projections (overview, entity detail, analysis summary) |
| `Evolution/` | `DomainEvolution`, `EvolutionBuilder`, `DomainChange` types — single evolution engine |
| `Analysis/` | V3 domain model analyzers on shared Syntax.Analysis substrate |
| `Lowering/` | `DomainExpressionLoweringPass`, `PolicyEvaluator` — VM-integrated policy evaluation |
| `Builders/` | Alternative fluent construction API (used for rich entity setup) |
| `Constraints/` | Constraint types (Range, Length, Pattern, etc.) |
| `Effects/` | Effect types (Create, Transition, Assign, Conditional, Composite, etc.) |