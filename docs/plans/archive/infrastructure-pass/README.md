# Archived — Infrastructure Pass Suite

**Archived:** 2026-07-24  
**Status:** **Done under current bar** (Groups 1–7). Do **not** re-execute unless reopening a named pull item.

## What shipped

| Slice | Outcome | Commit / note |
|-------|---------|----------------|
| Groups 1–5 | Entity syntax; Bar A IR side-path; analysis passes; generator metadata; `DslCompiler` fail-closed pipeline | `3d276a6` family |
| Group 6 | Production IR for DbContext + Program; All-mode smoke; domain-named `{Domain}DbContext.cs` | `c5d2220` |
| G6.5 + G7 | `Generate()` → IR only; dead StringBuilder paths removed; structural IR tests + `GenerationAssertions` | `b394a0e` |

**Product truth (still valid):**

```text
DslCompiler:
  CSharpGenerator().Generate(dbGen.GenerateCompilationUnit())
  CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbCtx))
  httpGen.Generate()   // still string — intentional
```

**Bar A renorms** (not full string oracle) remain the accepted IR dialect — see task-list renorm table in this archive.

## Do not reopen as “incomplete”

- Dual SequenceEqual string oracle (**Bar B**) — **explicit pull**, needs anonymous-object Syntax
- Re-decomposing analysis pipeline (Groups 3–5 closed)
- Re-adding StringBuilder emit twins for DbContext / MinimalApi

## Pull backlog (only if product pain)

| ID | Work |
|----|------|
| **Bar B** | Anonymous-object Syntax + full dual goldens |
| **RestApiSurfacePass** | Routes/DTO surface when a real consumer needs it |
| **StorageAccessPass** | Query/mutation patterns when needed |
| **G6.h1** | TransportPass keep-or-drop (doc or delete) |
| **HttpFile IR** | Only if agents need IR for `.http` |
| **G7′′.1** | Optional: MaxLength Constant `50` structural assert |

Active one-line pointer for agents: [`../../infrastructure-pass-NEXT.md`](../../infrastructure-pass-NEXT.md).

## Files in this archive

| File | Role |
|------|------|
| [`infrastructure-concern-analyzer-suite.md`](infrastructure-concern-analyzer-suite.md) | Design / phase model (historical execution queue) |
| [`infrastructure-pass-task-list.md`](infrastructure-pass-task-list.md) | Group ladder + renorm table |
| [`infrastructure-pass-NEXT-history.md`](infrastructure-pass-NEXT-history.md) | Full review trail (G6/G7/G7′/G7′′) |
| [`simple-agent-tasks/`](simple-agent-tasks/ip-README.md) | Completed `ip-g6-*` micro-tasks |

## Related live docs

- Platform map: [`docs/CORE.md`](../../../CORE.md)
- Persistence units ADR: [`docs/decisions/2026-07-22-persistence-units-medium-facets-pack-syntax-export.md`](../../../decisions/2026-07-22-persistence-units-medium-facets-pack-syntax-export.md)
- Code: `src/Poly.DslCompiler/`, analysis passes under `Poly/DomainModeling/Analysis/`
