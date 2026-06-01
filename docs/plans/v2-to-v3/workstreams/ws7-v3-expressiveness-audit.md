# Workstream WS7: V3 Expressiveness Audit (Phase 1)

**Phase**: 1 (parallel with WS1)  
**Priority**: High (risk reduction for Phase 4)  
**Owner**: Grok (orchestrator, full send per user direction)  
**Status**: Highest Priority for remaining Phase 1 (per June 2026 re-evaluation of master roadmap)  
**Last Updated**: 2026-06 (created under active ownership)

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

## Expanded Audit Table (Initial Comprehensive Pass - June 2026)

**Sources**: Direct code inspection of `Poly/Data/Modeling/` (V2) vs `Poly/DomainModeling/` + `Evolution/` (V3), roadblock files (`library-roadblocks.md`, `ecommerce-roadblocks.md`, `healthcare-roadblocks.md`), and prior decision records.

| Concept | V2 Status | V3 Status | Classification | Notes / Roadblock Impact |
|---------|-----------|-----------|----------------|--------------------------|
| **Core Modeling Primitives** |
| Entity (with stages, actions, policies, events) | ✅ Full | ✅ Full (leaner) | Equivalent (V3 cleaner) | Good |
| Stage (with parent hierarchy, OnEntry/OnExit effects, actions/policies) | ✅ Full + parent support | ✅ Basic + parent + OnEntry/OnExit effects via evolution | Mostly equivalent | Stage actions now possible via evolution layer |
| Action (parameters, effects, policies, result, triggers) | ✅ Full | ✅ Good coverage via evolution changes | Mostly equivalent | V3 actions on stages is a win |
| Property + Constraints (Required, Range, Length, Enum, etc.) | ✅ Full + rich constraints | ✅ Good set of constraints | Equivalent | Minor syntax differences |
| Relationship (with optional scoped behavior) | ✅ Full + relationship-scoped stages/policies | ✅ Basic (name, source/target, cardinality, properties only) | Gap | See "Relationship-scoped behavior" below |
| Event (with properties) | ✅ Full | ✅ Basic | Equivalent | Good for MVP |
| ValueType (owned composite documents) | ✅ Full | ✅ Full | Equivalent | Good |
| PrimitiveType (with categories) | ✅ Full + built-in catalog | ✅ Good | Equivalent | Good |
| **Actor / Identity** |
| Actor (specialized Entity with claims, identity profile, role claim type) | ✅ `Actor.cs` + full mutation + rules | ❌ No Actor concept | Gap | Blocks UAC / access control scenarios |
| ActorClaimMapping, ActorIdentityProfile, ActorRules | ✅ Dedicated types + commands | ❌ Not present | Gap | Part of Actor gap |
| **Policy & Rules** |
| Policy (named, attached to Entity/Stage/Action/Property) | ✅ Full | ✅ Good (via evolution + PolicyConstraintAnalyzer) | Equivalent | Good |
| Rich Rule system on Policy (`Rule` base + subtypes: `PropertyRule` (with constraints), `CrossPropertyRule`, `CompositeRule`, `ActorTypeRule`, `ActorRoleRule`, `ActorPropertyRule`, etc. + full lowering) | ✅ Very rich + JSON polymorphism + dedicated lowering | ⚠️ Only `DomainExpression` form (no rich Rule subtypes) | Genuine Gap | Actor-aware and composite rules not expressible. This is one of the largest expressiveness gaps. |
| **Effects System** |
| Basic effects (CreateEntityInstance with initializers, PublishEvent, StageTransition) | ✅ Present | ✅ Present + PropertyBindings + evolution support | Equivalent / V3 improved | Good coverage |
| CompositeEffect | ✅ Full support | ❌ Not present | Gap | Common pattern in real demos |
| ConditionalEffect | ✅ Full support | ❌ Not present | Gap | ReportLost etc. roadblocks |
| InvokeAction (with parameter binding) | ✅ Full + BindParameter support | ❌ Not present | Gap | FulfillReservation → CheckoutBook patterns blocked |
| Mutation effects (Assign / cross-entity property modification) | ✅ `Mutations/Assignment.cs` + cross-entity via relationships | ⚠️ Only same-entity via Create + PropertyBinding | Genuine Gap | **Library RenewLoan / CheckoutBook cross-entity mutation roadblock** |
| LinkRelationship / UnlinkRelationship | ✅ Dedicated effects | ❌ Not present | Gap | Relationship management scenarios |
| DeleteEntityInstance | ✅ Present | ❌ Not present (only via other means?) | Gap | Explicit deletion scenarios |
| Effect output wiring / BindOutputTo | ✅ Rich Result + binding model | ⚠️ Very limited (InvocationResult on some effects) | Gap | Complex effect chaining blocked |
| **Event Subscriptions & Correlation** |
| EventSubscription + correlation bindings | ✅ Full (`EventSubscription`, `EventCorrelationBinding`, routing modes, audience) | ❌ Only basic publish via effect | Genuine Gap | Complex event-driven + saga patterns blocked |
| **Contracts & Interop** |
| ImportedContract + ContractBinding + endpoints + field maps | ✅ Full + recipes (Clr, OpenApi) | ❌ Not present | Gap | External system integration scenarios |
| **DomainExpression / Calculations** |
| Rich expression system for guards, calculations, initializers | ✅ `ExpressionValue` + various forms | ✅ `DomainExpression` (property/param/owned/literal/exists/notexists + basic ops) | Intentional minimalism + partial gap | **Library RenewLoan dynamic calc roadblock** is the forcing function. Cross-entity expressions limited. |
| **Mutation / Evolution Model** |
| Heavy Command + Intent transactional system (65 MutationCommand files + DomainMutationIntent + Engine) | ✅ Full production. Breakdown: Entity (14), Domain (9), Action (8), EventSubscription (5), Stage (4), Property (4), Actor (4), Policy (2), InvokeAction (2), ImportedContract (2), Event (2), Effect (2), DomainType (2), ContractBinding (2), plus Relationship, Conditional, Composite | N/A (replaced) | Replaced by design | This is the core of the "mutation tax". V3 replaces it with a thin, native `DomainChange` set + `DomainEvolution` + `DomainMutationContext` batching |
| **Other Significant Concepts** |
| Visual metadata / layout / projections | ✅ `VisualMetadataStore` (immutable, NodeId-keyed), `VisualLayout`, `VisualProjectionEndpoint` | ❌ Not present (V3 has strong NodeId foundation that would make this easier) | Gap (deferred) | Important for future real-time UI / visual authoring requirements documented in evolution-layer-design.md |
| Recipes / Scaffolding | ✅ Dedicated system: `EntityScaffoldRecipe`, `RelationshipScaffoldRecipe`, `WorkflowScaffoldRecipe`, plus full Contract import recipes (Clr + OpenApi) | ⚠️ Fluent builders (`DomainBuilder`, `EntityBuilder`, etc.) provide some scaffolding ergonomics, but no equivalent recipe abstraction or contract import system | Partial gap | V3 builders are nicer for hand-authoring, but lack the rapid scaffolding + contract import capabilities |
| Full Effect output wiring model (`EffectResult`, `BindOutputTo`, `EffectValueRef`) | ✅ Rich | ⚠️ Minimal (`InvocationResult` on some effects) | Gap | Complex chained effects blocked |
| Capability views / analysis metadata | ✅ 19 files: ~10 specialized analyzers (CapabilityAnalyzer, ContractIntegrationAnalyzer, ConstraintPropagationAnalyzer, ActionEventQualityAnalyzer, EffectAnalyzer, ConstraintQualityAnalyzer, EnumConstraintSubsetAnalyzer, etc.) + rich metadata types (EffectiveMemberMetadata, EffectiveStageMetadata, StageLineageMetadata, PolicyValidationAstMetadata, etc.) + utilities (DomainDiffUtil, DomainOperabilityFacade) | ⚠️ Core set present (StructuralDomainAnalyzer, SemanticDomainAnalyzer, PolicyConstraintAnalyzer) + DomainModelMetadata | Partial | V2 has significantly richer specialized analysis surface. V3 reuses the shared Syntax.Analysis infrastructure; full unification and capability views are Phase 2 work. |
| Lowering / Code Generation | ✅ `DomainLoweringGenerator` + full pipeline | ✅ Exists but parity needed | In progress (Phase 2) | Critical for codegen consumers |

