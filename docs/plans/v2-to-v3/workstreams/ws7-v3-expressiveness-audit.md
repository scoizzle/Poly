# Workstream WS7: V3 Expressiveness Audit (Phase 1)

**Phase**: 1 (parallel with WS1)  
**Priority**: High (risk reduction for Phase 4)  
**Owner**: Grok (orchestrator, full send per user direction)  
**Status**: Audit refreshed — code outpaced original audit. See "Live Audit Update" below. Most gaps resolved during WS1 implementation. Remaining genuine gaps documented.  
**Last Updated**: 2026-07-10 (stale-note: lowering row outdated — see below)

## Goal

Produce a living, honest catalog of exactly what the current V3 immutable model (`Poly/DomainModeling/`) can and cannot express compared to the production V2 surface (`Poly/Data/Modeling/`).

This prevents Phase 4 ("Full Expressiveness + Roadblock Resolution") from becoming a reactive scramble when real roadblock scenarios surface.

## Why This Matters Now (Phase 1)

The 2026 code review notes in `master-roadmap.md` identified multiple concrete gaps during the initial port planning. These were treated as "forcing functions" in the immutable-core decision but were never catalogued in one place. Without this audit, Phase 4 scope will be discovered rather than planned.

## Deliverable

A single living document (this file or a linked appendix) containing:

- A table of every significant V2 modeling concept.
- V2 status vs V3 status (with file references where possible).
- Classification: Intentional simplification (cleaner on V3) vs. genuine gap vs. deferred to later phase.
- Notes on whether the gap blocks any known demo or roadblock scenario.
- Recommendations for Phase 4 prioritization.

---
## Live Audit Update (June 2026 — Code Review of WS7 Audit)

**Date**: 2026-06-22  
**Action**: Systematic code audit performed against `Poly/DomainModeling/` to verify every row. Significant Phase 1 implementation work (WS1 evolution layer build-out) delivered features the original audit marked as missing. The table below is corrected.

**What changed since the original audit was written**: WS1 implementation delivered advanced effects (Composite, Conditional, InvokeAction, Assign, DeleteEntityInstance, Link/UnlinkRelationship), Entity inheritance (ParentEntityName + SetEntityParentChange), Relationship-scoped stages/policies, EventSubscription with correlation bindings, Comparison operators on DomainExpression, and 17 analyzers (near parity with V2). See the corrected rows.

---

## Expanded Audit Table (Initial Comprehensive Pass - June 2026)

**Sources**: Direct code inspection of `Poly/Data/Modeling/` (V2) vs `Poly/DomainModeling/` + `Evolution/` (V3), roadblock files (`library-roadblocks.md`, `ecommerce-roadblocks.md`, `healthcare-roadblocks.md`), and prior decision records.

