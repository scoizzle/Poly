# Ontology | PR 51 residual queue through 2026-09-08

**Date:** 2026-09-05  
**Status:** Proposal / consultant note — **not CURRENT**. Do not admit a suite.  
**SHA:** `48a922203f1f930a818e40ee5039710541ad0f7b` (`48a92220`)  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**Prior:** `docs/plans/ontology-pr51-pipeline-alignment-2026-09-05.md` (re-rank at `1483d261`; not in this commit)  
**North star:** deterministic agent codegen from an ontology of ontology systems. Dual-path runtime vs export is a **bug**, not a host-bind footnote.  
**Hard lines:** [`docs/CORE.md`](../CORE.md) §0 / hard lines · [`docs/decisions/2026-09-04-frozen-core-pipeline.md`](../decisions/2026-09-04-frozen-core-pipeline.md) · [`docs/plans/pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md)

Frozen pipeline: one analyze → **one operation module** (tree has no bags) **and** surface bags → host artifacts. Simulate and print consume that module. Consumers bind; they do not fork lower.

This note does not implement C#, does not change PIPELINE-STATUS, and does not treat P1–P6 as a CURRENT suite. It is the ordered dual-path residual queue for the sprint through **2026-09-08**.

---

## 1. What is aligned (at `48a92220`)

Named invoke **is** the `session.Lower` module. `EnsureRuntimeOperations` / the `Operations` dict sibling tree is gone (`rg EnsureRuntimeOperations --glob '*.cs'` empty). `TryGetModuleMethod` → `BindModuleMethodBody` runs the same `MethodDefinitionNode.Body` that `session.Emit` prints (dictionary `This` is consumer bind, not a second lower).

| Slice | Commitment that holds |
|-------|------------------------|
| **P1 — one lower** | `rg LowerStageTransitions` empty. Create / create-in / unique are Store jobs on both simulate and emit. |
| **P2 — compile once** | Named invoke runs the cached module method. No sibling runtime tree. |
| **P3 — `session.Lower`** | Door exists; `session.Emit` prints that module. |
| **P4 — host artifacts** | `uses http` fail-closed on missing module action names. |
| **P5 — one analysis door** | Bound `Analyze` is real. |
| **P6 — clocks in the tree** | Operation-tree `now` / `today` / `guid` are BCL members the VM executes. |

That is the aligned product claim. Dual-path residuals below are why overall ranking does **not** leave `align-with-risk`.

---

## 2. Ordered residual queue (why this order)

**Product through 2026-09-08:** an agent that simulates a named action and then codegens must consume **one** module. Rank residuals by how much they still fork that loop (5a execute vs 5b print vs 5c host files), not by local cleanliness.

`LowerActionBody` is **now**, not after. Chieftan order **after** it closes:

| # | Residual | Dual-path | Why this slot |
|---|----------|-----------|----------------|
| **0 now** | **`LowerActionBody` on execute** | 5a still lowers from Effect IR when the module lookup does not bind | Until named-action execute is bind-only, simulate is not the module. Ranking stays `align-with-risk`. At HEAD: `DomainEntityInstance.ExecuteEffectList` still calls `effectPass.LowerActionBody` (bound: subscriptions / transition batches / missing entry; unbound: all effect lists, including named). Named+bound already throws if `TryGetModuleMethod` misses. |
| **1** | **Emit-bind / `Stay.Create`** | 5b prints a different bind of the same job names | Generated factories wrap `Stay.Create` / `CreateNav`. `EnsureUnique` in export is `return DomainResult.Success()`; uniqueness is EF schema. After 5a is the module, this is the simulate→codegen bind fork. |
| **2** | **Stage 5c Domain walk** | HTTP / `.http` re-derive from `Domain` | `MinimalApiGenerator` consumes `BehaviorMetadata` + `Domain`, not operation bodies. Fail-closed is a **name** check. After create bind is honest, doors still invent from facts instead of mapping the module catalog. |
| **3** | **`EvaluatePolicy` + defaults host-eval** | Authoring IR is still execution input | Guards re-lower from `DomainExpression` on every `EvaluatePolicy`. `EvaluateDefaultValue` host-evals `Now` / `Today` / `Guid` at `Create` bag fill. After named simulate→codegen is one module, this is the leftover second interpreter on the same customer loop (`evaluate_policy` / create-time clocks). |

Not this sprint (do not reorder into 0–3): Stage 3 owner still `DomainToCSharpExporter`; unbound `RuntimeAnalysisCache` `ForExtensions(core ids)`; EF Store; `uses cli`; CURRENT admission.

---

## 3. Overall ranking

**`align-with-risk` until `LowerActionBody` is closed on named-action execute paths.** §4 closed the P2 sibling tree; named invoke runs the module; the execute-time lower remains. Do not re-rank to `align` on a residual `LowerActionBody` call.

| Slice | Rank | One sentence |
|-------|------|--------------|
| **P1** | **align** | One Store-job vocabulary; `LowerStageTransitions` is gone. |
| **P2** | **align** | Named invoke runs `session.Lower` module method bodies. `EnsureRuntimeOperations` gone. |
| **P3** | **align-with-risk** | `session.Lower` exists; owner is still `DomainToCSharpExporter`. |
| **P4** | **align-with-risk** | HTTP fails closed on missing names; artifacts still walk `Domain`. |
| **P5** | **align-with-risk** | Bound session is real; unbound fallback is still core-catalog. |
| **P6** | **align-with-risk** | Operation-tree clocks are BCL members; create-time defaults still host-eval `now`/`today`/`guid`. |

---

## 4. One next slice

**`LowerActionBody` delete/bind on named-action execute.** Named actions never re-lower at execute time. Bind the module method or throw. Do not fall back to `effectPass.LowerActionBody` when `actionName` is set (bound or unbound).

**Stop:** `rg LowerActionBody` is empty on execute paths for named actions (`DomainEntityInstance` execute / HostAbi effect lists that run a named action). Subscriptions / transition batches / exporter `LowerActionBody` (stage 3) are **not** this slice’s stop. Test: named invoke with a missing module method throws — it does not silently re-lower.

Not this slice: `Stay.Create` deletion, Stage 5c Domain walk, EvaluatePolicy cache, create-time default host-eval, subscription populate, `uses cli`, CURRENT admission.

---

## Non-goals of this note

- Implementing C# or widening P1–P6.
- Admitting PIPELINE-STATUS CURRENT.
- Treating scratch store, `Stay.Create`, or Store job names as frozen.
- Reordering the post-`LowerActionBody` queue (emit-bind → 5c → host-eval) inside this sprint.