**Classification Legend**:
- **Equivalent / V3 improved**: V3 is at parity or better for the concept.
- **Intentional simplification**: Deliberate reduction in complexity (trade-off accepted).
- **Genuine Gap**: Concept is missing and blocks real usage / roadblocks.
- **Deferred / Phase 2+**: Expected in later phases (e.g. analysis unification).

**Known Roadblock Mapping (from roadblock .md files - strengthened with references)**:
- **Library RenewLoan** (library-roadblocks.md:32-52): Strongly blocked by dynamic calculations + cross-entity mutation. Suggested fixes in the doc: support expressions like `current + 1` or date arithmetic in effects; support navigation like `Loan.Book.AvailableCopies`.
- **Library CheckoutBook / ReturnBook** (library-roadblocks.md:14-30): Blocked by inability to do cross-entity property modification from an action. Suggested: `CrossEntityMutation` effect or relationship navigation in effects.
- **Library ReportLost** (library-roadblocks.md:56-68): Needs conditional effects + multiple side effects (stage transition + decrement copies + create Fine + publish event).
- **Library FulfillReservation → CheckoutBook** (library-roadblocks.md:92-103): Blocked by weak InvokeAction + parameter binding. Suggested: simplify binding API.
- **Healthcare Multiple Ownership** (healthcare-roadblocks.md:6-45): Blocked by "first-class owned collections / multiple ownership" constraints. Validation explicitly rejects multiple `SourceOwnsTarget = true` relationships to the same target (e.g., Patient + Doctor both owning Appointment). Workarounds used: set `SourceOwnsTarget = false` on secondary relationships. Suggested fixes: allow multiple ownership, clearer errors, or joint ownership support. This is a clear ownership modeling gap on the immutable core.
- **Relationship-centric / complex ownership** (healthcare + ecommerce): Blocked by lack of relationship-scoped stages/policies + limited cross-entity effects.
- **Actor / UAC / claims scenarios** (healthcare, general): Blocked by complete absence of Actor concept and supporting rules (ActorTypeRule, ActorRoleRule, etc.).
- **Complex event-driven + correlation** (ecommerce, general): Blocked by missing EventSubscription + correlation bindings.
- **E-commerce advanced flows**: Impacted by missing advanced effects (InvokeAction, Conditional, Composite) and richer expressions for order/payment/shipment state machines.

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

