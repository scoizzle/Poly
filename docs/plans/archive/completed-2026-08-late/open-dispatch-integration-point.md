# Plan: Open expression dispatch — remove core→pack usings

**Date:** 2026-08-13
**Status:** EXECUTED 2026-08-13 — build 0/0, 2178/2178 green. Core `Poly/` has a single pack
import: `DomainInputSet` (the composition root, `CreateWithTemporalPack`), same as SqlPack.
`Poly.Mcp/OracleTool` (host tool layer) also imports the pack legitimately.

## Why

The temporal IR (`Now`/`Today`/`DateOperation`/`Duration`) is pack-owned but derives from core
`DomainExpression`. Core integration points that must *route or resolve* it currently `using
Poly.DomainModeling.Packs.Temporal` — a core→pack dependency in 7 files:

- `DomainExpressionDispatch` (closed switch + virtual methods)
- `DomainExpressionRewriteBase` (identity/composite overrides)
- `DomainExpressionLoweringPass` (lowering arms)
- `ExpressionTypeAnalyzer` (inference + default checks)
- `DslExpressionParser` (TryFoldDateOperation)
- `EffectLoweringPass` (default-expression lowering: Now/Today → CLR)
- `DomainEntityInstance` (runtime default values: Now/Today → UtcNow/Today)
- `DomainExpression` (`DateOp` factory) · `OracleTool` (description)

The wrong abstraction is the **closed dispatch switch** ("new subtype = compile error"). Fix: an
open dispatch registry that the pack contributes to, with an **ambient default** so bare
construction sites (18 lowering sites, static `DomainModelAnalyzer`) get pack handlers without
threading.

## Design

### Core (new): `Poly/DomainModeling/ExpressionDispatchRegistry.cs`

```csharp
public interface IExpressionDispatchHandler<TResult> {
    Type ExpressionType { get; }
    bool TryHandle(DomainExpression expression, Func<DomainExpression, TResult> route, out TResult result);
}

public sealed class ExpressionDispatchRegistry<TResult> {
    public static ExpressionDispatchRegistry<TResult> Default { get; }   // ambient
    public void Register(IExpressionDispatchHandler<TResult> handler);   // duplicate type → throw
    public bool TryDispatch(DomainExpression expression, Func<DomainExpression, TResult> route, out TResult result);
}
```

### Core (open): `DomainExpressionDispatch<TResult>`
- Remove `Now`/`Today`/`DateOperation`/`Duration` virtual methods + switch arms.
- `Route(expr)`: core switch; `_` → `(_registry ?? ExpressionDispatchRegistry<TResult>.Default).TryDispatch(...)`; miss → `NotSupportedException` (fail-closed).
- Optional ctor registry; `route` callback = `x => Route(x)`.

### Pack registers handlers (module initializer, temporal is always-on product default)
`Poly/DomainModeling/Packs/Temporal/TemporalDispatchRegistration.cs` (`[ModuleInitializer]`):
- Rewrite (`ExpressionDispatchRegistry<DomainExpression>.Default`): Now/Today/Duration identity; DateOperation composite recursion.
- Lowering (`ExpressionDispatchRegistry<Node>.Default`): DateOperation → AddDays/AddMonths/DiffDays invoke (route date+offset); Duration → throw.
- Analysis (`ExpressionDispatchRegistry<TypeCategory>.Default`): Now/Today/DateOperation → Date; Duration → Duration.
- Default-expression lowerer + runtime clock value: registered provider hooks (below).

### Core removes its temporal arms (each becomes a handler)
- `DomainExpressionRewriteBase`: delete temporal overrides.
- `DomainExpressionLoweringPass`: delete DateOperation/Duration arms.
- `ExpressionTypeAnalyzer`: route `InferType` temporal cases through a new
  `DomainExpressionDispatch<TypeCategory>` instance with the ambient registry; delete
  `CheckDateOperation`/`CheckDefault` temporal arms, keep core paths. `TypeCategory` becomes
  `internal` (same assembly).
- `DslExpressionParser`: `TryFoldDateOperation` moves to the pack as a registered fold
  (registered via `TemporalPack` grammar contributor); parser calls a registered fold hook
  instead of the pack type.
- `DomainExpression`: `DateOp` factory moves to the pack; core drops it.
- `EffectLoweringPass`/`DomainEntityInstance`: Now/Today resolution moves behind a registered
  default-expression provider (core registry the pack fills); runtime clock value behind a
  registered clock provider.
- `OracleTool`: describe temporal nodes via a registered describer or generic fallback.

### Fail-closed
Unregistered temporal node reaching a concern → throw (never placeholder). Duplicate handler
registration → throw. Ambient default is documented as the built-in-pack product-default seam.

## Sequencing (test-first each step; suite green between steps)

1. Core registry + open `DomainExpressionDispatch` (empty ambient → suite green, temporal nodes
   would throw only if reached; pack registration added same step to keep green).
2. Rewrite + lowering handlers registered; delete rewrite/lowering temporal arms.
3. `DateOp` factory → pack; parser fold → pack fold hook.
4. Analyzer: `TypeCategory` inference dispatch + temporal checks as handlers.
5. Default-expression lowerer (EffectLoweringPass) + runtime clock value (DomainEntityInstance).
6. OracleTool description.
7. Delete all core→pack usings; final build + 2173+ suite green; update docs
   (p1 design lock Q1, pack-host lock 4, capability inventory).

## Files (expected)

| File | Change |
|------|--------|
| `[NEW] Poly/DomainModeling/ExpressionDispatchRegistry.cs` | registry + handler interface |
| `Poly/DomainModeling/DomainExpressionDispatch.cs` | open `Route` via ambient registry |
| `Poly/DomainModeling/DomainExpressionRewriteBase.cs` | remove temporal arms |
| `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` | remove temporal arms |
| `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs` | inference dispatch + remove temporal arms |
| `Poly/DomainModeling/Parsing/DslExpressionParser.cs` | fold via registered hook |
| `Poly/DomainModeling/DomainExpression.cs` | remove `DateOp` factory |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | default-expression via provider |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` | clock value via provider |
| `Poly.Mcp/Tools/OracleTool.cs` | describe via registered describer |
| `[NEW] Poly/DomainModeling/Packs/Temporal/TemporalDispatchRegistration.cs` | `[ModuleInitializer]` registers handlers |
| `[NEW] Poly/DomainModeling/Packs/Temporal/*Handler.cs` | rewrite/lowering/analysis/fold/default providers |
| `Poly/DomainModeling/Packs/Temporal/*` | move `DateOp`-equivalent factory here |
| tests | adjust usings; add registry fail-closed tests |

## Not in this change
Temporal clock runtime eval (p1-gate follow-up). Any pack beyond temporal.