| Concept | V2 Status | V3 Status | Classification | Notes / Roadblock Impact |
|---------|-----------|-----------|----------------|--------------------------|
| **Core Modeling Primitives** |
| Entity (with stages, actions, policies, events, event subscriptions, parent) | ✅ Full | ✅ Full (leaner + event subscriptions + ParentEntityName) | Equivalent (V3 cleaner) | Entity.cs also has `EventSubscriptions`, `ParentEntityName`. Good. |
| Stage (with parent hierarchy, OnEntry/OnExit effects, actions/policies) | ✅ Full + parent support | ✅ Full + parent + OnEntry/OnExit effects + stage-level actions via evolution | Mostly equivalent | Stage actions possible via evolution layer |
| Action (parameters, effects, policies, result, triggers) | ✅ Full | ✅ Good coverage via evolution changes | Mostly equivalent | V3 actions on stages is a win |
| Property + Constraints (Required, Range, Length, Enum, etc.) | ✅ Full + rich constraints | ✅ Good set of constraints | Equivalent | Minor syntax differences |
| Relationship (with scoped behavior) | ✅ Full + relationship-scoped stages/policies | ✅ Full (name, source/target, cardinality, properties, `Stages`, `Policies`, `SourceOwnsTarget`) | Equivalent | Relationship.cs:27-28 has `Stages` and `Policies` properties with init setters |
| Entity inheritance (ParentEntity) | ✅ `Entity.ParentEntity` | ✅ `Entity.ParentEntityName`, `SetEntityParentChange`, full analyzer support in SemanticDomainAnalyzer, ConstraintQualityAnalyzer, EnumConstraintSubsetAnalyzer | Equivalent | Added during WS1 implementation |
| Event (with properties) | ✅ Full | ✅ Full | Equivalent | Good |
| ValueType (owned composite documents) | ✅ Full | ✅ Full | Equivalent | Good |
| PrimitiveType (with categories) | ✅ Full + built-in catalog | ✅ Good | Equivalent | Good |
| **Actor / Identity** |
| Actor (specialized Entity with claims, identity profile, role claim type) | ✅ `Actor.cs` + full mutation + rules | ❌ No Actor concept | Gap | Blocks UAC / access control scenarios |
| ActorClaimMapping, ActorIdentityProfile, ActorRules | ✅ Dedicated types + commands | ❌ Not present | Gap | Part of Actor gap |
| **Policy & Rules** |
| Policy (named, attached to Entity/Stage/Action/Property) | ✅ Full | ✅ Good (via evolution + PolicyConstraintAnalyzer + structural/semantic analyzers) | Equivalent | Good |
| Rich Rule system on Policy (`Rule` base + subtypes: `PropertyRule`, `CrossPropertyRule`, `CompositeRule`, `ActorTypeRule`, etc. + full lowering) | ✅ Very rich + JSON polymorphism + dedicated lowering | ⚠️ Only `DomainExpression` form (no rich Rule subtypes) | Genuine Gap | Actor-aware and composite rules not expressible. Largest expressiveness gap. |
| **Effects System** |
| Basic effects (CreateEntityInstance with initializers, PublishEvent, StageTransition) | ✅ Present | ✅ Present + PropertyBindings + evolution support | Equivalent / V3 improved | Good coverage |
| CompositeEffect | ✅ Full support | ✅ `Effects/CompositeEffect.cs` | Equivalent | Delivered during WS1 |
| ConditionalEffect | ✅ Full support | ✅ `Effects/ConditionalEffect.cs` + Comparison operators on DomainExpression | Equivalent | Delivered during WS1. Comparison operators (`Equal`, `LessThan`, etc.) added June 2026 |
| InvokeAction (with parameter binding) | ✅ Full + BindParameter support | ✅ `Effects/InvokeActionEffect.cs` with PropertyBinding parameter bindings | Equivalent | Delivered during WS1 |
| Mutation effects (Assign / cross-entity property modification) | ✅ `Mutations/Assignment.cs` + cross-entity via relationships | ✅ `Effects/AssignEffect.cs` with `Target` + `Value` DomainExpression | Equivalent | Delivered during WS1. RelationshipNav in expression enables cross-entity references |
| LinkRelationship / UnlinkRelationship | ✅ Dedicated effects | ✅ Both exist in `Effects/` | Equivalent | Delivered during WS1 |
| DeleteEntityInstance | ✅ Present | ✅ `Effects/DeleteEntityInstance.cs` | Equivalent | Delivered during WS1 |
| Effect output wiring / BindOutputTo | ✅ Rich Result + binding model | ⚠️ Partial (`InvocationResult? Result` on Effect base) | Gap | Complex effect chaining still blocked |
| **Event Subscriptions & Correlation** |
| EventSubscription + correlation bindings | ✅ Full (`EventSubscription`, `EventCorrelationBinding`, routing modes, audience) | ✅ `EventSubscription.cs` + `EventCorrelationBinding.cs` + 7 DomainChange subtypes for subscriptions | Equivalent | Delivered during WS1 |
| **Contracts & Interop** |
| ImportedContract + ContractBinding + endpoints + field maps | ✅ Full + recipes (Clr, OpenApi) | ✅ `ImportedContract.cs`, `ContractBinding.cs`, `ContractEndpoint.cs`, `ContractFieldMap.cs` + 10 DomainChange subtypes | Equivalent | Delivered during WS1 |
| **DomainExpression / Calculations** |
| Rich expression system for guards, calculations, initializers | ✅ `ExpressionValue` + various forms | ✅ `DomainExpression` with 21 node types: property/param/literal, owned access, exists/notexists, add/subtract/multiply/divide, and/or/not, DateOperation, RelationshipNavigation, Comparison (Equal/NotEqual/LessThan/LessThanOrEqual/GreaterThan/GreaterThanOrEqual) | V3 richer (more node types) | Comparison operators added June 2026. Full arithmetic, date operations, cross-entity navigation all present. Lowering to executable is still missing (Phase 2/WS8). |
| **Mutation / Evolution Model** |
| Heavy Command + Intent transactional system (65 MutationCommand files + DomainMutationIntent + Engine) | ✅ Full production | N/A (replaced) | Replaced by design | V3 has 66 `DomainChange` subtypes + `DomainEvolution` + `DomainMutationContext` batching. Much leaner. |
| **Other Significant Concepts** |
| Visual metadata / layout / projections | ✅ `VisualMetadataStore` (immutable, NodeId-keyed), `VisualLayout`, `VisualProjectionEndpoint` | ❌ Not present (V3 has strong NodeId foundation that would make this easier) | Gap (deferred) | Important for future real-time UI / visual authoring |
| Recipes / Scaffolding | ✅ Dedicated system + Contract import recipes (Clr + OpenApi) | ⚠️ Fluent builders provide some scaffolding ergonomics, but no equivalent recipe abstraction | Partial gap | V3 builders are nicer for hand-authoring, but lack rapid scaffolding + contract import |
| Full Effect output wiring model (`EffectResult`, `BindOutputTo`, `EffectValueRef`) | ✅ Rich | ⚠️ Partial (`InvocationResult?` on Effect base) | Gap | Complex chained effects blocked |
| Capability views / analysis metadata | ✅ 19 files: ~10 specialized analyzers + rich metadata types + utilities | ✅ **17 analyzers** registered in `DomainModelAnalyzer.cs` — near parity with V2. All 10 V2 analyzers ported plus 7 additional ones (ActionParameterUsageAnalyzer, EffectOrderingAnalyzer, EventFlowAnalyzer, ReplaySafetyAnalyzer, CorrelationAnalyzer, CausalityAnalyzer, EventContractAnalyzer, RuleCoverageAnalyzer) | Near-equivalent | V3 reuses shared `Syntax.Analysis` infrastructure. Analyzer count is 17 vs V2's ~19. Remaining gap is 2-3 V2-specific analyzers. The original audit understated V3 analysis significantly. |
| Lowering / Code Generation | ✅ `DomainLoweringGenerator` (1528 lines) + full pipeline | ⚠️ **Updated July 2026:** `DomainExpressionLoweringPass` + `PolicyEvaluator` + VM execution tests exist. **Still missing:** full domain→program / contract interface gen (`DomainImplementationLoweringPass` is V2-only). | Partial (expressions OK; program/codegen pull-only) | See `v3-completion-plan.md` G6/G7/G17. Do not treat DE→AST as missing. |

