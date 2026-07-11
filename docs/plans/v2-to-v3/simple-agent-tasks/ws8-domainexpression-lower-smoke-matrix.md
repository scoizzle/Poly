# Micro-Task: DomainExpression lower + VM smoke matrix

**Parent**: WS8 / WP5  
**Suite:** [`ws8-README.md`](ws8-README.md) **#2**  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~6–8k  
**Status**: [x] **Done** (accepted with review caveats — 2026-07-10)  
**Last review**: 2026-07-10

## Objective

Regression matrix: every existing `DomainExpression` concrete node kind either lowers+executes or is a **documented gap**.

## What landed

- Inventory of subtypes in `DomainExpressionVmExecutionTests` (gap section)
- Lower-only tests: ParameterAccess, OwnedAccess, DateOperation, RelationshipNavigation
- Exists/NotExists **do** execute on VM

## Code review findings

| Severity | Finding |
|----------|---------|
| **Medium** | Several “smoke” tests only assert `Pass.Lower(...) != null` — inventory of **lower**, not VM execute. Acceptable if framed as gap inventory. |
| **Low** | `ParameterAccess_WithExplicitSubject_ResolvesParameter` builds an unused `Parameter("x")` and does not assert node shape / name — weak. |
| **Info** | Owned/Date/Rel nested Member VM gaps are honestly commented. |

## Follow-ups (optional — not blocking Done)

1. Strengthen ParameterAccess test: assert lowered node type/name, or map via `DomainExpressionLoweringPass(parameters)`.
2. One nested-Member VM case if easy (optional); else leave gaps as documented.

## Verification

- [x] Inventory vs source subtypes
- [x] Gaps documented in tests
- [x] Exists/NotExists VM covered

## Out of Scope

- Full action/effect program lowering
- MCP
