# Goal: merge the DomainModeling rewrite to `master`

**Date:** 2026-08-25  
**Status:** ✅ DONE (PR 26)  
**Does not wait on:** create / create-in leaving EffectExecutor.

## Goal

Fast-forward `rewrite/domainmodeling-from-scratch` onto `master` and treat **that** as the product trunk, so agents can work in **separate workstreams** from `master` instead of serializing on the rewrite branch.

`origin/master` is the merge-base of rewrite. Merge is a fast-forward (581 commits, 0 unique on master).

## Shipped claim (strong implementations)

Same-tree runtime + emit (VM canonical):

| Operation | Tree |
|-----------|------|
| Assign / if / composite | Syntax nodes |
| Stage transition | `CurrentStage` assign + `Invoke(Member(This, "Notify"))` in `finally` |
| Self-invoke | `Invoke(Member(This, action), args)` |
| Cross-entity invoke | `this.Rel.Action(args)` + linked-target `DomainResult.Failure` guard |
| For-invoke | OneToMany `ForEachLoop` + `InvokeNamed` + `if (!result.IsSuccess) return result` + zero-match Failure |

Also strong: Grammar-table `.poly`; `DomainSession` analyze/emit; MCP harness (author / inspect / simulate with caller context); HTTP host only via `uses http`; core test suite green on rewrite.

## Not a merge blocker (documented residual)

Keep these as **debt on master**. Do not grow them. Do not hold the merge.

- **create / create-in** — emit lowers (gated on `LowerStageTransitions`); runtime is still EffectExecutor
- **`ExecuteStructured`** — mixed `if`+create
- Sequential transitions share stale `SourceStageName`
- `RuntimeAnalysisCache` core-catalog reopen; Temporal Meaning unused
- S1 `not`-in-chain span vs fold
- Self/cross-entity lowering does not wrap `IsSuccess` (for-invoke does)

`create` stays in the shipped DSL. Dual-path is honest residual, not a silent claim that create is same-tree.

## Merge checklist

1. Land this CURRENT bump (this PR / plan). Close the “CURRENT is create/create-in” story as a merge blocker.
2. Confirm `dotnet run --project Poly.Tests/Poly.Tests.csproj` green on rewrite HEAD.
3. Open PR **rewrite → master** (fast-forward). Description = this shipped claim + residual list.
4. After merge: default PR base is `master`. Agents do not open work on `rewrite/domainmodeling-from-scratch`.
5. Then admit **parallel** workstreams (below). One stream still owns any given file.

## After master: parallel workstreams

Admission becomes **one owner per file**, not one suite for the whole repo.

| Stream | Owns | Does not touch |
|--------|------|----------------|
| **create / create-in** | `DomainModeling` lowering + `EffectExecutor` + `ExecuteStructured` | Grammar tables, MCP tool catalog |
| **MCP mut-safety** | `Poly.Mcp` session lock / idempotent add | Domain lowering, VM emitter |
| **Grammar wrap-up** | Grammar engine + live-fold S1 | Domain runtime, host-ABI |
| **Naming (`V3*`)** | rename-only (`post-v2-delete-naming-cleanup`) | behavior |

Create/create-in is the next **DomainModeling** slice after trunk lands. It is not the rewrite-to-master gate.

## Out of scope for this merge

- Finishing host-ABI create
- Killing `ExecuteStructured` / `LowerStageTransitions`
- Byte-identical `Poly.dll`
- Requiring every domain to `uses http`