## Phase 4 Prioritization Recommendations (Final)

This audit + the three roadblock .md files (with their explicit Suggested Fixes) is the primary input for Phase 4 scoping.

**High Priority (Directly unblock documented roadblocks — sequence these first):**
- Cross-entity effects/mutation (Assign-like + relationship navigation) — Library domain (CheckoutBook, ReturnBook, RenewLoan) blocked.
- Richer DomainExpression for dynamic calculations (increment, date arithmetic, cross-entity) — RenewLoan is the forcing function per the immutable-core decision.
- Conditional, Composite, and InvokeAction effects with proper binding (ReportLost, FulfillReservation patterns).
- EventSubscription + correlation bindings.
- Actor + claims/identity model + actor-aware rules.
- Multiple/joint ownership modeling on relationships (Healthcare domain blocker; current validation too strict).

**Medium-High Priority (Major expressiveness + future requirements):**
- Relationship-scoped stages, policies, actions.
- Imported contracts + recipes (Clr/OpenApi interop).
- Full Rule system evolution (or clean replacement on DomainExpression).
- Richer specialized analysis surface (V2 had ~19 Analysis files; V3 core set is good foundation — unification matters for consumers).
- Visual metadata + projection support (first-class requirement per evolution-layer-design.md for real-time UI/visual authoring).

**Lower / Deferrable:**
- Full 1:1 MutationCommand parity (intentionally not desired; thin evolution layer is the point).
- Some V2 analyzers can be reconstituted on the shared Syntax.Analysis base (Phase 2).

**Recommendation**: Scope Phase 4 around the High Priority list, using Library and Healthcare as the primary forcing functions. Enable WS5 to prove the new capabilities on the actual roadblock scenarios.

**Suggested New Decision Records** (create at Phase 4 kickoff):
- Actor / principal identity on the immutable core + evolution layer.
- Cross-entity effects and dynamic calculation expressions design.
- Event subscription + correlation model.
- Multiple/joint ownership on relationships.
- (Optional) Rich analysis metadata surface on the shared Syntax.Analysis infrastructure.

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