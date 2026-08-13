# Fleet Eval 2026-08-12 — Domain Model & Evolution (slice 02)

Agent slice: `Poly/DomainModeling/` core records (Domain, Entity, Property, Action, Stage,
Policy, Effect + Effects/, DomainExpression IR, DomainTypeReference, Constraints/) and
`Evolution/` (DomainChange, DomainEvolution, DomainMutationContext).

Probes (all under `probes/fleet-eval/02-domain-model/`):
- `hotel-booking.poly` — full-surface real system (enums, constraints, defaults, navs,
  stages, entry/exit, actions, requires, policies, subscriptions, `for` fan-out, `-> Entity`,
  annotations). Compiles 0/0 (entities and `--mode all`).
- `evolution-order.poly` — batch-ordering stress (deferred nav targets, same-name back-refs,
  defaults+constraints, stage actions). Compiles 0/0.
- `constraint-coherence.poly` — joint constraints, open length, negative/open ranges,
  literal/enum/`now`/`today`/`guid` defaults, param-to-constrained-property flow. Compiles 0/0.
- `path-prefix-enum-compare.poly` — 🔴 repro (analysis passes, export CS0019/CS0117).
- `same-name-action.poly` — 🔴/🟠 repro (CS0111 + silent effect misattribution).
- `open-range-assign.poly` — 🟠 repro (valid assign on open range rejected).

---
## F1 — Path-prefix comparison against an enum-typed related property fails to compile (CS0019 / CS0117)
- **Signal:** compile-fail
- **Severity:** 🔴
- **Slice:** Domain model & evolution (DomainExpression `RelationshipNavigation`/`Comparison` + export lowering of the model records)
- **Repro:** `probes/fleet-eval/02-domain-model/path-prefix-enum-compare.poly` via `scripts/run-probe.sh`; also `probes/fleet-eval/02-domain-model/hotel-booking.poly` with `RoomIsDeluxe: policy { room Type is "Deluxe" }` restored.
  - `RoomIsDeluxe: policy { room Type is "Deluxe" }` → `error CS0019: Operator '==' cannot be applied to operands of type 'RoomType' and 'string'`; `is not` sibling → `CS0019 '!='`.
  - `RoomReady: policy { room Status is "Ready" }` (leaf is `Text`, but the *caller* has a same-named `Status: BookingStatus`) → `error CS0117: 'BookingStatus' does not contain a definition for 'Ready'`.
- **Expected:** Guide §8 ships subject-first related reads (`customer Tier is "VIP"`, `loan book Title is "Classic"`); an enum-typed leaf with a member literal is the same family and analysis accepts it (member check passes — `"Deluxe"` is a member of `RoomType`). The export should lower the literal to the enum member (`RoomType.Deluxe`) exactly as it does for the **local** property form (`Type is "Deluxe"` → `this.Type == RoomType.Deluxe`, verified compiling 0/0).
- **Actual:** The exporter resolves the enum type for the literal from the **caller's** property namespace (or not at all), never from the path-prefix **target's** property type. Leaf enum → raw `"string"` (`CS0019`); caller same-name enum → wrong enum (`CS0117`). Both pass analysis and only fail at Roslyn — a late-rung failure; the sibling local form and the bare-identifier sibling (`room Type is Deluxe` — correctly rejected at analysis) bookend the broken string-literal form.
- **Sibling forms probed:** local enum compare (OK), local assign `assign Type to "Deluxe"` (OK), bare identifier under path-prefix (analysis-rejected, OK), `is not` under path-prefix (CS0019), quantifier body `any rooms where Type is "Suite"` (compiles — lowers to the `NotSupportedException` store-stub, so the bug only bites direct path-prefix lowering).
- **Proposed patch (not applied):** in the C# exporter's comparison lowering, when the left/right operand is a `RelationshipNavigation` leaf, resolve the literal's enum type from the **target entity's** property type (via the analysis-published `ResolvedTypeReferenceMetadata` on the leaf `PropertyAccess`), mirroring the local-property path; or reject enum-typed path-prefix comparisons at analysis (fail-closed earliest rung) until the lowering is correct.

