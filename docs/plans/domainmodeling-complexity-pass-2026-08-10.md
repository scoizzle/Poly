# DomainModeling complexity pass — 2026-08-10

**Kind:** Survey only — inventory + recommendations. No deletions in this change.
**Method:** reachability analysis (`rg` for product/test callers), LOC survey, pass/metadata consumer check. Every claim below cites the code.
**Scope:** `Poly/DomainModeling/**` (~19.6k LOC).

---

## Status

**Survey 2026-08-10.** **Cuts #1 + #7 EXECUTED 2026-08-10** (safe deletions): `Builders/` (8 files, 585 LOC), `Examples/` (4 files, 443 LOC), `PassRegistry.cs`, `EvolutionTransaction.cs`, `EntityDependencyGraphMetadata.cs` + the dead graph publishing in `CrossReferencePass` (cycle detection + warning kept). Two `StageBuilder` tests rewritten onto the product `Stage`+`Subscriptions` path. All projects build; full suite green (1971). **Placement fix (#4) EXECUTED 2026-08-10:** `PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping` moved to `Poly.Tests/TestHelpers/`; `DomainInputSet` re-verified as product-path (DslCompiler + packs) and left in place. Remaining: #3 (contract integration retirement), #5/#6 (decide), #8–#10 (defer).

## Summary

~2k LOC (~10% of the module) is **dead or dormant** with no product authoring surface, plus one test-only-helper placement violation cluster. Ordered by value/risk:

| # | Finding | ~LOC | Severity |
|---|---------|------|----------|
| 1 | Builders/ subsystem — zero product callers | 585 | 🔴 delete |
| 2 | Examples/ cluster — no live call sites | 443 | 🔴 delete/move |
| 3 | Contract integration — dormant, no authoring | ~330 | 🔴 retire/quarantine |
| 4 | Test-only helpers shipped in product (`PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping`, `DomainInputSet`) | 590 | 🟠 placement |
| 5 | Dormant effects (`Link`/`Unlink`/`TransitionRelationship`) | ~60 | 🟠 remove |
| 6 | `ValueType` — model/evolution/count, no authoring | ~60 | 🟠 retire |
| 7 | Dead singles: `PassRegistry`, `EvolutionTransaction` (`[Obsolete]`), `EntityDependencyGraphMetadata` (published, never read) | ~95 | 🟠 delete |
| 8 | Lint-only passes with ~zero test coverage | ~500 | 🟡 decide |
| 9 | `DomainChange` 59 records / 1169 lines — kitchen sink | — | 🟡 consolidate |
| 10 | `DomainEntityInstance` monolith (1405) + `DomainToCSharpExporter` (1555) | — | 🟡 split |

---

## 🔴 1. `Builders/` subsystem — zero product callers

`Poly/DomainModeling/Builders/` (8 files, 585 LOC): `DomainBuilder`, `EntityBuilder`, `StageBuilder`, `ActionBuilder`, `RelBuilder`, `ValueBuilder`, `OnEntryBuilder`, `CreateEffectBuilder`.

- **Zero instantiations outside the folder** — the only `new *Builder(...)` calls are inside `Builders/` itself.
- Referenced only by `Examples/PersonLifecycleViaBuilders.cs` (also dead) and comments.

The product authoring path is `DomainEvolution` (`DomainFactory.Create`, used by MCP `create_domain_session`). This is a second, unmaintained builder vocabulary. **§3/§6: delete.** If any builder shape is wanted later, extract from a second real use.

## 🔴 2. `Examples/` cluster — no live call sites

`Poly/DomainModeling/Examples/` (443 LOC): `PersonLifecycleExample`, `PersonLifecycleViaBuilders`, `Demos/LibraryDomain`, `Demos/ECommerceDomain`.

- **No live calls** — referenced only in comments (e.g. `DomainEvolutionApplicatorTests.cs:674`, `Builders/DomainBuilder.cs:131`). The tests re-implement the same domains inline.
- Shipped inside the product assembly (`Poly.csproj`).

