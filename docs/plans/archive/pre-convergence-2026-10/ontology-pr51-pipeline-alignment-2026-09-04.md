# Ontology | PR 51 P1–P6 pipeline alignment

**Date:** 2026-09-04  
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite.  
**SHA:** `0b6fcab93b833ed1ee77b55b0fb01bb3f961921c`  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**North star:** deterministic agent codegen from an ontology of ontology systems. Dual-path runtime vs export is a **bug**, not a host-bind footnote.  
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/plans/pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md)

Frozen pipeline: one analyze → **one operation module** (tree has no bags) **and** surface bags → host artifacts. Simulate and print consume that module. Consumers bind; they do not fork lower. `LowerStageTransitions` and the like are forbidden in new work.

This note does not implement C#, does not change PIPELINE-STATUS, and does not treat P1–P6 as a CURRENT suite.

---

## 1. What P1–P6 commits to

At `0b6fcab` the product path **names** one stage-3 door and one Store-job vocabulary:

| Slice | Commitment that landed |
|-------|------------------------|
| **P1 — one lower** | `rg LowerStageTransitions` in `*.cs` is empty. Create / create-in / unique lower to `this.Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique` (flattened name/value pairs) on both simulate and emit. |
| **P2 — compile once** | `session.Lower` / `RuntimeAnalysisCache.GetOrLower` populates named action / OnEntry trees. Named invoke looks them up; `LowerActionBody` is not on that hot path. |
| **P3 — `session.Lower`** | `DomainSession.Lower` → cached `DomainProgramProjection.ToSyntax`. `session.Emit` prints that module. |
| **P4 — host artifacts** | `uses http` fail-closed if a `BehaviorAction` name is missing from the module (`RequireHttpActionsInModule`). |
| **P5 — one analysis door** | `DomainSession.Analyze` binds `RuntimeAnalysisCache` (vendor maps visible when bound). |
| **P6 — clocks in the tree** | `PreprocessRuntimeKeyword` is gone. Operation-tree `now` / `today` / `guid` lower to BCL members (`DateTime.UtcNow`, `DateOnly.FromDateTime`, `Guid.NewGuid`) the VM executes. |

Create / create-in are Store jobs on the named module, not a second Effect interpreter. That is the right *claim* relative to CORE hard lines (shipped ⊆ Node; create/create-in/unique are Store jobs on the one module; clocks are in the tree).

The transformation’s own stop was stronger: **simulate and `session.Emit` take the same `Lower(...)` result.** That stop did not land. P2 encoded a sibling tree instead.

---

## 2. Where it still fakes meaning / widens dual-path / kernel-vs-library debt

**The dual-path bug is now a cached product.** `GetOrLower` always builds two things: the emit module (`UseThisReference: true`, `this`) **and** `EnsureRuntimeOperations` trees (`Parameter("entity")`, `UseThisReference: false`). Invoke runs the second. `PipelineTransformationTests.InvokeAction_RunsTheCachedTree_NotAReloweredEffectWalk` replaces the **Operations dict**, not the module method body — so the oracle proves compile-once of the sibling, not “simulate the module.” `UseThisReference` is the consumer flag `LowerStageTransitions` was. New work froze it into `RuntimeAnalysisCache`.

**C# still prints a different bind of the same job names.** Generated factories wrap `Stay.Create` / `CreateNav`. `EnsureUnique` in export is `return DomainResult.Success()`; uniqueness is EF schema. Frozen ADR lists those as *current consumers*, not architecture — leaving them as shipped meaning of create/unique is still runtime-vs-export divergence. Agents that simulate then codegen do not get the same program.

**Authoring IR remains execution input on residual paths.** `EvaluatePolicy` re-lowers the guard per call. Subscriptions and transition batches still `LowerActionBody` at execute time. `EvaluateDefaultValue` host-evals `DomainExpression` (`Now` / `Today` / `Guid`) at create-time bag fill — P6 removed the preprocess lie from operation trees and left a second clock interpreter on `DomainEntityInstance.Create`.

**Stage 3 owner is still the C# exporter.** `DomainProgramProjection.ToSyntax` is a façade over `DomainToCSharpExporter.BuildTypeDefsForEntity` / value types / `DomainResult` / contract adapters. The door is named; the call graph is still “lower by printing C#.”

**Stage 5c still walks Domain.** HTTP fail-closed is a **name** check against module methods. `MinimalApiGenerator` / `.http` still consume `BehaviorMetadata` + `Domain`, not operation bodies. Doors must not invent operations — they also must not re-derive them.

**Kernel vs loaded libraries.** Unbound `RuntimeAnalysisCache` fallback is `ForExtensions(core ids)` — vendor maps drop until `Analyze` binds. Scratch `DomainEntityInstance` / `DomainInstanceStore` remain the simulate kernel; Store job names live on `This`. That is current machinery (compose, do not freeze) — P2’s sibling cache treats the kernel’s `UseThisReference: false` shape as a second module rather than binding the generic tree.

These are not “out of scope footnotes” against the north star. Dual-path runtime vs export is the bug the ontology codegen loop cannot absorb.

---

## 3. Ranking

**Overall: `align-with-risk`.** P1–P6 kill the named consumer flag and put Store jobs + clocks on a `session.Lower` door; simulate still does not run the module emit prints.

| Slice | Rank | One sentence |
|-------|------|--------------|
| **P1** | **align** | One Store-job vocabulary; `LowerStageTransitions` is gone. |
| **P2** | **misaligned** | Compile-once caches a runtime-shaped sibling tree, so invoke is not the module. |
| **P3** | **align-with-risk** | `session.Lower` exists; owner is still `DomainToCSharpExporter`. |
| **P4** | **align-with-risk** | HTTP fails closed on missing names; artifacts still walk `Domain`. |
| **P5** | **align-with-risk** | Bound session is real; unbound fallback is still core-catalog. |
| **P6** | **align-with-risk** | Operation-tree clocks are BCL members; create-time defaults still host-eval `now`/`today`/`guid`. |

---

## 4. One tighter next slice (if off)

**Simulate the module method body.** Delete `EnsureRuntimeOperations` / the `Operations` dict as a second lower. Named invoke runs the `TypeDefinitionNode` method `session.Lower` already produced (same node `session.Emit` prints). Bind dictionary `This` so `UseThisReference` is not a product fork.

**Stop:** `rg EnsureRuntimeOperations` empty; invoke identity-equals the module method (replace `Lot.Issue` **on the module**, assert zero children); no `Parameter("entity")` sibling for named actions.

Not this slice: EF Store, `Stay.Create` deletion, EvaluatePolicy cache, subscription populate, `uses cli`, CURRENT admission.

---

## Non-goals of this note

- Implementing C# or widening P1–P6.
- Admitting PIPELINE-STATUS CURRENT.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