## F2 — Same-named entity-level + stage-scoped action: CS0111 duplicate member AND silent stage-effect misattribution
- **Signal:** compile-fail + export/runtime divergence
- **Severity:** 🔴 (compile) / 🟠 (divergence)
- **Slice:** Evolution mutation correctness (`AddActionToStageChange` copy + `UpdateAction` entity-first resolution; structural member-uniqueness invariant)
- **Repro:** `probes/fleet-eval/02-domain-model/same-name-action.poly`:
  ```poly
  Task: entity {
    Mark: action { assign State to "entity-mark" }
    Active: stage {
      Mark: action { assign Note to "stage-mark" }
    }
  }
  ```
  → `error CS0111: Type 'Task' already defines a member called 'Mark' with the same parameter types`. Dumped export shows:
  - entity-level `Mark()` body = `this.State = "entity-mark"; this.Note = "stage-mark";` — the **stage-scoped effect was attached to the entity-level twin**;
  - stage `Mark()` body = `this.State = "entity-mark";` only — the author's `assign Note to "stage-mark"` is **silently absent** from the stage action.
- **Expected:** The runtime/MCP incremental path explicitly supports this shape (`McpSmokeTests.AddActionToStage_CopiesEntityActionEffects`, `AddActionToStage_Order_StageBeforeEntityEffects_StillTransitions`), so the model is valid and analyzable. Either (a) the DSL batch path must export the same behavior the runtime has (per-stage action = its own declared effects; entity action = its own), or (b) the model/analysis must reject entity+stage same-name actions as ambiguous (member uniqueness) at the earliest rung.
- **Actual:** `UpdateAction(searchStages: true)` in `DomainMutationContext` resolves entity-level actions **first**, so `AddEffectToActionChange` / `AddParameterToActionChange` for an ambiguous name silently land on the entity-level action. `AddPolicyToActionChange` / `RemovePolicyFromActionChange` do the *opposite* and fail loud on `AmbiguousAction` (`EvolutionRollbackTests.RemovePolicyFromAction_WhenAmbiguousBetweenEntityAndStage_FailsLoud`) — the fail-loud contract is inconsistent within the same family of mutations. The batch path then also exports two identical method signatures (CS0111). The runtime fallthrough path (`empty stage + entity twin`) never fires because the stage copy carries the copied entity effects, so the author's stage effect never runs — silent behavior divergence between the two authoring paths for the same model.
- **Proposed patch (not applied):** in `DomainMutationContext.UpdateAction` (and the `AppendChildToAction`/`UpdateAction` call sites for effects/parameters/result), detect the entity+stage ambiguity the same way `ResolveAction(searchStages: true)` does and record a `RequireUpdate`/`Errors` entry (fail loud) instead of silently preferring the entity-level action; and have the structural analyzer reject entity-level + stage-scoped same-name actions (or the exporter disambiguate).

## F3 — Open-range constraints: derived-value check treats an unbounded bound as 0, rejecting valid assigns
- **Signal:** guide-drift / fail-loud-but-wrong (blocks valid authoring)
- **Severity:** 🟠
- **Slice:** Constraints/ (`RangeConstraint` null min/max) + invariant propagation (`EffectAnalyzer.CheckDerivedRange`, `ValidateCallChainPostconditions`)
- **Repro:** `probes/fleet-eval/02-domain-model/open-range-assign.poly`:
  - `assign Qty to Qty + 5` on `Qty: Number range(0, )` → `Assigned expression value range [5, 5] is entirely outside constraint range(0, +∞)` (error → rollback).
  - `assign Stock to Stock - 200` on `Stock: Number range(, 100)` → `range [-200, -100] is entirely outside constraint range(−∞, 100)` (error).