**Delete, or move the two `Demos/*` that tests mirror into `Poly.Tests/` as fixtures.**

## 🔴 3. Contract integration — dormant, no authoring surface

`ImportedContract`, `ContractBinding`, `ContractEndpoint`, `ContractFieldMap`, `ContractEndpointKind` + `Domain.ImportedContracts/ContractBindings` + 8 change records (`AddImportedContractChange` … `RemoveContractFieldMapChange`) + `ContractIntegrationAnalyzer` (~192).

- **No DSL or MCP tool authors contracts.** Parser emits none; MCP `add`/`remove` kinds are entity/property/stage/action/stage_action/relationship/constraint/policy. The DSL guide documents no contract syntax.
- Only consumers are the model container and the evolution/lint passes.

**§3: retire or quarantine** — either delete the surface or move it behind a documented extension point with a real consumer. This is the largest single dormant subsystem.

## 🟠 4. Test-only helpers shipped in product assembly

| Type | Was located at | LOC | Used by |
|------|----------|-----|---------|
| `PolicyEvaluator` + `PolicySubject` | `Lowering/` | 227 | tests only — the runtime `EvaluatePolicy` (`DomainEntityInstance.cs:180`) uses `DomainExpressionLoweringPass` + VM directly; MCP `simulate_policy` uses VM |
| `ClrTypeEntityMapping` (+ `AddEntityFrom<T>`) | `Bootstrap/` | 221 | `Poly.Tests/TestHelpers/DomainTypeMapper.cs` only |

**Placement violation** of the AGENTS rule "Helpers under Poly.Tests/TestHelpers are test-only — never promote into core Poly/."

**Correction (2026-08-10):** `DomainInputSet`/`DomainInputBuilder` was initially grouped here but is **product-path** — the DslCompiler's public `Compile(..., DomainInputSet inputs, ...)` API and the SqlServer/other packs' `Add*Defaults(this DomainInputBuilder)` extensions use it. **Not a placement violation; keep in Poly/.**

**FIXED 2026-08-10:** `PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping` moved to `Poly.Tests/TestHelpers/` (namespaces preserved, so no consumer churn; the types are now unreachable from product by construction). One `IDictionary` qualification fix for the test project's implicit usings.

## 🟠 5. Dormant effects — never authorable, partly never executed

`LinkRelationshipEffect`, `UnlinkRelationshipEffect` (no DSL syntax per guide §9), `TransitionRelationshipEffect` (guide: "IR exists but **not executed at runtime**").

- Runtime `ExecuteEffect` still routes them; `EffectExecutor` handles them; `PreprocessEffectExpressions` (`DomainEntityInstance.cs:451-456`) rewrites them; lowering/printer describe them.
- The DSL cannot produce them (no `link`/`unlink`/transition-relationship keyword).

**Remove the effect types + their dispatch/preprocess/printer arms (~60 LOC) once confirmed unreachable from any test.**

## 🟠 6. `ValueType` — model + evolution + count, no authoring

`ValueType` (10), `AddValueTypeChange`/`RemoveValueTypeChange` (`DomainEvolution.AddValueType`), `DomainOverview.ValueTypeCount`. The DSL guide: `value { }` not supported. Only authoring is the dead `PersonLifecycleExample`. **Retire** (remove the type, change records, count surface) unless value types are on the roadmap.

## 🟠 7. Dead singles

- **`PassRegistry.cs`** (27) — zero references.
- **`EvolutionTransaction.cs`** (30) — `[Obsolete("... was removed ...")]`, only self-reference. Delete.
- **`EntityDependencyGraphMetadata`** — `CrossReferencePass` publishes a dependency graph (`edges`) that **no consumer reads** (0 non-pass references). Only the cycle *warning* is consumed. Publish nothing; keep only the cycle detection. (~15 LOC of dead synthesis.)

