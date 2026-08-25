# Micro-Task: PolicyEvaluator — VM as primary product path

**Parent**: WS8 / WP5  
**Suite:** [`ws8-README.md`](ws8-README.md) **#3**  
**Difficulty**: Small  
**Estimated Tokens**: ~5k  
**Status**: [x] **Done** (accepted with nits — 2026-07-10)  
**Last review**: 2026-07-10

## Objective

VM primary for product; dual LINQ+VM only for explicit cross-check.

## What landed

- `Evaluate<T>` → VM only via `CompileVMPredicate`
- `EvaluateWithDualOracle` for tests
- XML docs describe VM-primary design
- Tests updated to `Evaluate` / dual-oracle

## Code review findings

| Severity | Finding |
|----------|---------|
| **Low** | `Evaluate` recompiles the program on every call — OK for tests; product hot path may want compile-once later. |
| **Low** | DomainModeling README may still lack a one-line “use PolicyEvaluator.Evaluate (VM)” pointer (Exact Steps item 4). |
| **Info** | LINQ path still has fragile parameter binding — secondary only. |

## Follow-ups (optional — not blocking Done)

1. One-line in `Poly/DomainModeling/README.md` pointing at VM-primary eval.
2. Compile-once helper only if a real consumer measures cost.

## Verification

- [x] Main product method is not silent dual-run
- [x] Dual-oracle clearly named
- [x] Policy tests green

## Out of Scope

- MCP
- Removing LinqExpressionGenerator
