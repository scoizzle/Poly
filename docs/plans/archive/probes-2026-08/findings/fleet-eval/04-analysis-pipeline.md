# Fleet-eval 2026-08-12 — Slice 04: Analysis pipeline & metadata

Agent slice: `Poly/DomainModeling/Analysis/` (DomainModelAnalyzer, Structural/Semantic
DomainAnalyzer, RuntimeContractAnalyzer, DomainCatalogPass, CapabilityAnalyzer,
BehaviorPass, CrossReferencePass, EffectTopologyPass, OwnershipAggregatePass,
RequiredPropertiesPass, EffectFactsPass, EffectAnalyzer, diagnostic codes, metadata
records) + `Poly/Analysis/` framework (AnalyzerBuilder, AnalysisContext, NodeMetadataStore,
node replacement).

Probes (all under `probes/fleet-eval/04-analysis-pipeline/`):

| Probe | Result |
|---|---|
| `clinic.poly` (valid library/clinic system: entity+action+stage policies, create-in with DMEFF011 coverage, back-ref auto-wire, cross-entity invoke, fan-out with policy + stage-membership predicates, stage-scoped + entity-level subscriptions incl. `all` quantifier and peer binder, owned nav, enum default, `-> T` return, annotations) | 0/0 PASS |
| `analysis-rejects.poly` (10 isolated analysis rejections: DMEFF009/010/011/007/008/001, unknown policy prop, type-confused default, `-> Number`) | FAILS at analysis (earliest rung), 10/10 correct codes per code-read |
| `edges.poly` (dependency cycle A→B→C→A, orphan Leaf, sparse entity, authoring hints, nav `aRefs` with internal capital) | FAILS at compile: **CS1061 `_aRefs`** |
| `selfrel-createin.poly` (minimal `create in` into a target with a self-relationship collection nav) | FAILS at compile: **CS1503** |
| `cond-return.poly` (guide's ✅ final-conditional `if/else create in` `-> T` return) | 0/0 PASS, but export body **always throws NotSupportedException** |

Findings are ranked: compile-fail first, then divergence/silent gap, then sharp/🟡.

---

## F1 — `create in Rel` into a target with a self-relationship nav → export passes `this` as the child's self-collection → CS1503 compile-fail
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** analysis-pipeline (metadata semantics) / export (arg injection)
- **Lens:** quality (metadata meaning), consistency (facts on nodes consumed wrongly), reliability
- **Repro:** `probes/fleet-eval/04-analysis-pipeline/selfrel-createin.poly`
  - `A { bs: many B; Mk: action { create in bs { } } }` and `B { bs: many B }`
  - `scripts/run-probe.sh probes/fleet-eval/04-analysis-pipeline/selfrel-createin.poly`
  - `error CS1503: Argument 1: cannot convert from 'A' to 'System.Collections.Generic.IEnumerable<B>'` (also B→B self case, line 56)
- **Expected:** `create in bs` creates a B with an empty `bs` collection; the export compiles.
- **Actual:** `EntityStructureAnalyzer.ComputeConstructorParameterOrder` marks *every* self-nav `IsBackReference: true` (rel.Target == entity.Name). The exporter's `DomainToCSharpExporter.AddCreateNavMethod` and `EffectLoweringPass.CreateEntityInRelationship` both treat `IsBackReference` as "wire the create source here" and inject `this` into that ctor arg — for a *collection* self-nav `this` (an `A`) is never an `IEnumerable<B>`. Analysis accepts the shape (recursive modeling is legitimate — comment threads, org charts); the export cannot compile it.
- **Proposed patch:** split the flag — self-relationship ≠ back-reference-to-create-source. Collection self-navs on the target should lower to an empty `List<T>` in `AddCreateNavMethod`/`CreateEntityInRelationship` (same as ordinary collection navs), or analysis should reject `create in` when the target has a self-relationship (fail at earliest rung). The single `IsBackReference` bit is semantically overloaded.

## F2 — camelCase nav name with an internal capital (`aRefs`) → backing-field casing disagreement → CS1061 compile-fail
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** export (name derivation) — found via analysis-pipeline probe
- **Lens:** quality (consumers derive different names from the same metadata), reliability
- **Repro:** `probes/fleet-eval/04-analysis-pipeline/edges.poly` (entity `C { aRefs: many A }`)
  - `scripts/run-probe.sh probes/fleet-eval/04-analysis-pipeline/edges.poly`
  - `error CS1061: 'C' does not contain a definition for '_aRefs'`
- **Expected:** any legal DSL nav name compiles.
- **Actual:** both sites consume the same `EntityStructureMetadata.ConstructorParameters` (`aRefs`), but derive different backing-field names: field emission uses `"_" + ToCamelCase(ToPascalCase(rel.Name))` → `_arefs`, while the ctor assignment uses `"_" + ToCamelCase(navParam.Name)` → `_aRefs`. Field `_arefs` is declared; ctor writes `_aRefs`. Any nav whose second letter is uppercase (e.g. `aRefs`, `ipAddress`, `x509Cert`) breaks the export.
- **Proposed patch:** derive both names from one canonical form (publish the backing-field name as a metadata fact on `EntityStructureMetadata.ConstructorParameterOrder`, or make both sites call the identical `ToCamelCase(ToPascalCase(name))`).

## F3 — `-> T` final-conditional producer (guide ✅) is analysis-accepted but the export emits a body that ALWAYS throws NotSupportedException
- **Signal:** export/runtime divergence + silent gap (0/0 gate passes)
- **Severity:** 🟠
- **Slice:** analysis-pipeline (DMEFF010 gate blesses a shape the export cannot emit; earliest-rung fail-closed not honored) / export (conditional-return lowering unimplemented)
- **Lens:** quality (pass correctness, earliest rung), product (actionable authoring), consistency (guide vs pipeline)
- **Repro:** `probes/fleet-eval/04-analysis-pipeline/cond-return.poly`
  - `Place: action -> Order { if (Rush is true) { create in orders { Code: "rush" } } else { create in orders { Code: "normal" } } }`
  - `scripts/run-probe.sh ...` → 0/0 PASS; generated `Place()`:
    ```csharp
    if (this.Rush) { var order = this.CreateOrders("rush", 0L); }
    else { var order = this.CreateOrders("normal", 0L); }
    throw new NotSupportedException("Action 'Place' has return type but its last effect does not produce a value. ...");
    ```
- **Expected:** per the guide (lines 372-380 the ✅ example, and "Runtime InvokeAction returns the created instance"), the export returns the branch-created instance. The runtime path does.
- **Actual:** the export never returns the created instance — the action is dead (create side-effect happens, then guaranteed throw). The guide documents the shape as correct, analysis DMEFF010 explicitly approves it, yet the export's "return value = last statement" logic only handles a direct create/create-in, not a final conditional. Analysis should reject the shape if the export cannot emit it (fail at earliest rung), or the export must implement branch-return lowering. Also internally inconsistent: DMEFF010 (my slice) blesses the conditional while DMEFF006 (my slice) warns the same nested create-in is "silently dropped at runtime."
- **Proposed patch:** either (a) DMEFF010 rejects conditional producers until the export supports them (one-line change: `LastStatementProducesEntityType` only accepts direct create/create-in), or (b) implement conditional-return lowering in the exporter.

## F4 — Effective-policy composition does not dedupe → duplicate export gates + duplicate failure messages
- **Signal:** silent duplication (behavior preserved, code-quality noise)
- **Severity:** 🟡
- **Slice:** analysis-pipeline (CapabilityAnalyzer `EffectivePolicies`, DomainEffectiveSurface)
- **Lens:** consistency (CORE: one composition algorithm producing the *net* constraint surface), product
- **Repro:** `probes/fleet-eval/04-analysis-pipeline/clinic.poly` — inspect exported `Book()`/`Sweep()`:
  - `Book` declares `require IsVip` and `IsVip` is also an entity-level policy → export emits `if (!this.IsVip()) { return Failure("'Book' blocked by policy 'IsVip'."); }` twice (lines 63-70).
  - `Department.Sweep` same with `HasManyPatients` (twice).
- **Expected:** the effective surface is the *net* guard; a policy appearing once (entity) plus once (action require) should collapse to a single gate.
- **Actual:** `CapabilityAnalyzer.AnalyzeAction` does `[.. inheritedPolicies, .. action.Policies]` with no name-based dedupe; `DomainEffectiveSurface.ComposeStagePolicies` is plain concatenation. Every duplicate propagates into the exported action and into `BehaviorModel` policy lists.
- **Proposed patch:** dedupe by policy name (last-wins) in `AnalyzeAction` and `ComposeStagePolicies` before publishing the capability views.

## F5 — Fact emitters soft-skip bag publication instead of failing closed (inconsistent with CatalogPass/EffectAnalyzer)
- **Signal:** silent gap (latent; unreachable in the full pipeline today)
- **Severity:** 🟡
- **Slice:** analysis-pipeline (RuntimeContractAnalyzer, EffectFactsPass)
- **Lens:** reliability (missing bags → silent skip vs throw), consistency
- **Repro:** static. `RuntimeContractAnalyzer.PublishEntitySubscriptionDispatchPlan`/`PublishSubscriptionDispatchPlan` `return;` silently when `RelationshipContractMetadata` is absent (lines 68-70, 95-97) — no structural failure, no diagnostic. `EffectFactsPass.TryResolveCreateIn` returns false (no fact, no diagnostic) when the RLM is absent. `DomainCatalogPass` and `EffectAnalyzer` both report structural failures for the same class of missing bag (CORE: "missing Semantic DTLM/RLM is a structural failure").
- **Expected:** a required fact bag that cannot be published fails closed (structural diagnostic) so downstream consumers cannot silently run without the fact.
- **Actual:** the three fact emitters have divergent fail-closed postures. Today the bags are always present in the full pipeline (same pass publishes on the Domain visit before children; EffectFacts falls back to the Semantic RLM), so this is latent — but a pass reorder or subtree analysis silently degrades the dispatch-plan / resolved-target facts.
- **Proposed patch:** align the fact emitters: report a structural failure (reuse DMSEM005) instead of `return;` when the dependency bag is absent.

## F6 — CapabilityAnalyzer subtree run yields silently empty stage/action capability views (children analyzed after parent)
- **Signal:** silent empty result (latent — domain-root path is correct)
- **Severity:** 🟡
- **Slice:** analysis-pipeline (CapabilityAnalyzer)
- **Lens:** reliability (silent empty results), quality
- **Repro:** static. `CapabilityAnalyzer.Analyze` switches on `Domain` (returns after pre-walking), `Action`, `Stage` — for a Stage root it calls `AnalyzeStage(context, stage)` BEFORE `AnalyzeChildren`, so `stage.Actions.Select(a => GetMetadata<ActionCapabilityMetadata>(a))` reads not-yet-published child facts → `LocalActions`/`EffectiveActions` empty; the standalone `AnalyzeAction` overload also drops stage policies from effective policies (only entity + action). `Analyzer.Analyze` is public; any subtree root (e.g. a future incremental granularity) silently gets an empty capability view.
- **Expected:** stage/action subtree analysis produces the same capability view as the domain path.
- **Actual:** empty views, no diagnostic. The Domain path is correct (`AnalyzeDomain` pre-walks all members), so the product path is unaffected today.
- **Proposed patch:** make the `case Stage`/`case Action` branches defer to post-order children (analyze children first, then compose the parent view), mirroring `AnalyzeDomain`.

## F7 — EffectAnalyzer/StorageAnalyzer read `ActionInvariantMetadata` without declaring `EffectInvariantAnalyzer` as a dependency (latent ordering fragility)
- **Signal:** silent degradation under reorder (latent — registration order currently masks it)
- **Severity:** 🟡
- **Slice:** analysis-pipeline (pass dependency declarations)
- **Lens:** consistency (pass dependencies sane)
- **Repro:** static. `EffectAnalyzer` reads `context.GetMetadata<ActionInvariantMetadata>(action)` at lines 1145 and 1205 (call-chain postcondition validation and stage-context range narrowing) but declares `Dependencies = [Semantic, Catalog, RequiredProps, ConstraintPropagation]` — not `EffectInvariantAnalyzer.Id`. `StorageAnalyzer` (via StoragePass) reads it too (line 324) without the dependency. The builder currently places EffectInvariantAnalyzer before both because of registration order, so the reads succeed.
- **Expected:** every bag read must be a declared dependency (the framework's contract — "a declared dependency that is not registered produces an error").
- **Actual:** if the registration order in `UseDomainModelAnalysisPipeline` changes (e.g. EffectAnalyzer registered before EffectInvariantAnalyzer), the reads silently return null: `ValidateCallChainPostconditions` returns without validating, `ValidateDerivedValueRange` falls back to declared-range-only inference, and `StorageAnalyzer` drops invariant-verified envelopes — all silently.
- **Proposed patch:** add `EffectInvariantAnalyzer.Id` to `EffectAnalyzer.Dependencies` and to `StoragePass`/`StorageAnalyzer`'s dependency set (or pass the metadata explicitly).

## F8 — DslCompiler surfaces analysis errors without codes and suppresses warnings/hints entirely on the CLI path
- **Signal:** fail-loud-but-sharp (authoring loop dead-ends on code diagnosis)
- **Severity:** 🟡
- **Slice:** product surface around the pipeline (DslCompiler `Compile` takes `.Where(Severity == Error)` and `.Select(d => d.Message)` — codes dropped, warnings/hints dropped)
- **Lens:** product (actionable diagnostics; authoring suggestions reachable), quality (diagnostic code quality)
- **Repro:** `scripts/run-probe.sh probes/fleet-eval/04-analysis-pipeline/analysis-rejects.poly` → messages like "Action 'Mk' declares return type 'Widget' but no create or create-in effect produces an instance of 'Widget'." with **no code** (DMEFF009). Warnings (DMDEP001 cycle, DMAGG001 orphan, DMSS003, DMEFF006, DMAS001/DMBEH001 authoring hints) never surface on the CLI/export path at all — a domain with a dependency cycle exports 0/0 with zero indication.
- **Expected:** analysis diagnostics reach authors with their codes and severities (hints/warnings included), so failures map to the documented codes and advisory suggestions are visible.
- **Actual:** codes are stripped and non-error diagnostics are filtered out before the error list is returned.
- **Proposed patch:** keep `(code, severity, message)` triples in `CompileResult.Errors` (or add a diagnostics surface) and include warnings/hints in the CLI output.

## F9 — `NodeMetadataStore.Get` silently falls back to the global (`NodeId.Empty`) bucket on any per-node miss (metadata-context leakage hazard)
- **Signal:** modeling trap / latent wrong-context read
- **Severity:** 🟡
- **Slice:** analysis framework (NodeMetadataStore)
- **Lens:** security (contexts on wrong nodes), reliability
- **Repro:** static. `NodeMetadataStore.Get<T>(node)` returns the `NodeId.Empty` global value whenever the per-node bucket misses (lines 40-52). A pass that intends a per-node read and *mistakenly* references a node that has no such metadata silently receives the global instance of that metadata type instead of null. Every consumer that treats "non-null metadata" as "this pass published for this node" (e.g. `RequiredPropertiesMetadata`, `SubscriptionDispatchPlanMetadata`, `ResolvedRelationshipTargetMetadata`) is exposed to cross-scope leakage if any pass ever writes that type globally. No current pass writes these types globally, so this is a latent design hazard rather than an active bug.
- **Expected:** global metadata should be opt-in (an explicit `GetGlobal<T>()` API), not a silent fallback for every node-relative read.
- **Actual:** the per-node lookup silently succeeds with wrong-scope data; a future pass storing a per-node-shaped bag globally would corrupt all consumers without a diagnostic.
- **Proposed patch:** split the API — `Get<T>(node)` stays strict (per-node only); global reads require an explicit global accessor. At minimum, document the fallback loudly on `GetMetadata<T>` and audit the ~25 consumers.

## F10 — DomainCatalogPass silently resolves duplicate action names to "last wins" in its action maps
- **Signal:** silent gap (latent — Structural pass reports duplicates in the full pipeline)
- **Severity:** 🟡
- **Slice:** analysis-pipeline (DomainCatalogPass `BuildActionResolution`/`BuildMutationTargetIndex` use `GroupBy(...).Last()`)
- **Lens:** reliability (missing/invalid configs fail loud), quality
- **Repro:** static. `BuildActionResolution` and the mutation-index action/policy maps collapse duplicate names to the last occurrence with no diagnostic of their own. Correctness today depends on `StructuralDomainAnalyzer` running first and flagging the duplicate as DMSTR001.
- **Expected:** the catalog publisher fails closed (or is guaranteed ordered after the structural check) so a duplicate name can never be silently resolved.
- **Actual:** a pipeline containing Semantic + Catalog without Structural would silently produce a "last wins" catalog. Order-dependent correctness.
- **Proposed patch:** have `DomainCatalogPass` report a structural failure on name collisions instead of `Last()` (or declare a dependency on `StructuralDomainAnalyzer`).

---

## Lens summary (ranked)

- **quality:** F1 🔴 (self-rel create-in CS1503), F2 🔴 (aRefs CS1061), F3 🟠 (conditional producer → throwing stub), F6 🟡 (subtree empty capability), F8 🟡 (codes stripped).
- **consistency:** F4 🟡 (effective-policy dedupe), F5 🟡 (fact-emitter fail-closed posture), F7 🟡 (undeclared EffectInvariant dependency), F10 🟡 (catalog last-wins).
- **product:** F3 🟠 (documented ✅ shape dead-ends in export), F8 🟡 (warnings/hints unreachable on CLI; codes hidden).
- **security:** F9 🟡 (global-metadata fallback = wrong-context leakage hazard), plus the F1 root cause is a metadata-semantics overload (`IsBackReference` conflates self-relationship with back-reference — a wrong-context flag).
- **reliability:** F1 🔴, F2 🔴, F5 🟡 (silent skip vs throw), F6 🟡, F10 🟡. Empty domains and empty/sparse entities verified clean (0/0); `analysis-rejects` failures all fire at the analysis rung, not at codegen/compile.

## Notes
- Node replacement (`SetNodeReplacement`) is not exercised by the DomainModeling pipeline (only ConstantFoldingPass in Interpretation) — no finding; consistent with CORE ("prefer when a rewrite is needed" — domain pipeline has none).
- Facts-on-nodes verified: subscription dispatch plans on Stage/Entity nodes, catalog on the Domain node, resolved-target facts on create-in effect nodes; DTLM/RLM live in the framework's global bucket (documented in CORE as intermediate bags embedded in the catalog) — consistent with CORE, no parallel side tables found. `RuntimeAnalysisCache` is a WeakTable cache, not a facts side table.
- Pass order/dependencies: all 23 declared `Dependencies` strings resolve to registered pass IDs (no unregistered-dependency build error); the one real gap is F7 (undeclared read).