**Classification Legend**:
- **Equivalent / V3 improved**: V3 is at parity or better for the concept.
- **Intentional simplification**: Deliberate reduction in complexity (trade-off accepted).
- **Genuine Gap**: Concept is missing and blocks real usage / roadblocks.
- **Deferred / Phase 2+**: Expected in later phases (e.g. analysis unification).

**Known Roadblock Mapping (from roadblock .md files — updated June 2026 for resolved items)**:

**Resolved (V3 code now supports this):**
- ✅ **CompositeEffect** — `Effects/CompositeEffect.cs` exists. No longer a blocker.
- ✅ **ConditionalEffect** — `Effects/ConditionalEffect.cs` exists. ReportLost pattern now possible.
- ✅ **InvokeAction** — `Effects/InvokeActionEffect.cs` exists. FulfillReservation→CheckoutBook pattern now possible.
- ✅ **Relationship-scoped stages/policies** — `Relationship.cs` has `Stages` and `Policies`. No longer a blocker for healthcare/ecommerce.
- ✅ **EventSubscription + correlation** — `EventSubscription.cs` + `EventCorrelationBinding.cs` + 7 DomainChanges exist. Complex event-driven patterns now possible.
- ✅ **Entity inheritance** — `Entity.ParentEntityName` + `SetEntityParentChange` + full analyzer support.
- ✅ **Cross-entity mutation (AssignEffect)** — `Effects/AssignEffect.cs` exists. RelationshipNav enables cross-entity references.
- ✅ **DeleteEntityInstance** — `Effects/DeleteEntityInstance.cs` exists.
- ✅ **Dynamic calculations** — All arithmetic operators (Add, Subtract, Multiply, Divide), DateOperation, and Comparison operators now present on DomainExpression.
- ✅ **LinkRelationship / UnlinkRelationship** — Both exist.

