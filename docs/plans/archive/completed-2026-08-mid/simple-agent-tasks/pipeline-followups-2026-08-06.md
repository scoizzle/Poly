# Pipeline follow-ups — 2026-08-06 (phenomenal review)

**Review:** [`../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md`](../../agent/reviews/2026-08-06-pipeline-amu-p4-coh-dogfood.md)  
**Scope:** Uncommitted amu / p4 / coh / dogfood work claimed complete in PIPELINE-STATUS  
**Status:** all items closed 2026-08-06 (F1–F7, P1) — see notes below

## Open

- [x] **F1** — EffectAnalyzer: RLM/catalog resolve parity with Policy + declare `DomainCatalogPass` dependency; bag-missing on domain-bound analyze → diagnostic fail-closed (not silent skip). Tests for stripped catalog / missing bag. (`EffectAnalyzer.cs` TryResolve*)
      **Closed:** `ResolveRelationshipLookup`/`ResolveTypeLookup` = catalog-first ?? intermediate bag (Policy parity); `Dependencies` += `DomainCatalogPass.Id`; `ReportCatalogUnavailable` structural failure on bag-missing (incl. `ValidateDomainEffects` DTLM-null gate). Tests: `Poly.Tests/DomainModeling/Analysis/EffectAnalyzerFailClosedTests.cs` (RLM fallback, unknown-rel error, no-bags fail-closed).
- [x] **F2** — Plan honesty: reopen or residual-mark amu gate G2/G7 and PIPELINE-STATUS until F1 done or human-waived; fix gate text that equates “skip” with fail-closed.
      **Closed:** F1 landed → amu-gate G2/G3/G7 reworded (bag-skip is not fail-closed) and `[x]`; PIPELINE-STATUS → all `done`, CURRENT `(none)`, blocker removed. P1 adds the gate DoD so the wording cannot regress.
- [x] **F3** — EffectFactsPass create-in resolve via catalog/RLM helpers (sibling of W1); align with EffectAnalyzer.
      **Closed:** `TryResolveCreateIn` now takes `AnalysisContext` and resolves via catalog-first ?? RLM; no `domain.Relationships.FirstOrDefault`. Test: facts published from Semantic-only context (RLM fallback).
- [x] **F4** — AMU residual scan inventory: EffectLowering analysis-null path, exporter enum/rel FirstOrDefault, StorageAnalyzer OfType enum — document R-rows; analysis-present must not scan.
      **Closed:** rows R12–R22 already inventoried; §3.4 post-review verification added. R14 (`ClassifyProperties` enum map) fixed — DTLM-first when analysis present; tree scan is analysis-absent residual only.
- [x] **F5** — P4: singular + any/all → error (or guide-documented warn); test.
      **Closed:** `SubscriptionAnalyzer` quantifier-vs-cardinality promoted to `ReportError`; tests renamed/assert `DiagnosticSeverity.Error` (`Analyze_AnyQuantifierOnOneToOne_ReportsError`, `Analyze_DslWhenAnyOnOneToOne_ReportsError`); p4-gate/p4-2 notes updated.
- [x] **F6** — Guide: reserve `any`/`all` as quantifier keywords (relationship name collision).
      **Closed:** poly-dsl-guide §7 — reserved-keyword note (a relationship named `any`/`all` cannot be the nav in `when`; rename it) + singular quantifier now “rejected at analysis time (error DMSS003)”.
- [x] **F7** — Optional: MCP SubscriptionPlanFact include quantifier; identity rewrite exhaustiveness test for DE subtypes.
      **Closed:** `SubscriptionPlanFact.Quantifiers` (distinct per relationship) surfaced in `get_domain_analysis` + smoke-test assertion; `DomainExpressionRewriteIdentityTests` covers all 20 DE subtypes round-tripping identity rewrite (incl. bare Count).

## Disposition

| Item | Status |
|------|--------|
| F1 (EffectAnalyzer parity + fail-closed) | Fixed 2026-08-06 |
| F2 (plan honesty) | Closed 2026-08-06 — gates reworded + `[x]` |
| F3 (EffectFactsPass resolve) | Fixed 2026-08-06 |
| F4 (residual inventory + R14) | Fixed 2026-08-06 |
| F5 (singular+any/all → error) | Fixed 2026-08-06 |
| F6 (guide reserved keywords) | Fixed 2026-08-06 |
| F7 (plan fact quantifier + rewrite test) | Fixed 2026-08-06 |
| P1 (gate DoD) | Added 2026-08-06 — SUITE-OF-SUITES §8 |
| Prior suite follow-ups | N/A (first pipeline review) |

## Process fix

- [x] **P1** — Suite gate DoD: “fail-closed” may not be redefined as “skip validation.” Add checklist item: bag-unavailable behavior + Dependencies edge to publisher pass.
      **Closed:** SUITE-OF-SUITES.md §8 Gate DoD — fail-closed ≠ skip; bag-unavailable behavior tested; Dependencies edge to publisher; gate notes state the real contract.
