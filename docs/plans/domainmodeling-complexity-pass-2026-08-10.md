# DomainModeling complexity pass — 2026-08-10

**Kind:** Survey only — inventory + recommendations. No deletions in this change.
**Method:** reachability analysis (`rg` for product/test callers), LOC survey, pass/metadata consumer check. Every claim below cites the code.
**Scope:** `Poly/DomainModeling/**` (~19.6k LOC).

---

## Status

**Survey 2026-08-10.** **Cuts #1 + #7 EXECUTED 2026-08-10** (safe deletions): `Builders/` (8 files, 585 LOC), `Examples/` (4 files, 443 LOC), `PassRegistry.cs`, `EvolutionTransaction.cs`, `EntityDependencyGraphMetadata.cs` + the dead graph publishing in `CrossReferencePass` (cycle detection + warning kept). Two `StageBuilder` tests rewritten onto the product `Stage`+`Subscriptions` path. All projects build; full suite green (1971). **Placement fix (#4) EXECUTED 2026-08-10:** `PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping` moved to `Poly.Tests/TestHelpers/`; `DomainInputSet` re-verified as product-path (DslCompiler + packs) and left in place. **Roadmap correction (2026-08-10):** contract integration (#3) and `ValueType` (#6) are **planned product surface — kept**, not removal candidates; the missing piece is their DSL/MCP authoring path. **Dormant effects (#5) DELETED 2026-08-10** (agent-added `Link`/`Unlink`/`TransitionRelationship`). **Soft-delete pruned 2026-08-10:** the automatic `IsDeleted` flag (emitted on every entity), the `delete` effect, and the runtime deleted-instance refusal were removed from the core — no implicit tombstone; if soft-delete is ever needed it is a pack concern, not a universal baked-in. Remaining: #8–#10 (defer).

## Summary

~2k LOC (~10% of the module) is **dead or dormant** with no product authoring surface, plus one test-only-helper placement violation cluster. Ordered by value/risk:

| # | Finding | ~LOC | Severity |
|---|---------|------|----------|
| 1 | Builders/ subsystem — zero product callers | 585 | 🔴 delete |
| 2 | Examples/ cluster — no live call sites | 443 | 🔴 delete/move |
| 3 | Contract integration — no authoring surface **yet** | ~330 | ✅ roadmap (kept) |
| 4 | Test-only helpers shipped in product (`PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping`, `DomainInputSet`) | 590 | 🟠 placement |
| 5 | Dormant effects (`Link`/`Unlink`/`TransitionRelationship`) | ~60 | ✅ deleted |
| 6 | `ValueType` — model/evolution/count, no DSL authoring **yet** | ~60 | ✅ roadmap (kept) |
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

## ✅ 3. Contract integration — roadmap feature (kept)

`ImportedContract`, `ContractBinding`, `ContractEndpoint`, `ContractFieldMap`, `ContractEndpointKind` + `Domain.ImportedContracts/ContractBindings` + 8 change records (`AddImportedContractChange` … `RemoveContractFieldMapChange`) + `ContractIntegrationAnalyzer` (~192).

- **No DSL or MCP tool authors contracts today.** Parser emits none; MCP `add`/`remove` kinds are entity/property/stage/action/stage_action/relationship/constraint/policy. The DSL guide documents no contract syntax.
- Only consumers today are the model container and the evolution/lint passes.

**Roadmap (2026-08-10):** contract integration is **planned product surface** — the missing piece is the authoring path (DSL + MCP) that the model, evolution, and `ContractIntegrationAnalyzer` already anticipate. Not a removal candidate; the work is to build the authoring surface, not delete the substrate.

## 🟠 4. Test-only helpers shipped in product assembly

| Type | Was located at | LOC | Used by |
|------|----------|-----|---------|
| `PolicyEvaluator` + `PolicySubject` | `Lowering/` | 227 | tests only — the runtime `EvaluatePolicy` (`DomainEntityInstance.cs:180`) uses `DomainExpressionLoweringPass` + VM directly; MCP `simulate_policy` uses VM |
| `ClrTypeEntityMapping` (+ `AddEntityFrom<T>`) | `Bootstrap/` | 221 | `Poly.Tests/TestHelpers/DomainTypeMapper.cs` only |

**Placement violation** of the AGENTS rule "Helpers under Poly.Tests/TestHelpers are test-only — never promote into core Poly/."

**Correction (2026-08-10):** `DomainInputSet`/`DomainInputBuilder` was initially grouped here but is **product-path** — the DslCompiler's public `Compile(..., DomainInputSet inputs, ...)` API and the SqlServer/other packs' `Add*Defaults(this DomainInputBuilder)` extensions use it. **Not a placement violation; keep in Poly/.**

