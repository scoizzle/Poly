# Ontology | PR 51 re-rank after §4 slice

**Date:** 2026-09-05  
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite.  
**SHA:** `1483d26183cb35f60e1d7f7a9cee5d613f095c93`  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**North star:** deterministic agent codegen from an ontology of ontology systems. Dual-path runtime vs export is a **bug**, not a host-bind footnote.  
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/plans/pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md)

Frozen pipeline: one analyze → **one operation module** (tree has no bags) **and** surface bags → host artifacts. Simulate and print consume that module. Consumers bind; they do not fork lower.

This note does not implement C#, does not change PIPELINE-STATUS, and does not treat P1–P6 as a CURRENT suite.

---

## 1. What P1–P6 commits to (post-§4)

§4 closed the P2 sibling tree. Named invoke now runs the `TypeDefinitionNode` method body that `session.Lower` already produced — the same node `session.Emit` prints. `EnsureRuntimeOperations` is gone; `rg EnsureRuntimeOperations --glob '*.cs'` is empty.

| Slice | Commitment that landed |
|-------|------------------------|
| **P1 — one lower** | `rg LowerStageTransitions` in `*.cs` is empty. Create / create-in / unique lower to `this.Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique` on both simulate and emit. |
| **P2 — compile once** | `session.Lower` / `RuntimeAnalysisCache.GetOrLower` caches only `Module` (`DomainProgramProjection.ToSyntax`). Named invoke: `TryGetModuleMethod` → `BindModuleMethodBody` (BindThis rewrites This→entity parameter — consumer bind, not a second lower). No `Operations` dict. No sibling tree. |
| **P3 — `session.Lower`** | `DomainSession.Lower` → cached `DomainProgramProjection.ToSyntax`. `session.Emit` prints that module. |
| **P4 — host artifacts** | `uses http` fail-closed if a `BehaviorAction` name is missing from the module (`RequireHttpActionsInModule`). |
| **P5 — one analysis door** | `DomainSession.Analyze` binds `RuntimeAnalysisCache` (vendor maps visible when bound). |
| **P6 — clocks in the tree** | Operation-tree `now` / `today` / `guid` lower to BCL members (`DateTime.UtcNow`, `DateOnly.FromDateTime`, `Guid.NewGuid`) the VM executes. |

---

## 2. Where it still fakes meaning

These are real residuals, not footnotes.

- **`LowerActionBody` fallback.** When `TryGetModuleMethod` fails (subscriptions, transition batches, missing entry actions), invoke falls back to `effectPass.LowerActionBody(effects)` — a second lowering path at execute time. The §4 stop killed the sibling cache; it did not kill the residual lower-on-execute path.

- **`EvaluatePolicy` re-lowers per call.** Guard expressions are re-evaluated from `DomainExpression` authoring IR on every `EvaluatePolicy` call. No cached policy tree on the module.

- **`EvaluateDefaultValue` host-evals authoring IR.** `DomainEntityInstance.Create` host-evals `Now` / `Today` / `Guid` from `DomainExpression` at create-time bag fill. P6 removed the preprocess lie from operation trees; this second clock interpreter on `Create` remains.

- **Stage 3 owner is still `DomainToCSharpExporter`.** `DomainProgramProjection.ToSyntax` is a façade over `DomainToCSharpExporter.BuildTypeDefsForEntity` / value types / `DomainResult` / contract adapters. The door is named; the call graph is still "lower by printing C#."

- **Emit diverges on create bind.** Generated C# factories wrap `Stay.Create` / `CreateNav`. `EnsureUnique` in export is `return DomainResult.Success()`; uniqueness is EF schema. Simulate and export still diverge on create bind semantics.

- **Stage 5c still walks Domain.** `MinimalApiGenerator` / `.http` consume `BehaviorMetadata` + `Domain`, not operation bodies. HTTP fail-closed is a name check against module methods; route derivation still walks Domain bags.

- **Unbound `RuntimeAnalysisCache` fallback.** When nothing has bound, `GetHolder` returns `ForExtensions(core ids)` — vendor maps are absent until `Analyze` is called.

---

## 3. Ranking

**Overall: `align-with-risk`.** §4 closed the P2 sibling EnsureRuntimeOperations tree — the most visible misalignment. Named invoke now runs the module method body. Residuals `LowerActionBody` fallback + emit-bind divergence + 5c Domain walk remain.

| Slice | Rank | One sentence |
|-------|------|--------------|
| **P1** | **align** | One Store-job vocabulary; `LowerStageTransitions` is gone. |
| **P2** | **align** | Named invoke runs `session.Lower` module method bodies. `EnsureRuntimeOperations` gone. |
| **P3** | **align-with-risk** | `session.Lower` exists; owner is still `DomainToCSharpExporter`. |
| **P4** | **align-with-risk** | HTTP fails closed on missing names; artifacts still walk `Domain`. |
| **P5** | **align-with-risk** | Bound session is real; unbound fallback is still core-catalog. |
| **P6** | **align-with-risk** | Operation-tree clocks are BCL members; create-time defaults still host-eval `now`/`today`/`guid`. |

---

## 4. One tighter next slice

**Kill `LowerActionBody` when Domain is bound.** When `RuntimeAnalysisCache` has a module (i.e. `GetOrLower` has run), every named action must have a module method. If `TryGetModuleMethod` fails on a bound domain, throw — do not fall back to `LowerActionBody`. The fallback path is only for unbound/simulate-kernel contexts; once the module is materialized, it owns all action lowering.

**Stop:** After the change, invoke on a bound domain with a missing module method throws `InvalidOperationException` — not silently re-lowers. `rg LowerActionBody` in `DomainEntityInstance.cs` should show the fallback only in the `Domain is null` (unbound) branch. Test: add an action in DSL, do not add a corresponding module method body, invoke → throws.

Not this slice: EF Store, `Stay.Create` deletion, EvaluatePolicy cache, subscription populate, `uses cli`, CURRENT admission.

---

## Non-goals of this note

- Implementing C# or widening P1–P6.
- Admitting PIPELINE-STATUS CURRENT.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
