# DAS W3.2 — Split validation diagnostics from fact emitters

**Wave:** W3 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.1, W3  
**Difficulty:** Large (slice-friendly)  
**Status:** `[x]`  
**Prereq:** W3.1  

## Objective

Start separating megapass “lint everything” from small fact emitters. Do not require finishing all diagnostic packs in one PR—land a clear split for **EffectAnalyzer** and/or **PolicyConstraintAnalyzer** as the template.

## Tasks

- [x] W3.2.1 Identify metadata actually consumed from EffectAnalyzer / PolicyConstraintAnalyzer.
- [x] W3.2.2 Extract fact publication into a small pass or catalog-adjacent step; leave diagnostics in a validate pack.
- [x] W3.2.3 Document optional severity tiers (core vs advisory) if splitting suggestion analyzers.
- [x] W3.2.4 Progress notes: remaining megapass LOC and follow-up tasks if multi-PR.

## Acceptance criteria

- [x] At least one megapass has a clear fact-vs-lint boundary in code or task follow-ups.
- [x] No consumer breaks; build + tests green.
- [x] Follow-ups filed in suite if split incomplete.

## Progress notes

### W3.2.1 Metadata inventory (consumed products)

| Former megapass | Fact product | Consumers | Everything else |
|-----------------|--------------|-----------|-----------------|
| **PolicyConstraintAnalyzer** | `RequiredPropertiesMetadata` on entity (and stage when collectable) | `EffectAnalyzer` (unsatisfied reqs), `RuleCoverageAnalyzer` | Policy expression reference integrity diagnostics |
| **EffectAnalyzer** | `ResolvedRelationshipTargetMetadata` on `CreateEntityInRelationshipEffect` | `EffectLoweringPass` | Binding / ordering / unused-param / invoke-shape / requirement-coverage diagnostics |

No other bags were published by these two passes.

### W3.2.2 Template split (landed)

| Role | Pass | Id | Writes | Deps |
|------|------|----|--------|------|
| **Fact** | `RequiredPropertiesPass` | `DomainRequiredProperties` | `RequiredPropertiesMetadata` | Semantic |
| **Validate** | `PolicyConstraintAnalyzer` | `DomainPolicyConstraint` | diagnostics only | Semantic |
| **Fact** | `EffectFactsPass` | `DomainEffectFacts` | `ResolvedRelationshipTargetMetadata` | Semantic |
| **Validate** | `EffectAnalyzer` | `DomainEffectAnalyzer` | diagnostics only | Semantic, RequiredProperties, ConstraintPropagation |

Registration in `DomainModelAnalyzer.UseDomainModelAnalysisPipeline`: fact emitters registered; consumers depend on fact pass Ids (not validate packs). Tests: `ValidationFactsSplitTests` (metadata present with facts; absent when EffectFacts omitted), `PassDependencyDeclarationTests` updated.

### W3.2.3 Severity tiers (optional / advisory)

Documented convention for further pack splits (not new pipeline stages yet):

| Tier | Severity | Examples today | When analysis fails closed |
|------|----------|----------------|----------------------------|
| **Core** | Error | Structural, semantic ref, effect binding, policy unknown property | Blocks evolution / product paths that require clean analyze |
| **Operability** | Warning | Effect ordering, unsatisfied requirements, non-executable effects | Visible; does not strip facts |
| **Advisory** | Hint / Info | Unused action params, rule coverage gaps, authoring suggestions | Optional packs; safe to severity-filter later |

Suggestion analyzers (`AuthoringSuggestionAnalyzer`, `RuleCoverageAnalyzer`) stay advisory; no severity-filter API in this PR.

### W3.2.4 Remaining megapass LOC + follow-ups

| Unit | ~LOC after W3.2 | Notes |
|------|-----------------|-------|
| `EffectAnalyzer` | ~1000 | Still large **validate** surface (many effect kinds); facts extracted |
| `PolicyConstraintAnalyzer` | ~365 | Quantifier / nav / owned diagnostics remain; facts extracted |
| `EffectFactsPass` | ~90 | Small fact emitter |
| `RequiredPropertiesPass` | ~120 | Small fact emitter |

**Follow-ups (not this task):**

1. Further split Effect validate into per-concern packs (binding vs ordering vs invoke-shape) only if a second consumer needs independent enablement.
2. Stage-level `RequiredPropertiesMetadata` still uses a limited collect walk (historical); improve when a consumer needs stage-keyed required props.
3. Optional: have `EffectAnalyzer.ValidateCreateEntityInRelationship` call `EffectFactsPass.TryResolveCreateIn` to delete dual resolve (kept separate messages for now).
4. Wave gate G3.3/G3.4 bookkeeping when convenient.

### Verify (2026-07-31)

**Implement (green):** Build `Poly.Benchmarks` green. Targeted tests green: ValidationFactsSplit (3), PassDependencyDeclaration order/deps, PipelineMergeMetadataTests required bag, DomainAnalysis_AllPasses, EffectBinding_*, EffectUnsatisfied*, Entity/Stage/ActionPolicy_*, EffectLowering_UsesResolved*, CreateIn*.

**Static AC review (verify pass, severity nit):**

| Check | Evidence |
|-------|----------|
| Fact emitters | `RequiredPropertiesPass` (`DomainRequiredProperties`) → `RequiredPropertiesMetadata`; `EffectFactsPass` (`DomainEffectFacts`) → `ResolvedRelationshipTargetMetadata` via `TryResolveCreateIn` |
| Lint-only | `PolicyConstraintAnalyzer` zero `SetMetadata`; `EffectAnalyzer` zero `SetMetadata` |
| Deps / order | `EffectAnalyzer` deps Semantic + RequiredProperties + ConstraintPropagation (**not** EffectFacts); `RuleCoverageAnalyzer` → RequiredProperties; pipeline registers fact emitters then validate packs |
| Consumers | Unsatisfied-req paths `GetMetadata` RequiredProperties; `EffectLoweringPass` still `GetMetadata` ResolvedRelationshipTarget |
| Docs | CORE §3.1 W3.2 note; this file inventory + severity tiers + remaining Effect ~1k LOC follow-ups; `das-README` W3 `[x]`; gate G3.3/G3.4 |
| Nit / follow-up | Dual create-in resolve (facts pass + EffectAnalyzer validate path) both correct; delete dual only if messages unified later |
| Suite re-run | Full build/suite **not** re-run in this verify pass (static AC) |