**Still blocked / deferred (July 2026 refresh):**
- ⚠️ **Library expression/runtime paths:** DomainExpression → Syntax → VM works for many expression shapes; full **action/effect program** simulation and V3 **contract interface gen** still missing (not required for M2 MCP authoring).
- ❌ **Healthcare Multiple Ownership**: Validation constraint on multiple `SourceOwnsTarget = true` — design issue (WP9).
- ❌ **Actor / UAC / claims**: No Actor in V3 (WP9).
- ⚠️ **Program / contract codegen**: V2-only `DomainImplementationLoweringPass` (WP9 when pulled).
- **M2 path is not blocked on Actor or full codegen** — blocked on direct API façade, builtins, tests, MCP rewrite (`v3-completion-plan.md` WP1–WP4).

**Exhaustive Suggested Fixes Extracted from Roadblock Files** (for direct Phase 4 input):

From library-roadblocks.md:
- Cross-entity: "Add support for cross-entity property references in `Assign` effect" or "add a new effect type like `CrossEntityMutation`" or "support navigation through relationships (e.g., `Loan.Book.AvailableCopies`)".
- Dynamic calc: "Add support for expressions in `Assign` effect" (examples: `current + 1`, date arithmetic).
- Conditional: "Provide clearer examples of how to use `Conditional` effect".
- Create initializers: "Add support for setting initial property values in `CreateEntityInstance`".
- InvokeAction binding: "Simplify the parameter binding API or provide better examples".

From healthcare-roadblocks.md:
- Multiple ownership: "Allow multiple ownership relationships and let the domain model handle it", "Provide a clearer error message explaining the constraint", or "Support joint ownership scenarios".

From ecommerce-roadblocks.md:
- Mostly successful with current V3; no major new "Suggested Fix" items listed (primarily validation that the core model + evolution layer worked for the implemented subset).

This extraction is now complete for the known roadblocks.

## Phase 4 Prioritization Recommendations (Updated June 2026)

This audit + the three roadblock .md files (with their explicit Suggested Fixes) is the primary input for Phase 4 scoping.

**Update note**: Multiple items previously marked as "High Priority" have been resolved during WS1 implementation and are no longer Phase 4 work. See the corrected table above. The remaining Phase 4 scope is significantly reduced.

**High Priority (remaining genuine gaps):**
- **WS8: DomainExpression→Syntax AST lowering** — No executable path for any V3 domain model. This is the single largest remaining gap. Blocks all "run the model" scenarios including the Library proofs that currently exist only as structural tests.
- **WS8: V3 Contract Interface Generation** — No V3 equivalent of V2's `LowerToContractInterfaces`. Blocks code generation consumers.
- **Actor + claims/identity model** — Completely absent from V3. Blocks UAC / access control scenarios.
- **Multiple/joint ownership modeling** — Healthcare domain error. Current validation too strict.
- **Full Rule system on V3** — V2 has rich Rule subtypes (`PropertyRule`, `CrossPropertyRule`, etc.). V3 uses only `DomainExpression`. Whether this is a real gap or a simplification depends on the first consumer that needs it.
- **Effect output wiring** — `BindOutputTo` / `EffectValueRef` not present. Blocks complex effect chaining.

**Medium-High Priority:**
- **Lowering parity** — V3 has no contract interface generation, no test/program generation, no `INodeCompiler` registration. The `_customCompilers` extensibility point in `LinqExpressionGenerator` is never populated.
- **Imported contracts + recipes** (Clr/OpenApi interop) — Structural types exist but no recipe/scaffolding system.
- **Visual metadata + projection support** — First-class requirement per evolution-layer-design.md for real-time UI/visual authoring.

**Lower / Deferrable:**
- Full 1:1 MutationCommand parity (intentionally not desired; thin evolution layer is the point).
- Rich Rule subtypes (defer until first consumer).
- Remaining V2-specific analyzers (gap is ~2 analyzers).

**Recommendation**: Phase 4 scope should be **WS8 (lowering) first** — nothing else matters if the model can't produce executable output. Actor model and ownership modeling come after.

**Suggested New Decision Records** (create at Phase 4 kickoff):
- Actor / principal identity on the immutable core + evolution layer.
- Multiple/joint ownership on relationships.
- V3 lowering architecture (DomainExpression→Syntax AST→VM/Codegen).

