# Platform velocity review — pain points for planned & future features

**Date:** 2026-07-25  
**Status:** Active reference (review, not a micro-task suite)  
**Audience:** Agents/humans adding DAU D3, packs, RestApi/transport emit, Q4, MCP tools, dogfood  
**Related:** [`archive/domainmodeling-completed-2026-08/domain-analysis-unification.md`](archive/domainmodeling-completed-2026-08/domain-analysis-unification.md) · [`CORE.md`](../CORE.md) · [`v2-to-v3/master-roadmap.md`](v2-to-v3/master-roadmap.md)

---

## 1. Executive summary

Poly’s **spine is coherent**: immutable `Domain` → analysis-gated `DomainEvolution` → DE lower → Syntax AST → VM; MCP is a thin consumer. What slows feature velocity is **mid-migration dual homes** and **agent-invisible facts**: domain analysis already pays for topology/aggregate/behavior, but storage/transport/packs still live in a second codegen world, and MCP mostly surfaces diagnostics—not structured metadata.

**Cohesion today:** strong for evolve + core domain facts; **partial** for operational model (storage/transport) and pack-aware session analyze; **weak** for agent consumption of analysis bags and for “one product path” in IR tests.

If we keep adding features (packs, RestApi emit, Q4, E5 tools) **without finishing DAU D3 + MCP facts**, each feature will re-solve “where does the fact live?” and re-wire DslCompiler vs MCP separately.

---

## 2. What already works (do not break)

| Asset | Why it enables features |
|-------|-------------------------|
| Analysis-gated evolution | Safe authoring; fail-loud Apply |
| Shared `Syntax.Analysis` framework | One pass/metadata model for domain + interpretation |
| Domain pipeline owns topo / agg / behavior / crossref | Agents *can* see these if we expose them |
| VM-canonical policy path | One execution truth for policies |
| Codegen Bar A IR (DbContext + Program) | Real consumer of aggregate/behavior/storage |
| MCP session + apply_dsl + runtime tools | Dogfood loop exists |
| Stage-transition-as-observable | Clear non-event model for new effects |

---

## 3. Pain points (ordered by impact on planned work)

### P0 — Blocks or multiplies cost of near-term planned work

#### P0.1 Dual operational pipeline (storage / transport)

| | |
|--|--|
| **Symptom** | `DomainModelAnalyzer` does not register Storage/Transport. `DslCompiler` runs a second `AnalyzerBuilder` wrapping `Lowering.StorageAnalyzer` / `TransportAnalyzer`. |
| **Blocks** | DAU D3; multi-DBMS packs on session analyze; RestApi/MinimalApi consuming **domain Transport** at emit without re-derive; agent-aware storage validation before codegen |
| **Cost if ignored** | Every pack and every emit path re-implements “run storage with maps”; MCP never fails on storage smells until CLI codegen |
| **Mitigation** | DAU **D3.0–D3.5** (fail-closed Storage, always-on Storage+Transport, context API, emit-first) |

#### P0.2 Authoring context not on evolve / analyze

| | |
|--|--|
| **Symptom** | `DomainAuthoringContext` (type maps, conventions, `PassRegistry`) used for parse/print and DslCompiler; `DomainModelAnalyzer.Analyze(domain)` and `DomainEvolution` take no context. MCP has `CreateWithSqlPack()` but does not pass maps into analysis. |
| **Blocks** | Pack plugins ([`domain-plugin-extension-platform.md`](domain-plugin-extension-platform.md)); dialect-specific validation during authoring; PassRegistry enrichers on product path |
| **Cost if ignored** | Packs stay “codegen-only toys”; evolve accepts models that cannot store under the session’s pack |
| **Mitigation** | DAU **D3.1**, **D3.4** |

#### P0.3 MCP does not project paid-for analysis metadata

| | |
|--|--|
| **Symptom** | Session holds `LatestAnalysis` with EffectTopology, OwnershipAggregate, Behavior, CrossReference; `get_domain_analysis` returns counts + ≤10 diagnostic messages. Structured facts only appear ad hoc (e.g. oracle entity detail capabilities). |
| **Blocks** | Agent dogfood for DAU features; Q4/date work that needs graph awareness; any “generate X from domain” MCP tool without re-analysis |
| **Cost if ignored** | Agents re-infer hierarchy from DSL text; tools invent parallel fact APIs |
| **Mitigation** | DAU **D3.4b** (extend analysis payload or thin facts tool; **no second store**) |