- **Expected:** Guide §13 (`Total: Number range(0, )`) and the shipped probes (`inventory.poly` `Qty: Number range(0, )`, `fulfillment.poly` `Weight: Number range(0, )`) document open-ended ranges. `Qty+5` with `Qty ∈ [0, ∞)` is provably inside `[0, ∞)` → no diagnostic; `Stock-200` with `Stock ∈ (−∞, 100]` is inside `(−∞, 100]` → no diagnostic.
- **Actual:** `ToDouble(null)` in `EffectAnalyzer` returns `0.0` (not null) because `Convert.ToDouble(null)` returns 0, so the unbounded maximum of `range(0, )` is compared as `0` → `fullyAbove` (`lo > 0`) fires; the unbounded minimum of `range(, 100)` is compared as `0` → `fullyBelow` (`hi < 0`) fires. Any arithmetic assign/create on an open-range property is rejected as an error. (Closed-range `range(1, 100000)` works — the probe 1 fix.) Same helper bug feeds the call-chain postcondition check.
- **Proposed patch (not applied):** make `ToDouble` return `null` for a null input (`value is null ? null : Convert.ToDouble(value)` with the catch), so `tmin`/`tmax` stay null for unbounded sides and the `fullyBelow`/`fullyAbove` guards behave correctly; add a regression test for `range(0, )` and `range(, N)` assigns.

## F4 — `EqualityConstraint` cannot round-trip through export_dsl → apply_dsl (printed as an unparseable comment)
- **Signal:** fail-loud-but-sharp (dead model path)
- **Severity:** 🟡
- **Slice:** Domain model & evolution (Constraints/ `EqualityConstraint`; printer vs scanner)
- **Repro:** a property carrying `EqualityConstraint` (only reachable via the C# evolution API — `AddConstraintToPropertyChange(new EqualityConstraint(...))`; neither the DSL nor MCP `add(kind: constraint)` can author it). `DomainDslPrinter.PrintConstraint` emits `/* equals(5) */` (printer line 615), which the tokenizer does **not** skip (`DslTokenReader.SkipWhitespaceAndComments` handles only `//`), so `apply_dsl` on the printed text fails: `Parse error: Expected property, stage, action, or policy, got '/' (line 3, col 15)`.
- **Expected:** The guide §11 treats `equals(v)` as a model constraint that projects to transport `[AllowedValues]`; if it is not authorable, printing must either round-trip (`equals(...)` syntax) or the model must reject adding it. A printed domain must always reparse (idempotent round-trip is the printer's documented contract).
- **Actual:** `export_dsl` of a model containing `EqualityConstraint` produces text that fails `apply_dsl`. The constraint is silently unfixable through the shipped surface.
- **Proposed patch (not applied):** either add an `equals(...)` constraint syntax to the parser/tokenizer (and remove the `/* */` comment emission), or make the printer throw on `EqualityConstraint` so the failure surfaces at print time instead of as a later parse error; update the guide accordingly.

## F5 — Evolution removes for value types / primitives / imported contracts silently succeed on missing targets
- **Signal:** silent no-op (reliability — inconsistent fail-loud)
- **Severity:** 🟡
- **Slice:** Evolution/ `DomainChange` removes (`RemoveValueTypeChange`, `RemovePrimitiveTypeChange`, `RemoveImportedContractChange`, `RemoveContractBindingChange`, `RemoveContractEndpointChange`, `RemoveContractFieldMapChange`)
- **Repro:** `new DomainEvolution(domain).Apply([new RemoveValueTypeChange("Missing")])` → `EvolutionResult.Success` with the unchanged root, `WasRolledBack: false`, no error diagnostic. The entity/stage/action/property/policy removes (`RemoveEntityChange`, `RemoveStageChange`, …) instead call `RemoveAllWithGuard` / `RequireTarget` and roll back with "not found — nothing to remove".
- **Expected:** A remove-by-identity of a missing target should fail loud (same contract as `RemoveEntityChange`) — silent no-ops are bugs per the discovery loop. The MCP `remove` tool masks this only for the kinds it exposes via a fingerprint guard; the model layer itself reports success.
- **Actual:** `context.Types.RemoveAll(...)` / `context.ImportedContracts.RemoveAll(...)` with no existence guard and no `RequireTarget`, so a caller cannot distinguish "removed" from "never existed".
- **Proposed patch (not applied):** route these removes through `RemoveAllWithGuard` (or an equivalent `RequireTarget` check) so a missing target records an `EVOLUTION_TARGET` error and the batch rolls back, matching the guarded removes.

---
