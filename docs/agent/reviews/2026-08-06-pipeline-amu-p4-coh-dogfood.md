# Phenomenal review — pipeline uncommitted (amu / p4 / coh / dogfood)

- **Target**: local uncommitted (staged + unstaged + untracked sources related to suite-of-suites execution)
- **Mode**: standard (reviewer not implementer of production code; session authored suite docs only)
- **Issue counts**: 3 bugs, 5 suggestions, 2 nits
- **Verdict**: **Not ship** as “all suites complete” until **F1–F3** closed (or explicitly re-scoped with honest gates). Production direction is largely sound; plan honesty and catalog soft-skip are the ship blockers.
- **Process notes**: PIPELINE-STATUS and suite gates claim full Done + “pre-ship clean / fail-closed preserved” while residual dual paths and soft-skip remain. Gate G7 text redefines “fail-closed” as “skip validation” — process bug.

## Summary

Uncommitted tree implements most of **dogfood wave 2**, **amu**, **p4**, and **coh** (Runtime/ move, DE rewrite base, EffectValidationDispatch, evolution helpers, catalog-oriented resolve, P4 any/all parse, MCP aggregate/subscription plan facts, bag-path exists fail-closed). Catalog is registered before EffectAnalyzer in `DomainModelAnalyzer`, so happy-path full analyze usually has bags. Residual risk is **present-but-soft** validation when domain-keyed catalog helpers return null (no RLM fallback on EffectAnalyzer), **sibling residual scans** (EffectFactsPass, exporter/storage enum paths, EffectLowering analysis-null scan), and **docs overclaim** (all gates `[x]`, pipeline complete).

Oracle strength: large TUnit surface including new P4 goldens and analysis tests; not re-run in this review session (treat green claims as unverified here).

## Issues

### Issue 1 -- Severity: bug

- **File:** `Poly/DomainModeling/Analysis/EffectAnalyzer.cs:407–414` (and all `if (!TryResolveRelationship(...)) return;` call sites ~346–347, 441–442, 556–557, 940+)
- **Description:** `TryResolveRelationship` / `TryResolveEntity` use only `context.GetRelationshipLookup(domain)` / `GetTypeLookup(domain)`, which are **catalog-only when domain is non-null** (`DomainSemanticLookupExtensions` returns null if catalog missing). On false they **skip validation** with no diagnostic. PolicyConstraintAnalyzer and SubscriptionAnalyzer intentionally fall back to intermediate RLM; EffectAnalyzer does **not**. EffectAnalyzer’s `Dependencies` omit `DomainCatalogPass` — full-pipeline order is accidental. Any partial pipeline, reordering, or failed catalog publish yields **present analysis context + silent omit of create/invoke/rel checks** (vacuous success for bad effects).
- **Suggestion:** (1) Add `DomainCatalogPass.Id` to EffectAnalyzer Dependencies **and/or** mirror Policy’s `GetRelationshipLookup(domain) ?? GetMetadata<RLM>(default)`. (2) When analysis is domain-bound and bags still missing after deps, **fail closed** (error on domain node), not skip. (3) Test: pipeline without catalog / stripped catalog → must not green-accept unknown relationship create-in.
- **Status:** open

### Issue 2 -- Severity: bug

- **File:** `docs/plans/simple-agent-tasks/amu-gate.md:25` (+ PIPELINE-STATUS “all suites complete”)
- **Description:** Gate note claims “fail-closed contract preserved (bag-unavailable → skip, never false-positive).” **Skip is not fail-closed** for required semantic validation; it is soft-open (invalid effects may receive zero diagnostics). Suite READMEs and PIPELINE-STATUS mark amu/p4/coh/dogfood **Done** while Issue 1 and residual scan siblings remain. Plan honesty failure.
- **Suggestion:** Reopen amu gate G2/G7 or add residual follow-ups; change G7 wording to “no false unknown-rel on stripped bags” if that is the real contract; set PIPELINE-STATUS residual/blocker until F1 fixed or waived with human signature.
- **Status:** open

### Issue 3 -- Severity: bug

- **File:** `Poly/DomainModeling/Analysis/EffectFactsPass.cs:75` (sibling to amu W1 create-in resolve)
- **Description:** EffectFactsPass still resolves create-in targets via `domain.Relationships.FirstOrDefault`. EffectAnalyzer now catalog-resolves create-in for **lint**. Fact publication and lint can diverge if IR/catalog disagree; AMU G3 claimed residual scans removed from “the three analyzers” but **fact pack still dual-path** for the same semantic EffectAnalyzer consumers rely on (ResolvedRelationshipTargetMetadata).
- **Suggestion:** Resolve create-in in EffectFactsPass via same catalog/RLM helpers as W1; add test that facts bag matches analyzer under full analyze.
- **Status:** open