**FIXED 2026-08-10:** `PolicyEvaluator`, `PolicySubject`, `ClrTypeEntityMapping` moved to `Poly.Tests/TestHelpers/` (namespaces preserved, so no consumer churn; the types are now unreachable from product by construction). One `IDictionary` qualification fix for the test project's implicit usings.

## ✅ 5. Dormant effects — DELETED 2026-08-10

`LinkRelationshipEffect`, `UnlinkRelationshipEffect` (no DSL syntax per guide §9), `TransitionRelationshipEffect` (guide: "IR exists but **not executed at runtime**") — agent-added effects with no authoring surface and (for transition-relationship) no execution path.

**Deleted:** the three effect records, their `EffectDispatch` methods, runtime routing (`ExecuteLink`/`ExecuteUnlink`/`ResolveLinkedInstance`), analyzer validation (`ValidateRelationshipName`/`ValidateTransitionRelationship` + the DMEFF005 `EffectNotExecutable` code), lowering/printer describe arms, and the subscription effect-classification arms. 5 tests removed. Linking existing instances remains available via MCP `link_instances` → `DomainInstanceStore.Link` (documented in the DSL guide §9).

## ✅ 6. `ValueType` — roadmap feature (kept)

`ValueType` (10), `AddValueTypeChange`/`RemoveValueTypeChange` (`DomainEvolution.AddValueType`), `DomainOverview.ValueTypeCount`. The DSL guide currently says `value { }` is not supported.

**Roadmap (2026-08-10):** value types are **planned product surface** — the missing piece is the DSL `value { }` authoring path (parse → model → analyze → export) that the model + evolution + count surface already anticipate. Not a removal candidate; the work is the authoring surface.

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
- `ContractIntegration` — kept (#3 is roadmap); the analyzer is the lint side of the coming authoring surface.

## 🟡 9. `DomainChange` — 59 records / 1169 lines

The evolution layer's kitchen sink. The contract (#3) and value-type (#6) records are **roadmap surface — keep**. The dormant-effect records are gone (effects pruned, #5). Dormant relationship-content records (`SetRelationshipShape`, rel-level property/stage/policy changes) remain consolidation candidates once confirmed unreachable. Consolidate the remaining property/stage/action/policy records with the `AppendChildToEntity` helpers already in `DomainMutationContext`.

## 🟡 10. Live-path monoliths (defer; structural)

- `DomainEntityInstance.cs` (1405): instance + nested `EffectExecutor` dispatch + a large `PreprocessEffectExpressions` switch + subscription notify + cross-entity invoke. Splitting `EffectExecutor`/preprocessing out is low-risk and would shrink the file ~20%.
- `DomainToCSharpExporter.cs` (1555): large but internally consistent; the `Relationship` node payloads it must render (`SourceOwnsTarget`, rel-level `Properties`/`Stages`/`Policies`) are dormant legacy — removing them (plan `relationship-domain-model-synthesis` phase 6) simplifies the exporter.

---

## Recommendations (proposed order)

1. ~~Delete `Builders/`, `Examples/`, `PassRegistry`, `EvolutionTransaction`; drop `EntityDependencyGraphMetadata` publishing. (🔴 #1, #2, #7)~~ — **DONE 2026-08-10**, ~1.1k LOC removed.
2. **Move** `PolicyEvaluator`/`PolicySubject`/`ClrTypeEntityMapping`/`DomainInputSet` to `Poly.Tests/TestHelpers/` or delete. (#4)
3. ~~**Retire dormant effects** (#5)~~ — **DONE 2026-08-10** (agent-added; no authoring surface). Contract integration (#3) and `ValueType` (#6) are **roadmap features, kept**.
4. **Decide + test** the four lint passes (#8).
5. **Consolidate** `DomainChange`/`DomainEvolution` after the dormant records go (#9). Split `EffectExecutor` out of `DomainEntityInstance` (#10, optional).

## Cross-cutting process note

This pass found **no compile oracle** for the exported C# (R6 from the relationship-refactor review). The recurring CS7036/CS1501 class argues for an in-suite Roslyn compile smoke before any further exporter work. The structural arity guard added for R1 is a stopgap.

## Non-findings (checked, intentionally kept)

- `DslGrammar`/`DslTokenReader`/`DslExpressionParser` — live "pure product DSL path".
- `LinqExpressionGenerator` (LINQ evaluator) — intentionally kept as oracle (documented in `dead-dual-inventory-2026-08-08.md`).
- `DomainFactory` — live (MCP bootstrap).
- Annotations/`Facet`/`AnnotationRegistry` — live (guide documents `column`/`table`).
- `EqualityConstraint` — legacy, un-authorable but handled in printer/export/validation; fold into a future constraint-surface cleanup.