#### P0.4 StoragePass fails open without hierarchy metadata

| | |
|--|--|
| **Symptom** | TransportPass errors if aggregate/topology missing; StoragePass still `SetMetadata` with null models allowed → empty FKs can look “complete”. |
| **Blocks** | Trust in storage metadata once always-on; pack tests that assume fail-closed |
| **Mitigation** | DAU **D3.0** (small, do first) |

---

### P1 — High friction for multiple roadmap items

#### P1.1 Incomplete analysis-home unify (deferred D2.1–D2.3)

| | |
|--|--|
| **Symptom** | Dual root story (EntityStructure + OwnershipAggregate); Capability + Behavior double walk; Topology + CrossReference separate coupling walks. Naming still mixes `*Pass` / `*Analyzer`. |
| **Blocks** | Clean Transport/Storage consumers of “one root / one action shape / one coupling graph”; future pack facets that attach to those facts |
| **Mitigation** | DAU D2.1–D2.3 when D3 has a stable always-on surface (or interleave carefully) |

#### P1.2 IR / test helpers bypass product analysis path

| | |
|--|--|
| **Symptom** | `GenerationAssertions` and some infra tests call `StorageAnalyzer` / build aggregate-behavior offline without full domain analyze. |
| **Blocks** | Catching hierarchy-sensitive emit regressions; safe refactor of Storage into domain pipeline |
| **Mitigation** | DAU **D3.6b** |

#### P1.3 Transport vs RestApi mental model still easy to get wrong

| | |
|--|--|
| **Symptom** | Infra NEXT still lists “RestApiSurfacePass” as analysis-like pull; TransportPass registered in codegen with “unused consumer” comments. |
| **Blocks** | Bar B / MinimalApi growth if someone re-adds RestApi bags on domain analysis |
| **Mitigation** | Document firmly: **domain Transport facts** in Analysis; **RestApi is a transport implementation** that consumes them (DAU §3). Implement RestApi emit only after D3.3. |

#### P1.4 Effect / policy surface complexity concentration

| | |
|--|--|
| **Symptom** | `EffectAnalyzer` ~1k LOC; many effect kinds; PolicyEvaluator still exposes LINQ dual-oracle on product type; JSON policy vs DSL quantifier split documented as weaker. |
| **Blocks** | E5 micro-tools, Q4 aggregates, new effect kinds — high risk of special cases in the mega-analyzer |
| **Mitigation** | Keep VM-primary; push dual-oracle to tests only; grow effects via shared flatten helpers + tests, not parallel validators; consider splitting EffectAnalyzer by concern only after D3 (not before) |

#### P1.5 Plan / inventory / code drift

| | |
|--|--|
| **Symptom** | Multiple “CURRENT” narratives (APM complete, infra complete, DAU mid); inventory and CORE lag mid-migration wording; agent pick thrash. |
| **Blocks** | Agents reopen finished suites or delete “unused” pack surfaces |
| **Mitigation** | Single primary pick = DAU D3; treat this doc as velocity map; D4.2 docs sync |

---

### P2 — Future velocity / maintainability

#### P2.1 Legacy “V3” naming everywhere

| | |
|--|--|
| **Symptom** | Types, MCP docs, README still say V3 after V2 delete. |
| **Blocks** | Clarity for new contributors; post-v2 naming cleanup plan exists but idle |
| **Mitigation** | [`post-v2-delete-naming-cleanup.md`](post-v2-delete-naming-cleanup.md) after DAU D3 green |

#### P2.2 HttpFile still string emit

| | |
|--|--|
| **Symptom** | DbContext/Program IR; `.http` StringBuilder. |
| **Blocks** | Structural tests / agent edits of HTTP surface; full Bar parity |
| **Mitigation** | Pull when RestApi transport emit needs IR; not P0 for DAU |

#### P2.3 MCP tool surface sprawl without fact layer