## 🟡 8. Lint-only analysis passes with ~zero test coverage

`ConstraintQualityAnalyzer` (192), `RuleCoverageAnalyzer`, `ContractIntegrationAnalyzer` (lints the dormant #3 subsystem), `AuthoringSuggestionAnalyzer` (132, feeds MCP `get_domain_suggestions`). All four emit agent-visible diagnostics but their messages are asserted by **no test** (only generic pass-registration tests reference them).

**Decision needed per pass** (§3/§7 — "no new process/facts without consumers"; here the question is whether existing consumers exist):
- `AuthoringSuggestion` — consumed by MCP suggestions; **keep**, add a smoke test.
- `ConstraintQuality` — genuinely useful checks (unsatisfiable `range(min>max)`, incompatible type override); **keep + test** the min>max check.
- `RuleCoverage` — may duplicate the newer required-props machinery (`RequiredPropertiesPass`/`EffectAnalyzer`); verify overlap, then cut one.
- `ContractIntegration` — retire with #3.

## 🟡 9. `DomainChange` — 59 records / 1169 lines

The evolution layer's kitchen sink. ~15 records are for dormant subsystems (#3: 8 contract records; #5: `SetRelationshipShape` + relationship-content records; #6: `AddValueType`). Once those are removed the file roughly halves. `DomainEvolution` (526) shrinks with it. Consolidate the remaining property/stage/action/policy records with the `AppendChildToEntity` helpers already in `DomainMutationContext`.

## 🟡 10. Live-path monoliths (defer; structural)

- `DomainEntityInstance.cs` (1405): instance + nested `EffectExecutor` dispatch + a large `PreprocessEffectExpressions` switch + subscription notify + cross-entity invoke. Splitting `EffectExecutor`/preprocessing out is low-risk and would shrink the file ~20%.
- `DomainToCSharpExporter.cs` (1555): large but internally consistent; the `Relationship` node payloads it must render (`SourceOwnsTarget`, rel-level `Properties`/`Stages`/`Policies`) are dormant legacy — removing them (plan `relationship-domain-model-synthesis` phase 6) simplifies the exporter.

---

## Recommendations (proposed order)

1. ~~Delete `Builders/`, `Examples/`, `PassRegistry`, `EvolutionTransaction`; drop `EntityDependencyGraphMetadata` publishing. (🔴 #1, #2, #7)~~ — **DONE 2026-08-10**, ~1.1k LOC removed.
2. **Move** `PolicyEvaluator`/`PolicySubject`/`ClrTypeEntityMapping`/`DomainInputSet` to `Poly.Tests/TestHelpers/` or delete. (#4)
3. **Retire** contract integration (#3), `ValueType` (#6), dormant effects (#5). Largest surface; needs the "is this on a roadmap?" decision.
4. **Decide + test** the four lint passes (#8).
5. **Consolidate** `DomainChange`/`DomainEvolution` after the dormant records go (#9). Split `EffectExecutor` out of `DomainEntityInstance` (#10, optional).

## Cross-cutting process note

This pass found **no compile oracle** for the exported C# (R6 from the relationship-refactor review). The recurring CS7036/CS1501 class argues for an in-suite Roslyn compile smoke before any further exporter work. The structural arity guard added for R1 is a stopgap.

## Non-findings (checked, intentionally kept)

- `DslGrammar`/`DslTokenReader`/`DslExpressionParser` — live "pure product DSL path".
- `LinqExpressionGenerator` (LINQ evaluator) — intentionally kept as oracle (documented in `dead-dual-inventory-2026-08-08.md`).
- `DomainFactory` — live (MCP bootstrap).
- Annotations/`Facet`/`AnnotationRegistry` — live (guide documents `column`/`table`).
- `EqualityConstraint` — legacy, un-authorable but handled in printer/export/validation; fold into the #5/#3 cleanup.