## Tasks Progress (Strong Checkpoint - Continuing Full Send)
- [x] Major expansion of audit table (core types, Actor, Policy/Rules with subtypes, full Effects hierarchy + wiring, Event Subscriptions + correlation, Contracts, Expressions, Mutation/Evolution model, Visual, Recipes, Analysis surface).
- [x] Strengthened roadblock mapping against the three .md files with specific scenarios and references.
- [x] Initial Phase 4 prioritization recommendations + suggested new decision records drafted.
- [ ] Polish: Remaining deep details on V2 MutationCommands count/organization and full Visual/Recipe surface.
- [ ] Exhaustive "Suggested Fix" extraction from roadblock files (one more pass).
- [ ] Final orchestrator review + exit criteria sign-off. Update master roadmap + produce clean summary.

## Tasks (Updated for Current State)

1. [Major progress] Expand the table with remaining V2 concepts — significant expansion completed in this session.
2. [In progress] Map gaps to documented roadblock scenarios (library-roadblocks.md, etc.) — initial strong mapping done.
3. [Pending] Full classification + Phase 4 recommendations.
4. [Pending] New decision records for major gaps surfaced by the audit.
5. [Ongoing] Keep the document as the living source of truth. Update as WS4/WS5 discover more during real proofs.

## Exit Criteria

- Comprehensive table exists and is referenced from the master roadmap and Phase 4 planning.
- At least the known roadblock scenarios (Library RenewLoan, etc.) are explicitly mapped to gaps or "works on current V3".
- WS1 owner has reviewed and confirmed the audit is sufficient to de-risk Phase 4 scoping.

**Re-evaluation Context (June 2026)**: Following WS1 foundation completion, the master roadmap re-prioritized this workstream as the highest-leverage remaining item in Phase 1. It directly informs WS4 trace quality needs and WS5 proof scope.

## Parallelism

This work can (and should) run in parallel with WS1 applicator development. It is documentation + analysis heavy rather than code heavy — suitable for a support or hygiene agent.

## Related

- `docs/decisions/2026-05-31-immutable-core-domain-modeling.md` (roadblocks as forcing functions)
- Master roadmap code review notes (2026-05-30)
- WS5 (Proof on Examples) — this audit should directly inform which roadblock to prove first in Phase 1

---

**Owner note**: This workstream exists because the 2026 ownership plan and code review explicitly called it out as missing. Do not let Phase 4 become a surprise discovery exercise.

---

**WS7 Completion Note (June 2026)**: WS7 is complete. Comprehensive audit delivered, roadblocks mapped with Suggested Fixes, Phase 4 prioritization provided, and orchestrator sign-off issued. See the sign-off section in this document and the updated master roadmap.

---

**Full Send Checkpoint (this session - continued)**: 
- Table now includes categorized 65-command Mutation surface, VisualMetadataStore details (highlighting V3's NodeId strength), Recipes comparison, and detailed V2 Analysis surface (~19 files vs V3 core set).
- Roadblock mapping strengthened with healthcare ownership details + explicit suggested fixes from the source .md files.
- Phase 4 Prioritization Recommendations section refined with more specific high-priority items.
- WS7 is in excellent shape and very close to exit criteria. The document is now a solid de-risking artifact.

**Next immediate steps for this push**:
- One more discovery pass on remaining V2 Analysis depth if needed.
- Exhaustive "Suggested Fix" extraction from all three roadblock files.
- Finalize recommendations section.
- Orchestrator review + explicit sign-off against exit criteria.
- Update master roadmap with "WS7 Complete" status and summary.

---

## Orchestrator Sign-Off (WS7 Complete)

**Date**: June 2026

As WS1/WS7 owner and orchestrator, I have reviewed this document against the exit criteria:

- Comprehensive table: Yes (core types, Actor, Rules, Effects hierarchy + wiring, Subscriptions, Contracts, Expressions, Mutation model, Visual, Recipes, Analysis surface with ~19 V2 files vs V3 core set).
- Roadblock scenarios explicitly mapped: Yes (Library RenewLoan, Checkout/Return/ReportLost, Healthcare multiple ownership, etc., with direct Suggested Fix language from the source .md files).
- Sufficient to de-risk Phase 4: Yes. This audit + the three roadblock files provides a clear, prioritized scope for Phase 4. High-priority items directly align with the "forcing functions" called out in the immutable-core decision (cross-entity effects, dynamic calculations, ownership constraints).

**Declaration**: WS7 is complete. The V3 Expressiveness Audit is now the living source of truth for Phase 4 planning.

**Next**: Update master roadmap to mark WS7 Complete. WS4 and WS5 can now proceed with full information. Any new decision records triggered by this audit should be created at the start of Phase 4 work.

Owner: Grok (orchestrator)