| | |
|--|--|
| **Symptom** | Many evolve/query tools; effect authoring still mostly `apply_dsl`; missing `remove_constraint`, `unlink_instances`, E5 micro-tools. |
| **Blocks** | Fine-grained agent edits without DSL round-trips |
| **Mitigation** | Prefer DSL + facts exposure (D3.4b) before inventing many micro-tools; pull E5/unlink only with dogfood pain |

#### P2.4 Dual analysis worlds (Domain vs Interpretation)

| | |
|--|--|
| **Symptom** | DomainModelAnalyzer vs Interpreter.Analyze — intentional, but agents confuse “analysis” bags. |
| **Blocks** | Wrong metadata lookup when lowering DE → AST |
| **Mitigation** | CORE already states separation; keep PolicyEvaluator as explicit bridge; don’t merge pipelines |

#### P2.5 Deferred module split (`Poly.Ast` / `Poly.Analysis`)

| | |
|--|--|
| **Symptom** | CORE notes future split; do not execute mid-product work. |
| **Blocks** | Nothing now; **will** thrash every namespace if done during DAU |
| **Mitigation** | After product stability only |

#### P2.6 Always-on pass cost growth

| | |
|--|--|
| **Symptom** | ~18 domain analyzers; Storage always-on will add cost per `Apply`. |
| **Blocks** | Large multi-entity dogfood sessions if unmeasured |
| **Mitigation** | Structural failure short-circuit; measure after D3; incremental analyze already used |

---

## 4. Planned features × pain matrix

| Planned / likely feature | Hardest pain points | Need first |
|--------------------------|---------------------|------------|
| **DAU D3 storage/transport always-on** | P0.1, P0.2, P0.4, P1.2 | D3.0 → D3.1–D3.5 |
| **Multi-DBMS / pack plugins** | P0.1, P0.2 | D3.1 + D3.4 |
| **RestApi / richer MinimalApi** | P0.1, P1.3 | Domain Transport (D3.3) then emit consumer |
| **MCP dogfood / E5 tools** | P0.3, P2.3 | D3.4b then dogfood-driven tools |
| **Q4 aggregates / date ops** | P1.4, DE lower + EffectAnalyzer | Kernel path green; avoid second eval story |
| **unlink_instances MCP** | Runtime tool honesty only | Small if library exists |
| **Bar B string oracle** | P2.2, generator completeness | Pull only |
| **V3 naming cleanup** | P2.1 | After D3 |
| **Ast/Analysis module split** | P2.5 | Far future |

---

## 5. Recommended sequence (velocity-maximizing)

```text
1. D3.0  StoragePass fail-closed + test          ← contract, unblocks trust
2. D3.1  Analyze(domain, authoringContext?)      ← pack seam
3. D3.4  Thread context on MCP evolve/analyze
4. D3.2–D3.3  Storage+Transport on domain pipeline (Analysis-owned algorithms)
5. D3.4b MCP structured facts from LatestAnalysis
6. D3.5  DslCompiler emit-first
7. D3.6 / D3.6b  Tests + helper retarget
8. D3.7  Gate; then D4 docs
9. Optional: D2.1–D2.3 walk unify once always-on surface is stable
10. Then: packs / RestApi emit / Q4 / E5 as real consumers pull
```

**Do not** start RestApiSurfacePass, StorageAccessPass, or Ast split before steps 1–7.  
**Do not** invent MCP micro-tools that re-derive hierarchy—the data is already on `LatestAnalysis`.

---

## 6. Anti-patterns that will make the future harder

1. Second analysis pipeline “just for this feature”  
2. RestApi or route bags on domain analysis (transport **implementation** belongs at emit)  
3. Pack logic only in DslCompiler  
4. Tests that bypass `DomainModelAnalyzer` for product-shaped IR  
5. Deleting Transport / coupling as “unused” before packs/emit consume them  
6. Growing EffectAnalyzer with one-off special cases without shared walks + goldens  
7. Claiming suite Complete while dual homes remain (honesty tax for every agent)

---

## 7. Success signal

Feature work becomes **local**: new domain fact = one Analysis analyzer + metadata + optional MCP projection; new host surface = emit consumer of existing bags; new pack = context maps/conventions/PassRegistry on the **same** analyze path. No new private pipeline per feature.
