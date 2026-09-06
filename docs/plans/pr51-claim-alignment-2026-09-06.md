# PR 51 Claim Alignment — SHA 574e941a

**Date:** 2026-09-06  
**Status:** Proposal — not CURRENT. Do not admit.  
**Author:** Market (fact-check desk). No implementation.  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**SHA:** `574e941ab80a0e2ff9a2429ea9f411cf943a16bb`  
**Sources:** PR title/body; `docs/plans/pipeline-transformation-2026-09-04.md` P1–P6; tree at this SHA.  
**Prior:** gaps none at `48a92220` (2026-09-05). This pass is tip-only.

---

## Claim table

| # | Plan/PR claim | Evidence at `574e941a` | Verdict |
|---|---------------|------------------------|---------|
| **P1** | One lower. Create / create-in / unique always lower to Store jobs. C# `Stay.Create` / `CreateNav` are host bind of those jobs. `LowerStageTransitions` gone. | `rg LowerStageTransitions` → **0** `.cs` hits. `EffectLoweringPass` docs Store jobs `Create` / `CreateIn` / `ProbeCreate` / `EnsureUnique` (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:18`). Host bind in `DomainToCSharpExporter.StoreBind.cs`. | **MATCH** |
| **P2** | Compile once. Named invoke runs cached module method `Body`. Subscriptions / transition batches still lower at execute time. | `ExecuteEffectList` (`DomainEntityInstance.cs:653–671`): named `actionName` → `TryGetModuleMethod` + `BindModuleMethodBody` (not a second lower); else `LowerActionBody` for entry/subscriptions/batches. Plan stop: `LowerActionBody` not on named-action hot path — holds. | **MATCH** |
| **P3** | `session.Lower` = cached `DomainProgramProjection.ToSyntax`. `session.Emit` prints that module. | `DomainSession.Lower` → `RuntimeAnalysisCache.GetOrLower` (`DomainSession.cs:140`). Cache: `holder.Module ??= DomainProgramProjection.ToSyntax` (`RuntimeAnalysisCache.cs:62–72`). | **MATCH** |
| **P4** | Host artifacts: `uses http` fail-closed if a `BehaviorAction` is missing from the module. | `DslCompiler.RequireHttpActionsInModule` called at emit (`src/Poly.DslCompiler/DslCompiler.cs:187`, definition `:223`). | **MATCH** |
| **P5** | One analysis door: `DomainSession.Analyze` binds `RuntimeAnalysisCache`. | `DomainSession.Analyze` (`:116–119`) calls `RuntimeAnalysisCache.Bind(domain, this, analysis)`. | **MATCH** |
| **P6** | Clocks in the tree → BCL members the VM executes. `PreprocessRuntimeKeyword` removed. | `rg PreprocessRuntimeKeyword` → **0**. `EffectLoweringPass` maps `now`/`utcnow`/`today`/`guid` to `DateTime.UtcNow` / `DateTime.Today` / `Guid.NewGuid` (`EffectLoweringPass.cs:944–974`). | **MATCH** |
| **Create defaults on probe** | Store `Create` / `ProbeCreate` fill `default(...)` before unique/required validation. | `DomainInstanceStore` ProbeCreate/CreateCore call `FillCreateDefaults` before constraints (`DomainInstanceStore.cs:151`, `:175`). Helper at `DomainEntityInstance.cs:146`. | **MATCH** |
| **Fine Type auto-link (this tip)** | `Store.Create` auto-links unambiguous many-rel (Fine Type) — commit `574e941a`. | Bare create (no `relationshipName`) calls `TryAutoLinkUnambiguousOutbound` (`DomainInstanceStore.cs:225–228`). Comment cites PR 52 Fine. Dogfood re-probe on this SHA: Fine orphan **N**, Type+Rel skew **N** (PR comment 2026-09-06). | **MATCH** |

---

## Gaps

**None** for the claimed P1–P6 + create-defaults + Fine Type auto-link scope at `574e941a`.

---

## Not a claim gap (residuals)

- `LowerActionBody` still exists for populate + subscriptions / transition batches (`DomainEntityInstance.cs:667–671`; `EffectLoweringPass.cs:698`). Plan P2 already allows that. Ontology residual queue (LowerActionBody → emit-bind → 5c → EvaluatePolicy/defaults) is **next product work**, not a PR-body overclaim.
- **Not CURRENT.** Plan status and PIPELINE-STATUS stay non-CURRENT; this file does not admit.

---

## Recommendation

Claim surface at `574e941a` matches what the PR ships for the stated bullets. Fact-check does not replace Razor / Final Boss. Idle until the next landing SHA.

---

*End. No product commit. Proposal — not CURRENT.*