### Issue 4 -- Severity: suggestion

- **File:** `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:505–517` and `DomainToCSharpExporter.cs` residual `OfType<EnumType>` / relationship FirstOrDefault (~1192, 1251, 1264)
- **Description:** AMU W3.2 marked complete (“no edit needed”) while analysis-null path still scans domain tree; exporter still has residual enum/rel scans. Acceptable as dual-path for standalone, but gate G5 overclaims “use metadata when Analysis present” completeness without residual inventory.
- **Suggestion:** Grep residual scans into amu inventory R-rows; analysis-present branches must never hit FirstOrDefault; keep analysis-null scan only under explicit reduced contract comments + tests.
- **Status:** open

### Issue 5 -- Severity: suggestion

- **File:** `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs:277–285`
- **Description:** P4-2 quantifier vs cardinality emits a **warning** that Any/All on singular “behaves identically to Each” rather than **error**. Product absorption text said fail-closed; warning allows authoring misleading DSL.
- **Suggestion:** Promote to error (or document intentional warn + guide honesty). Add test for singular+any.
- **Status:** open

### Issue 6 -- Severity: suggestion

- **File:** `Poly/DomainModeling/Parsing/PolyDslParser.cs:769–780` (`ParseSubscriptionQuantifier`)
- **Description:** Quantifier is matched as bare identifier `any`/`all` before relationship name. A relationship literally named `any` or `all` becomes unauthorable as `when any Stage…` (always consumed as quantifier).
- **Suggestion:** Document reserved quantifier tokens in guide; or require `when quantifier(any) Rel` form later. Track as known product limitation in p4 residual.
- **Status:** open

### Issue 7 -- Severity: suggestion

- **File:** `Poly.Mcp/Tools/DomainTools.cs:314–360` (subscription plan projection)
- **Description:** MCP walks `domain.Types.OfType<Entity>()` and stages to collect `SubscriptionDispatchPlanMetadata`. Correct projection, but O(entities×stages) and re-derives from model shape rather than a single catalog/plan index if one exists. Not wrong; scales poorly and skips quantifier in plan facts (only stage names).
- **Suggestion:** Include quantifier in `SubscriptionPlanFact` if agents need any/all honesty; optional.
- **Status:** open

### Issue 8 -- Severity: suggestion

- **File:** `Poly/DomainModeling/Runtime/DomainExpressionRewriteBase.cs:19–23` (`Default` throws)
- **Description:** Sealed Default throws for unhandled subtypes; good for exhaustiveness if Route is exhaustive. If base Route has a default arm that never calls Default, throw is dead. Verify DomainExpressionDispatch.Route exhaustiveness for all current expression types + test adding a new subtype fails compile or test.
- **Suggestion:** Compile-time exhaustiveness or single test that all known DE types round-trip identity rewrite.
- **Status:** open

### Issue 9 -- Severity: nit

- **File:** `Poly/DomainModeling/Runtime/*` still `namespace Poly.DomainModeling`
- **Description:** Folder move without namespace change is intentional churn reduction; README claims Runtime/ but CORE placement may still list root files. Low risk.
- **Suggestion:** One-line CORE/DomainModeling README already partially updated — confirm CORE path strings.
- **Status:** open

### Issue 10 -- Severity: nit

- **File:** Dogfood reports untracked under `docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S*-20260806.*`
- **Description:** Pipeline claims dogfood done; reports exist untracked — fine if intentional; ensure fix G-S6-1 is reflected in dogfood-fix-README status.
- **Suggestion:** Commit reports with code or ignore policy explicit.
- **Status:** open

## Sibling-path checklist (selected)

| Semantic | Paths | Result |
|----------|-------|--------|
| Rel name resolve (lint) | Effect catalog-only; Policy catalog+RLM; Facts domain scan | **Diverge** (Issues 1, 3) |
| Rel name resolve (lower) | Analysis metadata then null; analysis-null scan | Documented dual; residual |
| Quantifier any/all | Parse stamps; store dispatches; analysis warn on singular | Warn ≠ fail-closed (Issue 5) |
| Exists without instanceId | Domain-bound Create for bag eval | Improved (G-S6-1); not re-verified here |

## Reachability (Issue 1)

- On **full** `DomainModelAnalyzer` order, CatalogPass runs before EffectAnalyzer → catalog usually present → soft-skip **not** hit on happy path.
- Soft-skip **is** reachable on: partial analyzer registration, catalog publish failure with later passes still run, unit tests that construct EffectAnalyzer without catalog.
- Severity remains **bug** for contract/docs claiming fail-closed, and for missing Dependency edge.
