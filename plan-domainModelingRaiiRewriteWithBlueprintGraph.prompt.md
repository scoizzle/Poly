## Plan: DomainModeling RAII Rewrite with Blueprint Graph

## Re-evaluated Plan (Systems-First, Accelerated)

This section supersedes the sequencing logic below for implementation kickoff. The rest of this file is retained as design detail reference.

### Core Operating Tenets
1. Question every requirement: if it does not improve customer time-to-value, correctness, or operability, it is removed.
2. Engineer the system, not isolated parts: every component must be justified by end-to-end behavior and ownership boundaries.
3. Optimize for shipped capability, not framework completeness: deliver the smallest coherent platform that proves the business model.
4. Implementation precedes abstraction: the canonical design pattern literature — GoF, POSA, PoEAA, DDD — are observations of patterns that emerged from real implementations, not prescriptions written ahead of them. The order of operations is non-negotiable: build working code first, extract the pattern second. Designing abstractions without prior implementations to observe is speculation, not engineering, and wastes the only resource that cannot be recovered — time. Note: operational guardrail tasks (ADR templates, compatibility policies, test conventions, CI configuration) are not abstractions — they are enabling constraints that directly unblock implementation tasks and have identifiable first consumers. They comply with this tenet.
5. Tools serve the domain; the domain serves the system: ORMs, frameworks, and infrastructure are means to an end — the end is delivering capabilities to the system as a whole. The domain model is the most important artifact in the codebase, and every tool choice must be evaluated against how well it expresses domain intent, not how familiar or convenient it is. This tenet yields only to the higher-order tenets above it; no tool preference overrides correctness, operability, or shipped capability.

### System Definition (Single Sentence)
Build a canonical domain semantics platform that can validate/evaluate business rules remotely and generate deployable/runtime artifacts, with strict boundary separation and self-hosting trajectory. (In scope but omitted from one-sentence summary for brevity: multi-tenant isolation, visual authoring via Blueprint graph and sidecar metadata, and OpenAPI/AsyncAPI/gRPC/JSON Schema interoperability.)

### Requirement Triage (Keep / Drop / Defer)
1. Keep now: canonical model + invariants, portability of rule semantics, validity API (REST + MCP), generated clients, governance/publish/versioning, diagnostics contract, multi-tenant isolation, compatibility enforcement.
2. Keep now: projection to interpretation/runtime plus one production-ready runtime strategy, with optional actor target contract preserved.
3. Defer: deep Blueprint editing ergonomics beyond schema/sidecar compatibility (layout polish, advanced editor UX features).
4. Defer: non-core extension formats beyond v1 commitments and trigger policy.
5. Drop for v1: any design artifact that is not consumed by a gate, test, or implementation decision.

### Anti-Bloat Rules
1. No new subsystem unless it owns a distinct failure mode and has measurable outcomes.
2. No dual implementations without a parity test and explicit retirement plan.
3. No schema surface without generated client contract and compatibility policy.
4. No "future-proof" abstraction without an identified first consumer.

### Accelerated Delivery Architecture (3 Integrated Tracks)
1. Track A: Canonical Semantics Core
	- contracts, invariants, identity, lifecycle/rule semantics, compatibility classes.
2. Track B: Execution + Serving Core
	- simulator runtime strategy, validity APIs (REST + MCP), diagnostics, auth/tenant/idempotency.
3. Track C: Projection + Consumption Core
	- interpretation projection, generated client contract, extension ingestion (v1 scope), minimal visual sidecar compatibility.

### Critical Path (What Must Exist Before First Customer Value)
1. Canonical model and invariant enforcement.
2. Deterministic rule/constraint evaluation with stable diagnostics codes.
3. Published version and compatibility gating.
4. Validity API + generated client in at least one primary ecosystem.
5. End-to-end validity-only customer scenario proving rule changes ship without customer rebuild.

### What We Are Explicitly Not Doing First
1. Rich visual editor implementation; only schema/sidecar compatibility and projection hooks.
2. Broad polyglot SDK explosion; one primary SDK + conformance harness first.
3. Maximal extension format coverage; only committed v1 set plus v1.1 trigger path.

### System-Level Acceptance Criteria (Replaces Checklist Sprawl)
1. Coherence: one canonical semantic source drives simulator, validity API, and generated artifacts with parity evidence.
2. Customer utility: a validity-only customer can ship rule changes independently of app rebuilds.
3. Safety: breaking changes are hard-gated by version policy and diagnostics are machine-actionable.
4. Operability: multi-tenant auth/audit/idempotency is enforceable and observable.
5. Evolvability: self-hosting level target achieved for model registry and governance workflow. v1 target is explicitly L0–L2 (model registry self-describes its schema, governance lifecycle is self-modeled, governance commands are self-validated through the evaluation pipeline). L3–L5 are deferred post-v1.

### 90-Day Accelerated Plan
1. Sprint 1-2: finalize canonical contracts/invariants + diagnostics/compatibility contracts; freeze v1 API envelopes.
2. Sprint 3-4: implement evaluation portability path and validity API + first generated client.
3. Sprint 5-6: implement publish/version gates, tenant/auth/idempotency, and end-to-end validity-only scenario.
4. Sprint 7-8: implement projection hardening, extension ingestion for v1 formats, and self-hosted governance proof.
5. Sprint 9-10: production hardening (parity, load, failure drills), release gate closure.

### Decision Filter (Use Before Accepting New Scope)
1. Does this reduce time for a customer to safely ship domain changes?
2. Does this increase semantic correctness or compatibility safety?
3. Does this reduce operational risk at multi-tenant scale?
4. If no to all three, defer or remove.

### Immediate Next Planning Output
1. Convert A-F details into an implementation backlog only for items on the critical path above.
2. Mark every backlog item with one of: customer-value, safety, operability, or defer.
3. Remove or park anything untagged.
4. Use solo-agent-task-board.md as the primary dispatch board for simple parallel task execution.

### Critical-Path Implementation Backlog (Execution-Ready)

Backlog rules:
1. Every item must have exactly one primary tag: customer-value, safety, operability, or defer.
2. If an item cannot be tied to a release gate or customer scenario, move it to defer.
3. No item enters implementation without acceptance criteria and owner lane.

Owner lanes:
1. Core: canonical contracts, invariants, compatibility semantics.
2. Runtime: evaluation engine, simulator parity, routing/consistency.
3. Serving: REST/MCP surfaces, auth/tenant/idempotency, governance endpoints.
4. SDK: generated client contracts and conformance tests.
5. Ops: observability, runbooks, release gates, reliability drills.

Epic 1: Canonical Semantics Foundation (Sprint 1-2)
1. Define canonical contract package boundaries and v1 contract set.
	- Tag: customer-value
	- Owner: Core
	- Acceptance: v1 contract inventory frozen and referenced by all downstream items.
2. Implement invariant matrix as enforceable construction/compile rules.
	- Tag: safety
	- Owner: Core
	- Acceptance: invariant failures emit structured diagnostics with stable codes.
3. Finalize SemanticId and dual-versioning conventions in all canonical contracts.
	- Tag: safety
	- Owner: Core
	- Acceptance: IDs and versions are present and validated in canonical snapshots.
4. Finalize rule compatibility classification and publish-gate policy.
	- Tag: safety
	- Owner: Core
	- Acceptance: breaking changes are blocked without required major version increment.

Epic 2: Deterministic Evaluation Core (Sprint 3-4)
1. Implement deterministic rule/constraint evaluation pipeline with two modes (fast-fail/full-report).
	- Tag: customer-value
	- Owner: Runtime
	- Acceptance: same input + version yields identical pass/fail and diagnostic codes.
2. Implement command lifecycle pipeline up to evaluation and mutation planning boundaries.
	- Tag: customer-value
	- Owner: Runtime
	- Acceptance: command intake, route resolution, preconditions, and planned effects are traceable.
3. Implement lifecycle transition validation and post-mutation invariant re-check.
	- Tag: safety
	- Owner: Runtime
	- Acceptance: invalid transitions and post-mutation invariant breaks are blocked with diagnostics.
4. Establish parity fixtures shared by simulator/hosted validity execution.
	- Tag: safety
	- Owner: Runtime
	- Acceptance: parity suite baseline exists and gates runtime profile changes.

Epic 3: Validity Service v1 (Sprint 3-6)
1. Implement REST/JSON validity metadata and evaluate endpoints.
	- Tag: customer-value
	- Owner: Serving
	- Acceptance: metadata/evaluate/explain endpoints available with versioned envelopes.
2. Implement equivalent MCP resources/tools for model discovery and evaluation.
	- Tag: customer-value
	- Owner: Serving
	- Acceptance: MCP and REST semantics are parity-tested for shared operations.
3. Implement tenant isolation and authorization scopes for all service operations.
	- Tag: safety
	- Owner: Serving
	- Acceptance: cross-tenant access attempts are blocked and auditable.
4. Implement idempotency key enforcement for mutation-like control operations.
	- Tag: safety
	- Owner: Serving
	- Acceptance: duplicate requests with same key replay original result within retention window.

Epic 4: Generated Client v1 (Sprint 4-6)
1. Define first primary SDK contract profile for validity-only customers.
	- Tag: customer-value
	- Owner: SDK
	- Acceptance: typed request/response and diagnostics-first error model finalized.
2. Generate SDK from service contracts and publish conformance test harness.
	- Tag: customer-value
	- Owner: SDK
	- Acceptance: SDK passes wire-contract and diagnostics-parity tests.
3. Add compatibility-aware version negotiation helpers to SDK.
	- Tag: safety
	- Owner: SDK
	- Acceptance: SDK can detect incompatible versions and surface actionable guidance.

Epic 5: Governance and Publish Controls (Sprint 5-7)
1. Implement draft/stage/publish workflow and policy checks.
	- Tag: customer-value
	- Owner: Serving
	- Acceptance: governance states are enforced with auditable transitions.
2. Implement compatibility impact analysis endpoint for proposed changes.
	- Tag: safety
	- Owner: Core
	- Acceptance: changes classified as additive/non-breaking/soft-breaking/breaking/contract-breaking.
3. Enforce publish hard-gates for breaking changes and missing migration signals.
	- Tag: safety
	- Owner: Serving
	- Acceptance: non-compliant publish attempts fail deterministically with policy diagnostics.

Epic 6: End-to-End Customer Proof (Sprint 6-8)
1. Build validity-only customer reference scenario.
	- Tag: customer-value
	- Owner: SDK
	- Acceptance: customer app validates domain payloads via hosted API using generated client.
2. Demonstrate rule-change rollout without customer rebuild.
	- Tag: customer-value
	- Owner: Serving
	- Acceptance: compatible rule update is published and consumed by client without redeploy.
3. Demonstrate deterministic diagnostics and compatibility handling during rollout.
	- Tag: safety
	- Owner: Runtime
	- Acceptance: observed results match compatibility class and diagnostic contracts.

Epic 7: Projection and Interoperability Minimum (Sprint 7-9)
1. Implement interpretation projection required by runtime and validity service.
	- Tag: customer-value
	- Owner: Core
	- Acceptance: projection outputs preserve SemanticId traceability and parity requirements.
2. Implement extension ingestion for v1 formats (OpenAPI, AsyncAPI, JSON Schema, gRPC/proto).
	- Tag: customer-value
	- Owner: Serving
	- Acceptance: imported contracts map through anti-corruption layer with deterministic diagnostics.
3. Implement minimal visual sidecar schema compatibility (no rich editor).
	- Tag: defer
	- Owner: Core
	- Acceptance: sidecar references resolve; editor ergonomics remain out of v1 scope.

Epic 8: Operational Hardening and Release (Sprint 8-10)
1. Implement mandatory telemetry and trace coverage for control-plane and evaluation paths.
	- Tag: operability
	- Owner: Ops
	- Acceptance: command/evaluation/governance traces meet correlation and audit requirements.
2. Run failure drills: auth, tenant isolation, idempotency replay, publish gate failures.
	- Tag: operability
	- Owner: Ops
	- Acceptance: runbooks validated and incident response playbooks updated.
3. Execute release gate verification against system-level acceptance criteria.
	- Tag: operability
	- Owner: Ops
	- Acceptance: all gate artifacts approved and release kickoff authorized.

Backlog Parking Lot (Explicit Defer)
1. Rich Blueprint editing UX and advanced layout automation.
2. Additional SDK ecosystems beyond primary v1 SDK.
3. GraphQL SDL ingestion until v1.1 trigger criteria are met.
4. Non-critical projection targets not required for validity-only and first generated-runtime paths.
5. Test generation and fuzzing projection (v2 target): the canonical model is a first-class source for automated test generation. Because the model encodes the exact shape of valid and invalid inputs by construction, the following generators are derivable as projection targets under a new `TestGeneration` track:
   - Property-based test generator: derives typed input generators and shrinkers directly from property constraints (MinLength, Range, Regex, Required, etc.) and emits framework-agnostic theory/QuickCheck-style test cases with boundary-value and invalid-value corpora.
   - FSM conformance harness: from LifecycleModel (states + valid transitions), generates every valid path and every invalid-transition attempt; asserts correct pass/fail outcomes and stable diagnostic codes.
   - Mutation round-trip harness: applies each Command/Mutation against a simulator instance, re-evaluates all invariants and postconditions, and asserts the before/after state pair satisfies the declared contract.
   - Regression diff generator: when a new model version is published, reruns generators against both versions and produces a diff of changed test cases — effectively an automated regression surface delivered alongside rule updates.
   - Validity-only customer distribution: generated test suites can be distributed to customers the same way the validity API is, so customers can verify their own implementations against the published rule set without manual test authoring.
   Trigger criteria (same gate policy as other v2 scope): stable projection pipeline (T70 + T73 done), one identified customer with a concrete test-authoring pain point, and a fixed post-v1 timeline commitment.
6. Multi-library project extraction (post-v1): keep implementation in the existing `Poly` project during v1 delivery; split into dedicated libraries only after v1 release-gate completion, preserving behavior and public contracts.

Kickoff Definition of Ready
1. Each in-scope backlog item has owner lane, acceptance criteria, and dependency mapping.
2. Critical path items are marked with release gate linkage.
3. Any item without tag or gate linkage is moved to defer before sprint planning.

Replace the entire DomainModeling subsystem with a v2 architecture that enforces constructor-valid invariants (RAII), models full domain semantics (types, properties, constraints, cross-property rules, relationships and relationship constraints, lifecycle FSM, commands, mutations, events), emits interpretation-ready artifacts for codegen/simulation, and supports agent plus Blueprint-style tooling via MCP with separate visual metadata persistence.

**Steps**
1. Phase 0: SaaS product and platform guardrails.
2. Phase 0 detail: Define execution semantics contract (determinism, transaction boundaries, rollback behavior, event ordering), multi-tenant trust boundaries, MCP authorization model, audit requirements, initial performance/error budgets, and architectural separation rules so domain semantics are never collapsed into persistence DTOs before implementation.
3. Phase 1: Define immutable v2 metamodel contracts and invariants.
4. Phase 1 detail: Introduce explicit root contracts for model, type, property, relationship, lifecycle, command, mutation, and event; validate cross-object invariants at creation time and fail fast.
5. Phase 2: Build RAII-first construction APIs.
6. Phase 2 detail: Use draft/build-session APIs that only produce immutable valid objects at commit; no partially valid instances may escape.
7. Phase 3: Unify validation semantics.
8. Phase 3 detail: Merge property constraints and cross-property rules into one composable invariant model with deterministic evaluation and diagnostics.
9. Phase 4: Implement behavioral core.
10. Phase 4 detail: Encode relationship semantics, FSM transitions, command invocation contracts, mutation effects, command-to-mutation-to-event linkage, and aggregate-boundary inference with compile-time model validation.
11. Phase 5: Implement interpretation projection pipeline.
12. Phase 5 detail: Replace legacy AST extension flow with a v2 projector that emits interpretation nodes/expressions for structure and behavior, with stable IDs for traceability and round-trip.
13. Phase 6: Add simulator runtime.
14. Phase 6 detail: Implement deterministic in-memory execution for commands, preconditions, mutations, transitions, and event publication with replay hooks; define how inferred aggregate roots map to executable runtime boundaries.
15. Phase 7: Add Blueprint graph representation.
16. Phase 7 detail: Define node/port/edge/group schema, valid connection rules, and bidirectional mapping between graph edits and model updates.
17. Phase 8: Add visual sidecar persistence.
18. Phase 8 detail: Store layout/style/geometry in a separate companion artifact keyed by stable IDs; keep canonical domain schema UI-agnostic.
19. Phase 9: Add MCP serving for agents and UI.
20. Phase 9 detail: Expose model discovery/query, interpretation graph retrieval, visual sidecar retrieval/update, validation/simulation analysis tools, and a customer-consumable validity API surface for remote type/constraint/rule access.
21. Phase 10: Hard cutover and cleanup.
22. Phase 10 detail: Delete/replace legacy DomainModeling paths and switch tests/integration to v2 only.
23. Phase 11: Complete conformance test suite and release gate.
24. Phase 12: Add extension and interoperability framework.
25. Phase 12 detail: Support importing and projecting extensions through common API description and integration formats such as OpenAPI and MCP adapters, with explicit anti-corruption mappings into the canonical domain model.

**Architectural Style Constraints**
1. Domain-first implementation: the core model must represent business semantics directly rather than mirroring ORM entities, transport DTOs, or storage tables.
2. Strict boundary separation: persistence schemas, API contracts, MCP resource payloads, simulator state snapshots, and generated customer-facing DTOs must be explicit projections from the domain model rather than the domain model itself.
3. Generated artifacts must preserve intent: produce task-oriented command/query contracts, aggregate-focused APIs, and explicit read models instead of exposing raw persistence-shaped records as the primary programming surface.
4. Rich behavior over anemic data bags: invariants, transitions, commands, and domain events should remain attached to domain concepts; generated code should avoid encouraging CRUD-only workflows when richer business operations exist.
5. Projection-specific models: allow separate read-optimized, persistence-optimized, and integration-optimized artifact generation, each traceable back to the canonical domain model.
6. Persistence is an adapter, not the center: database/storage choices must compile from the model through dedicated build targets and mapping layers, so customer artifacts can evolve storage independently from domain semantics.
7. Self-hosting must be possible: the system must be capable of modeling its own bounded contexts, aggregates, behaviors, UI graph metadata, and artifact-generation pipeline within the canonical domain model.
8. Self-description must be first-class: the model must be rich enough that agents and humans can query the system's own structure, semantics, and generation rules through the same MCP and projection mechanisms offered to customers.
9. Standard-format extensibility must be first-class: external capabilities should be ingestible and exposable through common interface formats such as OpenAPI, AsyncAPI where relevant, and MCP adapters rather than only through custom integration code.
10. Validity-only consumption must be supported: customers must be able to consume domain types, constraints, and rules as a remotely served capability without adopting the full generated runtime or rebuilding their own software for every rule change.

**Non-Goals and Banned Failure Modes**
1. Do not make ORM entities or database tables the canonical domain model.
2. Do not expose persistence entities directly as API contracts, MCP payloads, UI contracts, or generated SDK types.
3. Do not generate CRUD-first surfaces as the default customer programming model when the domain defines richer commands, invariants, and transitions.
4. Do not collapse aggregates into flat record bags that rely on external services or controllers to enforce all business rules.
5. Do not allow anemic domain artifacts where state changes bypass command/precondition/invariant enforcement.
6. Do not let read-model concerns, query optimization needs, or reporting shapes back-drive the canonical domain semantics.
7. Do not couple generated customer artifacts tightly to any one storage engine, ORM, transport protocol, or UI framework.
8. Do not make aggregate inference opaque: every inferred aggregate boundary must be explainable, reviewable, and overrideable.
9. Do not allow Blueprint graph structure or visual layout metadata to become the canonical semantic source of truth.
10. Do not allow MCP edit operations to mutate models outside validated construction and compilation pathways.
11. Do not permit silent fallback from rich behavioral generation to generic DTO CRUD generation without an explicit diagnostic.
12. Do not treat simulator internals, persistence snapshots, or integration wire formats as substitutes for the canonical domain runtime contracts.
13. Do not ingest external API descriptions directly as canonical domain semantics without explicit mapping, validation, and boundary review.
14. Do not force customers into full generated runtime adoption when their use case only requires externally served validity and rule evaluation capabilities.

Dependency notes:
1. Phase 0 defines non-negotiable product/platform constraints and should complete before Phase 1 implementation.
2. Phase 1 blocks all semantic implementation phases.
3. Phases 2 and 3 can proceed in parallel after Phase 1 stabilizes.
4. Phase 4 depends on Phases 1 to 3.
5. Phase 5 depends on Phases 3 and 4.
6. Phase 6 depends on Phases 4 and 5.
7. Phase 7 depends on Phases 1 and 4 (and ties into Phase 5 outputs).
8. Phase 8 depends on Phase 7.
9. Phase 9 depends on Phases 5 to 8.
10. Phase 10 depends on Phases 0 to 9.
11. Phase 11 runs throughout, with final gate after Phase 10.
12. Phase 12 depends on Phases 1, 5, and 9 so imported integrations are normalized through canonical semantics and exposed through governed runtime boundaries.

**SaaS Considerations Added**
1. Concurrency and consistency model: include optimistic concurrency/version tokens, idempotent command handling, and explicit cross-aggregate consistency policy.
2. Stable identity strategy: define rename-safe element IDs and runtime instance IDs separately to preserve graph and MCP references across edits.
3. Validation tiers: author-time structural checks, compile-time semantic checks, runtime precondition/invariant checks, and post-run consistency audits.
4. Extensibility boundaries: formally define plugin points for custom constraints/rules/effects/projectors and MCP tool extensions.
5. Security model for agent-driven edits: capability-based MCP permissions, strict input schemas, sandboxing policy for executable expressions, and immutable audit trails.
6. Governance workflow: draft/staging/published model states, approval gates, and breaking-change detection policy.
7. Observability and explainability: command execution traces, rule-failure explanations, transition rejection reasons, and event causality chains as first-class outputs.
8. Performance and scale targets: latency/SLO budgets for analysis/projection/simulation/MCP and max supported model size targets.
9. v2 evolution policy: versioning and compatibility policy for future v2 schema changes, including feature-flagged experimental capabilities.
10. Aggregate inference strategy: define deterministic rules for identifying aggregate roots and consistency boundaries from model semantics, while allowing explicit overrides where inference is ambiguous.
11. Virtual actor build target: design generated artifacts so inferred aggregates can map cleanly to virtual actor/grain-style runtimes for scalable execution, isolation, and state ownership.
12. Projection policy: define which artifact categories are generated from the canonical domain model, including domain runtime types, persistence projections, read models, integration contracts, and UI-facing view models.
13. Anti-corruption boundaries: require explicit mapping layers between canonical domain semantics and external contracts so customer integrations do not force the model into DTO-shaped compromises.
14. Self-hosting roadmap: define the staged path by which the platform first models its own metadata and workflows, then progressively replaces hand-authored implementation areas with self-modeled/generated equivalents.
15. Extension ingestion model: define how standard external descriptions such as OpenAPI specs, MCP tools/resources, and other integration formats are imported, normalized, versioned, validated, and mapped into extension points.
16. Integration contract strategy: separate imported external contracts from canonical domain contracts while allowing generated adapters, facades, and anti-corruption layers.
17. Validity service mode: define a remotely hosted domain validity surface that serves canonical type metadata, constraints, rules, diagnostics, and versioned evaluation endpoints to generated clients.
18. Client generation strategy: generate lightweight client SDKs for validity-only consumers so customer-owned software can request validation, inspect rule metadata, and react to rule changes without recompilation.

**Detailed Implementation Planning**
1. Planning rule: do not write production code until the contract, invariants, diagnostics, and projection boundaries for the current milestone are documented and reviewed.
2. Planning artifact set for every milestone: a contract spec, invariants list, diagnostic catalog, serialization examples, and at least one end-to-end usage example.

**Detailed Workstreams**
1. Canonical metamodel workstream: define the core object model for bounded contexts, aggregates, entities/value objects where applicable, properties, relationships, commands, mutations, lifecycle, events, rules, constraints, IDs, and versioning metadata.
2. Validation semantics workstream: define the unified invariant model, diagnostic shapes, explainability structures, rule compatibility semantics, and remote validity contracts.
3. Projection workstream: define transformation contracts from canonical model to interpretation artifacts, read models, persistence projections, integration contracts, and Blueprint graph sidecars.
4. Runtime workstream: define simulator execution semantics, command routing, aggregate boundary enforcement, event publication rules, replay model, and optional virtual actor target mapping; evaluate simulator execution strategies and select one via benchmarked decision gate.
5. Serving workstream: define MCP resources/tools, validity-only service APIs, generated client SDK contracts, authorization boundaries, and audit envelopes.
6. Interoperability workstream: define external extension ingestion contracts for OpenAPI, MCP-backed integrations, and other supported interface formats with anti-corruption mapping rules.
7. Self-hosting workstream: define how the platform models itself, what subsystem is first to become self-hosted, and what evidence is required before expanding self-generation.

**Detailed Milestones**
1. Milestone A: Canonical contract freeze.
2. Milestone A deliverables: metamodel glossary, object identity strategy, constructor/factory invariant matrix, JSON schema sketches, and explicit banned anti-pattern enforcement strategy.
3. Milestone B: Semantic execution design.
4. Milestone B deliverables: command lifecycle specification, rule evaluation semantics, aggregate inference heuristics plus override model, compatibility/change taxonomy, and diagnostics contract examples.
5. Milestone C: Projection and graph design.
6. Milestone C deliverables: interpretation projection spec, Blueprint graph schema, sidecar schema, read-model projection policy, persistence projection policy, and traceability rules between canonical IDs and generated artifacts.
7. Milestone D: Service surface design.
8. Milestone D deliverables: MCP capability catalog, validity API contract, generated client surface design, auth/audit contract, tenant partitioning model, and extension ingestion lifecycle.
9. Milestone E: Runtime target design.
10. Milestone E deliverables: simulator architecture spec, virtual actor target mapping spec, consistency/concurrency rules, event routing model, failure/rollback policy, and a simulator strategy decision record with benchmark and operability evidence.
11. Milestone F: Self-hosting and release readiness design.
12. Milestone F deliverables: self-hosting maturity ladder, first self-hosted subsystem definition, release gates, operability checklist, and adoption paths for generated-runtime and validity-only customers.

**Milestone A Design Worksheet (Implementation Planning Only)**

**A1. Canonical Glossary (v1 Draft)**
1. DomainModel: the canonical top-level semantic container for bounded contexts, types, relationships, behavior, policies, and version metadata.
2. BoundedContext: a semantic boundary containing aggregate definitions, integration boundaries, and local ubiquitous language.
3. Aggregate: the transactional consistency boundary and command-routing unit; represented explicitly and inferable with deterministic explainable metadata.
4. AggregateRootType: the primary type in an aggregate through which state-changing operations are invoked.
5. DomainType: a modeled semantic type (entity, value object, or conceptual type) with properties, invariants, and lifecycle participation.
6. DomainProperty: a typed member on a DomainType with constraints, optional defaults, facets, and state-dependent behavior.
7. Constraint: a local validity predicate attached to a property or relationship endpoint.
8. Rule: a composable cross-property or cross-context invariant with deterministic evaluation semantics.
9. Command: an intent-bearing operation contract that may trigger validation, mutation, transitions, and events.
10. Mutation: an internal state-change operation that applies effects under preconditions.
11. DomainEvent: an immutable occurrence emitted by behavior execution with causal metadata.
12. LifecycleModel: a finite-state model for a type or aggregate including states, transitions, and transition guards.
13. Projection: a derived representation of canonical semantics for a target purpose (interpretation, read model, persistence, integration, visual graph).
14. ValidityService: hosted endpoint surface that serves type/rule metadata and validation/evaluation contracts to external clients.
15. Diagnostic: machine-readable evaluation result item with stable code, severity, path reference, and optional localized message.
16. ExtensionContract: imported external API or schema description normalized through anti-corruption mapping.
17. VisualSidecar: separate artifact containing visual graph layout and style metadata keyed by stable semantic IDs.
18. SemanticVersion: version marker for canonical contract compatibility and change classification.

**A2. Identity Strategy (v1 Draft)**
1. Every canonical semantic node has a stable SemanticId independent of display name.
2. SemanticId is immutable after creation and survives rename/refactor operations.
3. ExternalReferenceId is optional and intended for interop with customer or partner systems.
4. Derived projection artifacts must include source SemanticId trace fields.
5. Diagnostic path references must resolve to SemanticId-backed members.
6. Visual sidecar node references are keyed by SemanticId, never by display labels.

**A3. Invariant Matrix (v1 Draft)**
1. DomainModel invariants:
	- Must contain at least one BoundedContext.
	- SemanticVersion must be present.
	- All SemanticIds must be unique across the model.
	- Cross-context references must declare integration boundaries explicitly.
2. BoundedContext invariants:
	- Name and SemanticId required.
	- Must contain at least one DomainType or Aggregate declaration.
	- Imported ExtensionContracts must be mapped through anti-corruption rules before use.
3. Aggregate invariants:
	- Must reference exactly one AggregateRootType.
	- Must provide explainable inference metadata if inferred.
	- Command routing target must be unambiguous.
4. DomainType invariants:
	- Name, SemanticId, and category required.
	- Property names unique within the type.
	- Lifecycle references must resolve to declared lifecycle models.
5. DomainProperty invariants:
	- Name, SemanticId, and type expression required.
	- Default value must satisfy applicable constraints.
	- State-specific facet overrides must reference declared states.
6. Constraint invariants:
	- Must declare applicability to specific type categories.
	- Parameters must be complete and type-valid.
	- Must be deterministic and side-effect free.
7. Rule invariants:
	- Inputs must resolve to declared members or value sources.
	- Evaluation must be deterministic and order-stable unless explicitly commutative.
	- Rule dependencies must not form unresolved cycles.
8. Command invariants:
	- Name, SemanticId, target aggregate/type, and parameter schema required.
	- Preconditions must resolve before mutation execution.
	- Authorization and tenancy policy references must be explicit in serving layer projections.
9. Mutation invariants:
	- Must target declared writable members.
	- Effects must preserve invariant contracts after execution.
	- Emitted events, if any, must map to declared DomainEvent contracts.
10. DomainEvent invariants:
	- Name, SemanticId, and payload schema required.
	- Payload fields must reference declared type expressions.
	- Causality metadata fields are required for runtime tracing projections.
11. LifecycleModel invariants:
	- Exactly one initial state.
	- Transition source and target states must exist.
	- Terminal states cannot have outgoing non-compensating transitions unless explicitly marked.
12. Projection invariants:
	- Must preserve SemanticId traceability.
	- Must not introduce canonical semantic changes.
	- Projection failures must emit structured diagnostics.
13. ValidityService invariants:
	- Served metadata must correspond to a published SemanticVersion.
	- Evaluation responses must include diagnostic code contract version.
	- Compatibility class must be declared for each published rule set update.

**A4. Diagnostic Contract Schema (v1 Draft)**
1. Required fields per diagnostic item:
	- code: stable machine code string (namespace scoped).
	- severity: Error, Warning, Info.
	- category: Structural, Semantic, Compatibility, Authorization, Runtime.
	- messageTemplate: localization key or canonical template token.
	- path: semantic path with SemanticId and optional property segment.
	- correlationId: execution/request correlation handle.
	- ruleId: optional SemanticId of originating rule/constraint.
2. Optional fields:
	- localizedMessage: resolved display text.
	- details: structured object for typed context.
	- suggestion: machine-actionable remediation hint.
3. Response envelope fields:
	- diagnosticsVersion
	- modelSemanticVersion
	- evaluationMode (simulate, validate, compile-check)
	- compatibilityClass (breaking, non-breaking, additive)

**A5. Rule Compatibility and Versioning Table (v1 Draft)**
1. Add metadata-only annotation: Non-breaking.
2. Improve localized text without code change: Non-breaking.
3. Add optional rule that does not reject previously valid payloads: Non-breaking.
4. Add warning-only advisory rule: Non-breaking.
5. Tighten numeric/string bounds so previously valid data can fail: Breaking.
6. Add new required field/relationship for acceptance: Breaking.
7. Change rule semantics that alters pass/fail outcome: Breaking.
8. Reorder evaluation with identical outcomes and identical diagnostics codes: Non-breaking.
9. Change diagnostic code identifiers for existing failures: Breaking for client integrations.

**A6. Milestone A Review Checklist**
1. Glossary definitions approved by platform, runtime, and SDK stakeholders.
2. Invariant matrix reviewed against RAII guarantee for constructor/factory completion.
3. Diagnostic schema validated for generated client usability.
4. Compatibility table approved for validity-only delivery commitments.
5. Anti-pattern bans mapped to at least one planned analyzer or build-time check each.
6. ADR records created for identity strategy and aggregate representation model.

**A7. Exit Criteria for Milestone A**
1. No unresolved canonical-term ambiguity remains in the glossary.
2. Every core contract listed in A3 has documented required fields and invariants.
3. Diagnostic contract sample payloads exist for success, warning, and failure scenarios.
4. Compatibility classification rules are documented and testable.
5. Design review sign-off completed for Core, Validation, Projection, Serving, and Runtime workstreams.

**Milestone B Design Worksheet (Implementation Planning Only)**

**B1. Command Lifecycle Semantics (v1 Draft)**
1. Intake: command envelope is validated for schema, tenant scope, authorization context, and idempotency key presence.
2. Resolution: command target aggregate and root instance are resolved by explicit route; ambiguous routing fails with structured diagnostics.
3. Precondition phase: command-level preconditions and referenced rules are evaluated before mutation effects.
4. Mutation planning phase: eligible effects are ordered deterministically and bound to resolved value sources.
5. Apply phase: mutation effects are executed against a consistent aggregate snapshot under transactional boundary rules.
6. Invariant re-check phase: post-mutation invariants and lifecycle transition guards are re-evaluated.
7. Transition phase: lifecycle state transition is attempted only if guards pass and transition is declared valid.
8. Event phase: domain events are emitted in deterministic order with causality references.
9. Commit phase: state and emitted events are committed atomically according to selected runtime strategy.
10. Response phase: outcome envelope returns status, diagnostics, compatibility class, and trace correlation.

**B2. Rule and Constraint Evaluation Semantics (v1 Draft)**
1. Deterministic ordering: evaluation order follows canonical declaration order, then stable SemanticId ordering as tie-break.
2. Short-circuit policy: logical composition supports short-circuit for runtime efficiency while preserving required diagnostics behavior.
3. Diagnostic completeness modes:
	- fast-fail mode: stop after first blocking error per branch.
	- full-report mode: continue to collect all deterministic diagnostics for client tooling.
4. Null/missing semantics: explicit distinction between null value, missing field, and inaccessible field due to policy.
5. Side-effect rule: rule and constraint evaluation is pure and side-effect free.
6. Context inputs: evaluation context explicitly includes tenant, execution mode, model version, and time source abstraction.
7. Consistency guarantee: same input envelope and model version must produce identical pass/fail and diagnostic codes.

**B3. Aggregate Inference Heuristics and Override Model (v1 Draft)**
1. Primary heuristic: types receiving state-changing commands and owning lifecycle transitions are aggregate root candidates.
2. Relationship heuristic: ownership-style relationships with invariant coupling suggest same aggregate boundary.
3. Consistency heuristic: invariants requiring atomic multi-member updates imply shared boundary.
4. Event heuristic: command-to-event causality concentrated on one root supports aggregate root selection.
5. Conflict handling: if heuristics produce conflicting boundaries, inference emits warning diagnostics and requires explicit override.
6. Override model:
	- explicit aggregate declarations always win.
	- explicit exclusion/inclusion rules are supported per type/relationship.
	- override decisions are audit-logged with rationale metadata.
7. Explainability output: inference engine must publish structured explanation per inferred boundary.

**B4. Compatibility Taxonomy (v1 Draft)**
1. Additive: change adds optional capabilities without altering previous acceptance outcomes.
2. Non-breaking behavioral: internal optimization or evaluation reordering with identical outcomes and diagnostic codes.
3. Soft-breaking: same accept/reject outcomes but diagnostic code or category changes that may affect client automation.
4. Breaking: any change that can alter accept/reject outcomes, required fields, required transitions, or event contract obligations.
5. Contract-breaking: API envelope or generated client contract changes requiring customer integration updates.

**B5. Compatibility Examples (v1 Draft)**
1. Add optional warning rule with new advisory code only: Additive.
2. Tighten max length from 64 to 32 on accepted field: Breaking.
3. Add new optional command parameter ignored when absent: Additive.
4. Rename diagnostic code while keeping same semantics: Soft-breaking.
5. Require lifecycle transition approval where previously implicit: Breaking.
6. Add event payload field marked optional: Additive.
7. Change event payload field from optional to required: Breaking.
8. Replace REST response envelope property name used by generated clients: Contract-breaking.

**B6. Semantic Execution Diagnostics Catalog (v1 Draft)**
1. CMD-INTAKE-* : envelope/schema/idempotency failures.
2. CMD-AUTH-* : authorization and tenant-scope failures.
3. CMD-ROUTE-* : aggregate resolution and routing failures.
4. RULE-EVAL-* : rule/constraint evaluation failures.
5. MUT-APPLY-* : mutation planning and effect application failures.
6. LIFE-TRANS-* : lifecycle transition validation failures.
7. EVT-EMIT-* : event emission contract failures.
8. COMPAT-* : compatibility policy violations on publish/promote flows.

**B7. Milestone B Review Checklist**
1. Command lifecycle steps are unambiguous and mapped to diagnostics.
2. Rule evaluation semantics are deterministic and mode-aware (fast-fail/full-report).
3. Inference heuristics produce explainable output and deterministic overrides.
4. Compatibility taxonomy is mapped to versioning and publish policy enforcement.
5. Example matrix reviewed by SDK/client teams for validity-only consumption impact.
6. ADR records created for command lifecycle, inference strategy, and compatibility taxonomy.

**B8. Exit Criteria for Milestone B**
1. Lifecycle and command execution sequence approved by Runtime and Serving workstreams.
2. Rule evaluation semantics documented with at least one normative example per operator family.
3. Aggregate inference/override design approved with explainability sample outputs.
4. Compatibility taxonomy is tied to publish gates and major/minor version policy.
5. Diagnostics catalog prefixes and namespaces are approved for generated client consumption.

**Milestone C Design Worksheet (Implementation Planning Only)**

**C1. Projection Architecture Model (v1 Draft)**
1. Canonical source of truth: only canonical model artifacts are projection inputs.
2. Projection stages:
	- semantic normalization stage
	- target projection stage
	- target validation stage
	- diagnostics emission stage
3. Projection output families:
	- interpretation graph/expression projection
	- read-model projection
	- persistence projection
	- integration contract projection
	- visual graph sidecar projection
4. Projection determinism: same canonical version and projection configuration produce byte-stable outputs except explicitly non-deterministic metadata fields (timestamps/build IDs).
5. Projection isolation: target-specific failures must not mutate canonical model state.

**C2. Interpretation Projection Specification (v1 Draft)**
1. Input set:
	- canonical types/properties
	- constraints/rules
	- lifecycle definitions
	- commands/mutations/events
	- aggregate boundary metadata
2. Output set:
	- typed interpretation nodes
	- execution metadata
	- analysis annotations
	- source-to-target trace map
3. Required guarantees:
	- semantic equivalence with canonical rule behavior
	- deterministic node identity mapping from SemanticId
	- stable diagnostics path mapping for evaluation/runtime results
4. Projection diagnostics categories:
	- PROJ-INPUT-* (invalid or missing canonical input)
	- PROJ-MAP-* (failed semantic mapping)
	- PROJ-TGT-* (target constraint violations)
	- PROJ-COMPAT-* (version/compatibility projection issues)

**C3. Blueprint Graph Schema (v1 Draft)**
1. Node model:
	- nodeId (stable, sidecar-local but mapped to SemanticId)
	- semanticRef (SemanticId)
	- nodeType (type, command, event, rule, lifecycle-state, transition)
	- display metadata (title, subtitle, badges)
2. Port model:
	- portId
	- direction (in/out/bidirectional)
	- portKind (data, control, relation, constraint)
	- semanticPathRef
3. Edge model:
	- edgeId
	- sourceNode/port
	- targetNode/port
	- semantic meaning (dependency, transition, relation, emits, validates)
4. Group model:
	- groupId
	- purpose (aggregate, bounded context, subsystem)
	- contained node references
5. Graph validation rules:
	- invalid edge categories rejected with diagnostics
	- orphan nodes flagged with warnings
	- duplicate semanticRefs in conflicting contexts rejected

**C4. Visual Sidecar Schema (v1 Draft)**
1. Sidecar envelope:
	- sidecarVersion
	- modelSemanticVersion
	- graphSchemaVersion
	- generatedAt
2. Layout fields:
	- node coordinates, dimensions, z-order
	- group boundaries
	- viewport metadata
3. Style fields:
	- palette/theme tokens
	- semantic emphasis tags (critical, warning, derived)
4. Interaction state fields (optional):
	- collapsed groups
	- pin/focus metadata
	- editor hints
5. Sidecar invariants:
	- no semantic authority (advisory only)
	- all semantic references must resolve to canonical model
	- unsupported visual fields are preserved as opaque extension blobs for forward compatibility

**C5. Traceability Contract (v1 Draft)**
1. Every projected artifact record includes source SemanticId.
2. Multi-source derived projections include ordered sourceRefs list.
3. Diagnostics emitted from projections include both projection-local path and source semantic path.
4. Generated clients and runtime traces must surface semanticRef when reporting behavior or failures.
5. Trace maps are versioned and persisted for debugging and support workflows.

**C6. Projection Policy Matrix (v1 Draft)**
1. Interpretation projection:
	- required for simulator and validity service execution
	- blocking on semantic mismatch
2. Read-model projection:
	- required for query-facing generated artifacts
	- warning or blocking based on declared query profile requirements
3. Persistence projection:
	- required for generated persistence adapters
	- non-blocking for validity-only customers
4. Integration projection:
	- required when extension contracts are active
	- blocking on anti-corruption mapping failures
5. Visual sidecar projection:
	- required for Blueprint authoring mode
	- non-blocking for headless validity-only deployments

**C7. Projection Failure Handling (v1 Draft)**
1. Projection failures never alter canonical state.
2. Failure responses include deterministic diagnostics and compatibility class.
3. Partial projection output is only allowed for explicitly non-blocking targets and must be tagged incomplete.
4. Blocking projection failures prevent publish/promote operations for affected deployment profiles.

**C8. Milestone C Review Checklist**
1. Projection stages and failure boundaries reviewed by Core, Projection, Runtime, and Serving stakeholders.
2. Interpretation projection guarantees mapped to simulator and validity API portability requirements.
3. Graph schema and sidecar schema validated against Blueprint UX requirements.
4. Traceability contract validated with diagnostics and support workflows.
5. Projection policy matrix reviewed for generated-runtime and validity-only customer paths.

**C9. Exit Criteria for Milestone C**
1. Interpretation projection spec approved with normative examples.
2. Graph + sidecar schemas approved with compatibility/versioning policy.
3. Traceability rules approved and mapped to diagnostics and runtime traces.
4. Projection policy matrix approved for all deployment profiles.
5. Projection failure handling approved and aligned with publish gate policy.

**Milestone D Design Worksheet (Implementation Planning Only)**

**D1. Service Surface Topology (v1 Draft)**
1. Control-plane surfaces:
	- model registry operations
	- lifecycle/governance operations (draft, stage, publish, promote)
	- extension ingestion and mapping operations
2. Data-plane surfaces:
	- validity evaluation endpoints
	- model/query metadata endpoints
	- diagnostics and trace retrieval endpoints
3. Interface families:
	- REST/JSON with OpenAPI contracts
	- MCP resources/tools for agent-oriented workflows
4. Contract parity rule: REST and MCP capabilities must share equivalent semantic outcomes and compatibility behavior.

**D2. MCP Capability Catalog (v1 Draft)**
1. Model discovery capabilities:
	- list models
	- get model by version
	- list bounded contexts/aggregates/types
2. Validation and analysis capabilities:
	- evaluate payload against type/rules
	- explain rule failures
	- analyze compatibility impact for proposed changes
3. Governance capabilities:
	- create/update draft
	- stage draft
	- publish staged version
	- compare versions
4. Projection capabilities:
	- fetch interpretation projection
	- fetch sidecar graph projection
	- fetch read/integration projection metadata
5. Extension capabilities:
	- import extension contract
	- validate mapping
	- list mapping diagnostics

**D3. Validity API Contract (v1 Draft)**
1. Endpoint families:
	- metadata: types, constraints, rules, lifecycle definitions
	- evaluate: validate payload/command request
	- explain: diagnostic expansion and rule trace
	- compatibility: impact checks between versions
2. Envelope requirements:
	- tenantId
	- modelVersion and/or rulesetVersion
	- correlationId
	- idempotencyKey for mutation-like control operations
3. Response guarantees:
	- structured diagnostics schema
	- compatibility class
	- trace references and semantic path references
4. Backward compatibility guarantees:
	- non-breaking changes preserve existing endpoint semantics
	- breaking changes require explicit major version bump and migration guidance

**D4. Generated Client Contract Plan (v1 Draft)**
1. Client profiles:
	- validity-only consumers
	- generated-runtime consumers
	- integration adapter consumers
2. Client capabilities:
	- typed request/response models
	- diagnostics-first error handling primitives
	- compatibility-aware version negotiation helpers
	- idempotency and retry helpers for control operations
3. Multi-language approach:
	- prioritize SDK generation for primary customer ecosystems
	- maintain shared wire-contract conformance tests across SDKs

**D5. AuthZ, AuthN, and Tenant Model (v1 Draft)**
1. Tenant boundary model:
	- logical tenant isolation with strict policy enforcement at all endpoints
2. Principal and role model:
	- admin, model-author, reviewer, runtime-client, integration-agent
3. Capability authorization:
	- least-privilege scopes mapped to service operations
	- explicit separation of read-only analysis vs mutation/governance operations
4. Token and credential policy:
	- short-lived tokens for interactive sessions
	- service credentials for automated clients
5. Audit policy:
	- all control-plane changes emit immutable audit events
	- audit records include actor, tenant, operation, target SemanticIds, and outcome

**D6. Idempotency and Retry Semantics (v1 Draft)**
1. Required idempotency keys for mutation-like operations (draft update, stage, publish, approve).
2. Replay-safe responses must return original outcome for duplicate key within retention window.
3. Idempotency scope includes tenant + operation type + target resource.
4. Retry guidance is explicit in client contracts for transient vs permanent failures.

**D7. Extension Ingestion Lifecycle (v1 Draft)**
1. Import phase: parse external format (OpenAPI, AsyncAPI, JSON Schema, gRPC/proto for v1).
2. Normalize phase: convert to ExtensionContract canonical ingestion shape.
3. Map phase: apply anti-corruption mapping to canonical domain concepts.
4. Validate phase: run structural/semantic checks and compatibility checks.
5. Publish phase: activate extension mapping with version metadata.
6. Observe phase: collect diagnostics and usage telemetry for lifecycle governance.

**D8. Service Diagnostics Taxonomy (v1 Draft)**
1. SVC-AUTH-* : authentication/authorization failures.
2. SVC-TENANT-* : tenant boundary and scope violations.
3. SVC-IDEMP-* : idempotency/replay contract failures.
4. SVC-COMPAT-* : compatibility and version policy failures.
5. SVC-EXT-* : extension ingestion/mapping failures.
6. SVC-RATE-* : quota/rate limiting and service protection outcomes.

**D9. Milestone D Review Checklist**
1. REST and MCP capability parity verified for shared semantics.
2. Validity API envelopes and response contracts reviewed with SDK stakeholders.
3. Auth/tenant model reviewed with security and platform operations stakeholders.
4. Idempotency contract validated for multi-tenant replay safety.
5. Extension ingestion lifecycle aligned with anti-corruption and compatibility policy.

**D10. Exit Criteria for Milestone D**
1. Capability catalog approved and mapped to ownership.
2. Validity API and MCP contracts approved with versioning policy.
3. Client generation contract approved for at least one primary SDK profile.
4. Auth/tenant/audit model approved and traceable to service diagnostics taxonomy.
5. Extension lifecycle design approved for v1 scope formats.

**Milestone E Design Worksheet (Implementation Planning Only)**

**E1. Runtime Architecture Baseline (v1 Draft)**
1. Runtime execution roles:
	- command execution coordinator
	- rule/constraint evaluation engine
	- lifecycle transition validator
	- mutation effect executor
	- event emission coordinator
2. Runtime profiles:
	- simulator profile (design-time and test-time)
	- hosted validity profile (request/response evaluation)
	- generated runtime profile (customer deployable artifacts)
3. Shared semantic core rule: all profiles must consume equivalent projected semantics and diagnostic code contracts.

**E2. Simulator Strategy Execution Plan (v1 Draft)**
1. Primary strategy: hybrid runtime (interpreter-first plus compiled hot paths).
2. Promotion process:
	- collect invocation and latency metrics
	- apply promotion threshold policy
	- verify semantic and diagnostic parity against interpreted baseline
3. Rollback process:
	- demote compiled path on parity mismatch or instability signal
	- preserve traceability for promotion/demotion decisions
4. Fallback implementation path:
	- expression-compiled delegate path with dictionary-backed shims if hybrid rollout risks schedule goals

**E3. Aggregate Routing and Consistency Model (v1 Draft)**
1. Routing contract:
	- command routes to aggregate root instance by explicit identity reference
	- unresolved or ambiguous routes fail deterministically with diagnostics
2. Consistency contract:
	- aggregate-local state transitions are atomic within runtime transaction boundary
	- cross-aggregate operations require explicit orchestration semantics
3. Concurrency model:
	- optimistic concurrency tokens on state-changing operations
	- deterministic conflict diagnostics and retry guidance

**E4. Lifecycle and Transition Runtime Rules (v1 Draft)**
1. Transition attempt requires:
	- valid current state
	- declared transition edge
	- passing transition guard conditions
2. Transition failure behavior:
	- no partial state commitment
	- diagnostics returned with transition semantic references
3. Compensating behavior:
	- compensating transitions are explicit and separately auditable

**E5. Event Routing and Delivery Semantics (v1 Draft)**
1. Emission ordering:
	- deterministic ordering by mutation plan order then SemanticId tie-break
2. Delivery contract profiles:
	- simulator: in-memory ordered emission log
	- hosted validity: optional event simulation traces only
	- generated runtime: transport-bound adapter contract
3. Event envelope minimum fields:
	- eventSemanticId
	- aggregate identity reference
	- causationId
	- correlationId
	- model/ruleset version references

**E6. Failure and Rollback Policy (v1 Draft)**
1. Pre-commit failures:
	- no state change persisted
	- full diagnostic envelope returned
2. Commit-time failures:
	- runtime-specific rollback policy required and documented
	- partial failure states must be detectable and diagnosable
3. Post-commit side effects:
	- adapter failures are isolated from canonical semantic outcomes where possible
	- compensation policy required for non-isolated integrations

**E7. Virtual Actor Target Mapping (v1 Draft)**
1. Mapping principles:
	- aggregate root maps to actor/grain identity boundary
	- command routing maps to actor method contract
	- lifecycle state maps to actor state snapshot
2. Actor compatibility invariants:
	- no semantic weakening of invariants/rules/transition guards
	- deterministic diagnostics parity with simulator profile
3. Actor-generation constraints:
	- generated actor surface remains domain-first, not transport/storage-first

**E8. Runtime Observability Contract (v1 Draft)**
1. Required trace events:
	- command intake
	- precondition evaluation summary
	- mutation application summary
	- transition attempt result
	- event emission summary
2. Metrics baseline:
	- command latency by profile
	- rule evaluation counts and failure rates
	- transition success/failure rates
	- promotion/demotion counts for hybrid simulator
3. Correlation requirements:
	- all events and diagnostics linked by correlationId and causationId where applicable

**E9. Milestone E Review Checklist**
1. Runtime profile equivalence validated for semantic and diagnostic parity.
2. Hybrid simulator promotion/demotion policy reviewed against operability goals.
3. Routing/concurrency/conflict contracts reviewed with platform and SDK stakeholders.
4. Failure/rollback policy reviewed for each runtime profile.
5. Virtual actor mapping reviewed for invariant preservation.

**E10. Exit Criteria for Milestone E**
1. Runtime architecture and profile contracts approved.
2. Simulator strategy ADR approved with benchmark and parity evidence.
3. Concurrency/conflict and rollback policies approved and testable.
4. Event routing/delivery semantics approved for all runtime profiles.
5. Virtual actor mapping spec approved for optional build target.

**Milestone F Design Worksheet (Implementation Planning Only)**

**F1. Self-Hosting Maturity Ladder (v1 Draft)**
1. Level 0: platform modeled externally, no self-hosted governance.
2. Level 1: platform model registry represented in canonical model.
3. Level 2: draft/stage/publish governance workflow self-modeled and enforced.
4. Level 3: platform validity API contracts self-described and validated by own model.
5. Level 4: at least one platform subsystem generated/enforced from self-model with parity checks.
6. Level 5: recurring platform evolution flows through self-hosted model and governance pipeline.

**F2. First Self-Hosted Subsystem Plan (v1 Draft)**
1. Target subsystem: model registry plus governance lifecycle.
2. Scope boundary:
	- canonical contracts for drafts/staged/published states
	- approval policy rules
	- publish compatibility gates
3. Evidence requirements:
	- platform uses own validity and compatibility checks for its model updates
	- governance operations emit expected audit and diagnostics outputs

**F3. Release Readiness Gate Model (v1 Draft)**
1. Gate A: architecture/documentation completeness (A-F worksheets approved).
2. Gate B: semantic parity evidence (simulator, hosted validity, generated runtime).
3. Gate C: security and tenant isolation verification.
4. Gate D: compatibility and versioning policy verification.
5. Gate E: operability and observability readiness.
6. Gate F: self-hosting milestone evidence for targeted level.

**F4. Operability Checklist (v1 Draft)**
1. Runbook coverage for control-plane and data-plane operations.
2. Incident response flows for auth, tenant isolation, and compatibility failures.
3. Backfill/replay procedures for diagnostics and trace pipelines.
4. Version rollback and deprecation procedures.
5. Capacity planning and scaling playbooks for high-evaluation workloads.

**F5. Adoption Path Definitions (v1 Draft)**
1. Path A: generated-runtime adoption.
	- customer consumes generated runtime artifacts and optional actor target.
2. Path B: validity-only adoption.
	- customer keeps own runtime and consumes hosted validity plus generated clients.
3. Path C: hybrid adoption.
	- customer uses generated projections/adapters with selective runtime ownership.
4. For each path define:
	- required contracts
	- compatibility promises
	- migration expectations

**F6. Governance and Change Control (v1 Draft)**
1. Change proposal classification:
	- additive
	- soft-breaking
	- breaking
	- contract-breaking
2. Required approvals by class and deployment profile.
3. Mandatory publish artifacts:
	- compatibility report
	- diagnostics impact summary
	- client impact notes

**F7. Milestone F Review Checklist**
1. Self-hosting maturity ladder reviewed and agreed by platform leadership.
2. First self-hosted subsystem scope approved with evidence plan.
3. Release gates mapped to objective artifacts and accountable owners.
4. Operability checklist validated by engineering and operations.
5. Adoption paths validated with customer-facing and SDK stakeholders.

**F8. Exit Criteria for Milestone F**
1. Self-hosting level target for initial release is approved.
2. Release readiness gate artifacts are defined, owned, and scheduled.
3. Adoption path documentation is complete for generated-runtime and validity-only customers.
4. Governance/change-control model is approved and linked to publish workflow.
5. Planning package A-F is complete and implementation kickoff is authorized.

**Proposed Package and Boundary Plan**
1. Poly/DomainModeling/V2/Core: immutable canonical contracts and ID/value types.
2. Poly/DomainModeling/V2/Construction: RAII-safe draft/build session APIs and compile steps.
3. Poly/DomainModeling/V2/Validation: invariant model, diagnostics, explainability, and compatibility logic.
4. Poly/DomainModeling/V2/Projection: canonical-to-interpretation, canonical-to-read-model, canonical-to-persistence, and canonical-to-integration projections.
5. Poly/DomainModeling/V2/Runtime: simulator contracts, command execution model, event flow, and aggregate routing abstractions.
6. Poly/DomainModeling/V2/Visual: Blueprint graph contracts, sidecar schema, graph deltas, and visual mapping rules.
7. Poly/DomainModeling/V2/Serving: MCP contracts, validity-only service contracts, generated client descriptors, and tenant/auth models.
8. Poly/DomainModeling/V2/Extensions: extension ingestion pipeline, normalized external contract models, and adapter-generation contracts.
9. Poly/DomainModeling/V2/SelfHosting: platform self-model definitions, self-hosting checkpoints, and conformance scenarios.

**Early Design Questions To Resolve Before Coding**
1. What is the exact canonical representation for aggregate boundaries: explicit node type, inferred metadata, or both? RESOLVED: both. Canonical contracts support explicit aggregate declarations, while deterministic inference emits derived aggregate metadata with required explainability and explicit override rules.
2. Which parts of the lifecycle/rule engine are guaranteed portable across simulator, hosted validity API, and generated customer runtimes? RESOLVED: constraint evaluation, cross-property rule evaluation, command precondition evaluation, lifecycle transition validity checks, and diagnostic code/category semantics are portable and contract-bound.
3. What constitutes a non-breaking rule change for validity-only consumers? RESOLVED: additive metadata, diagnostic text improvements, and additions that do not invalidate previously valid payloads. Breaking changes include stricter acceptance criteria, newly required fields, and rule behavior shifts that change pass/fail outcomes.
4. How are remote diagnostics represented so generated clients can present them without platform-specific coupling? RESOLVED: structured diagnostics with stable machine codes, severity/category, path/member references, and optional localized message templates; human-readable text is supplemental.
5. Which extension formats are in scope for v1 of interoperability beyond OpenAPI and MCP? RESOLVED: AsyncAPI, JSON Schema, and gRPC/proto are in-scope for v1. GraphQL SDL is scheduled for v1.1 post-launch.
6. What is the first self-hosted subsystem that provides strong evidence without creating circular build dependence too early? RESOLVED: the platform model registry and governance workflow (draft, staged, published) is first self-hosted target.

**Resolved Design Decisions (Round 1)**
1. Portability expectation: simulator semantics must remain equivalent to hosted validity evaluation semantics.
2. Runtime type abstraction: simulator instances may use dictionary-backed shims during bootstrap, but instance representation is an implementation detail behind stable semantic contracts.
3. Evolution path: the simulator engine can change over time without contract drift if semantic behavior, diagnostics, and compatibility rules remain stable.

**Resolved Design Decisions (Round 2)**
1. Aggregate boundary representation: hybrid model. Explicit aggregates are canonical; inference is deterministic and produces explainable derived metadata.
2. Portable execution contract: rule/lifecycle/precondition evaluation semantics and diagnostic code contracts are identical across simulator, hosted validity API, and generated runtimes.
3. Validity compatibility policy: non-breaking changes preserve previous acceptance outcomes; breaking changes require explicit version increment and migration notice.
4. Diagnostic wire contract: generated clients consume machine-readable diagnostics first, with localized text as optional presentation.
5. Interoperability v1 scope: OpenAPI, MCP adapters, AsyncAPI, JSON Schema, and gRPC/proto.
6. Self-hosting start point: model registry plus governance lifecycle is the first subsystem required to self-host.

**Resolved Design Decisions (Round 3)**
1. Default simulator strategy for v1: Candidate D hybrid mode. Use interpreter-first execution for explainability, deterministic diagnostics, and easier semantic debugging; add compiled delegate acceleration for validated hot paths.
2. Hot-path promotion rule: only promote interpreted paths to compiled delegates when benchmark evidence shows meaningful improvement without diagnostic or semantic drift.
3. Fallback strategy: if hybrid complexity creates unacceptable delivery risk in early milestones, start with Candidate A (expression-compiled delegates with dictionary-backed shims) and preserve interpreter parity tests as a planned follow-up.
4. Benchmark baseline: all simulator candidates are compared against shared correctness fixtures before performance comparison to guarantee fidelity-first selection.

**Resolved Design Decisions (Round 4)**
1. gRPC/proto ingestion: included in v1 scope.
2. GraphQL SDL ingestion: targeted for v1.1 post-launch.
3. Deferred-priority trigger: partner roadmap dependency is the primary trigger for promoting deferred interoperability formats.

**Resolved Design Decisions (Round 5)**
1. SemanticId format: ULID string as canonical semantic identifier.
2. Versioning model: dual versioning with Model SemVer plus independent RuleSet version.
3. Compatibility enforcement: hard-block publish of breaking rule changes unless accompanied by required major version increment.
4. Validity API style: both REST/JSON plus OpenAPI and MCP are first-class in v1.
5. Tenant isolation baseline: logical tenant isolation with strict policy boundaries in v1.
6. Diagnostic namespace strategy: diagnostic codes derive from rule/constraint SemanticIds.

**Resolved Design Decisions (Round 6)**
1. Simulator hot-path promotion criteria: hybrid threshold using minimum invocation count plus measurable latency delta and CPU/throughput gain before compiled promotion.
2. GraphQL SDL v1.1 inclusion criteria: import-attempt telemetry threshold, revenue-impact threshold, and fixed post-v1 timeline commitment.
3. Publish/stage/approve idempotency contract: required idempotency key with replay-safe semantics for all mutation-like multi-tenant control-plane operations.

**Resolved Design Decisions (Round 7)**
1. Test strategy: three-tier — unit (isolated logic), integration (service boundary), conformance (REST/MCP parity and V1/V2 semantic equivalence). Every task produces tests at the appropriate tier; tier selection is driven by what the task touches.
2. CI/CD gate: build + unit + integration tests run automatically before merge; no manual-only gates.
3. V1→V2 migration: hard cutover — V2 builds alongside V1 in the same repo; V1 is deleted in one commit once V2 passes all acceptance criteria. A dedicated migration task captures the removal scope and fallback test parity evidence.
4. SLO definition timing: performance budgets are defined after T42 (evaluate endpoint), baselined against a real measured implementation rather than guesses; a dedicated task (T45) captures this work.
5. Deprecation and sunset policy scope: one unified policy covers both API contracts (REST/MCP endpoint versioning, announcement grace period, and removal gates) and model contracts (published domain model version aging and forced migration triggers).
6. Self-hosting tasks: T-tasks created for L0–L2 only (model registry self-describes its own schema and governance lifecycle); L3–L5 deferred until the rest of the system ships.

**Resolved Design Decisions (Round 8)**
1. SDK targets: C#, TypeScript, WASM, and Zig are all planned targets. C# is first (the runtime is .NET; zero translation layer). TypeScript second (widest web reach for validity-only customers). WASM and Zig are tracked future targets with no committed timeline; they require the OpenAPI contract to be stable before generation is attempted. SDK expansion follows the same trigger policy as extension formats: telemetry + identified customer + fixed timeline before adding any new target.
2. Deployment model: pre-built container images distributed via per-tenant container registries. The customer pulls and runs the image; the platform publishes images per tenant (or per tier/region). The underlying service framework is ASP.NET Core containerised; the container image is the deployment unit. T05 gains an additional container-image build and registry push step.
3. Authentication mechanism: deferred to after T03 defines the role matrix; decision on JWT Bearer vs. API key vs. both is made before T50 starts.
4. Idempotency store: deferred to when T52 starts; decision (in-memory, Redis, or DB-backed) is made when the deployment topology is confirmed.
5. Domain event routing: both — internal in-process fan-out is the v1 baseline; an optional external relay adapter is the v1.1 add-on, gated by the same trigger policy as other v1.1 scope items. No hard dependency on a message broker in v1.
6. Branch strategy for parallel agent work: one short-lived feature branch per task, merged via PR. CI must pass before merge. Agents branch from main, work in their listed files only, and open a PR when their DoD is met.

**Resolved Design Decisions (Round 9)**
1. SemanticId generation: hybrid — system auto-generates a ULID by default; caller may supply a custom ID at creation time only and the system validates its format. IDs are immutable after creation and survive renames.
2. Lifecycle FSM vs property state: lifecycle is canonical. If a DomainType has a LifecycleModel, any status-like property is a derived read projection of the current FSM state, not an independent writable field. Invariants may not contradict the lifecycle state.
3. Multi-tenant publish scope: tenant-private by default — a published model is visible only within the publishing tenant. The owner may explicitly grant read access to specific other tenants. No global visibility without an explicit grant.
4. Approval workflow: configurable per tenant — tenant policy controls whether publish is self-approved (synchronous, author role) or requires a designated reviewer (synchronous, reviewer role) or is asynchronous (pending approval record). Default policy for new tenants is self-approve. T46 must read tenant policy before executing the publish transition.
5. Authentication: dual — API key (header-based) for M2M callers; JWT Bearer (OIDC/OAuth2) for interactive/human callers. Both produce the same tenant+role scope context for downstream authorization checks.
6. Idempotency store: in-memory, interface-backed — default implementation is in-process; the interface (IIdempotencyStore) is the swap point for Redis/Garnet in production. No hard Redis dependency in v1. T52 implements the interface and the in-memory default.
7. Conformance test tasks: T53 (cross-tenant access breakout), T54 (idempotency replay edge cases), T55 (diagnostic-code parity across evaluation profiles) added as explicit tasks in Stage 5.

**Still Open After Round 9**
1. None currently.

**Still Open After Round 8**
1. Authentication mechanism — deferred to post-T03 (role matrix must be available before mechanism is chosen).
2. Idempotency store — deferred to T52 kickoff (deployment topology must be confirmed first).
3. Multi-tenant publish scope — is a published model visible to one tenant only or all tenants? Affects versioning contract, compatibility enforcement, and client update semantics. Must be decided before T40.
4. SemanticId generation strategy — user-provided, auto-generated (ULID), or name-derived? Collision and rename-safety implications. Must be decided before T11.
5. Approval workflow sync/async — is publish-stage approval synchronous (inline) or asynchronous (separate step)? Who can approve? Must be decided before T46 (governance endpoints).
6. Lifecycle FSM vs property state reconciliation — if a DomainType has both a LifecycleModel and a status property, are they synchronized by invariant, is lifecycle canonical, or are they independent? Must be decided before T16/T18.
7. MCP write surface boundary — plan states MCP is for read/query/discovery (Non-Goal #10); this must be made explicit in T44's acceptance criteria. No MCP operation may mutate model state outside the validated construction and governance pipeline.

**Still Open After Round 7**
1. None currently.

**Still Open After Round 2**
1. None currently; deferred interoperability work is now explicitly scheduled and trigger-governed.

**Still Open After Round 5**
1. None currently; quantitative thresholds and rollout values are implementation-tuning inputs, not unresolved architecture decisions.

**Simulator Strategy Decision Gate (Before Production Implementation)**
1. Candidate A: expression-compiled delegates via System.Linq.Expressions with dictionary-backed shims.
2. Candidate B: direct interpreter over projected node/expression graph with optional memoization.
3. Candidate C: source-generated temporary runtime types plus compiled delegates.
4. Candidate D: hybrid mode (interpreter for analysis/explainability, compiled delegates for hot paths).
5. Selection criteria: semantic fidelity, explainability quality, cold-start latency, steady-state throughput, memory profile, debugging ergonomics, and compatibility stability.
6. Decision output: publish an ADR-style simulator strategy record with measured benchmark results, trade-offs, and rollback plan.

**Recommended Planning Sequence**
1. Finalize the canonical glossary and success criteria.
2. Write the invariant matrix for every public concept.
3. Define the diagnostic contract before defining execution APIs.
4. Define the validity-only API and generated client contract before defining hosted implementation details.
5. Define aggregate inference heuristics and manual override semantics before runtime target mapping.
6. Define projection schemas before any code generation strategy.
7. Define self-hosting milestones before claiming architectural completeness.

**Relevant files**
- [Poly/DomainModeling/DataModel.cs](Poly/DomainModeling/DataModel.cs) - replace legacy root model contract
- [Poly/DomainModeling/DataType.cs](Poly/DomainModeling/DataType.cs) - replace type contract with strict invariants
- [Poly/DomainModeling/DataProperty.cs](Poly/DomainModeling/DataProperty.cs) - replace property contract and invariant enforcement
- [Poly/DomainModeling/Relationship.cs](Poly/DomainModeling/Relationship.cs) - replace relationship hierarchy and endpoint/cardinality invariants
- [Poly/DomainModeling/Lifecycle.cs](Poly/DomainModeling/Lifecycle.cs) - replace FSM contracts and transition invariants
- [Poly/DomainModeling/DataTypeBuilder.cs](Poly/DomainModeling/DataTypeBuilder.cs) - repurpose/replace for draft-to-immutable RAII sessions
- [Poly/DomainModeling/Builders/MutationBuilder.cs](Poly/DomainModeling/Builders/MutationBuilder.cs) - replace behavior builder so invalid mutation definitions cannot be emitted
- [Poly/DomainModeling/Mutations/Mutation.cs](Poly/DomainModeling/Mutations/Mutation.cs) - redefine command/mutation semantics
- [Poly/DomainModeling/Events/DomainEvent.cs](Poly/DomainModeling/Events/DomainEvent.cs) - redefine event contracts and behavior linkage
- [Poly/DomainModeling/DataModelAstExtensions.cs](Poly/DomainModeling/DataModelAstExtensions.cs) - retire legacy conversion and replace with v2 projection path
- [Poly/DomainModeling/DataModelPropertyPolymorphicJsonTypeResolver.cs](Poly/DomainModeling/DataModelPropertyPolymorphicJsonTypeResolver.cs) - replace serialization mapping for v2 schema
- [Poly/Interpretation/AbstractSyntaxTree/Node.cs](Poly/Interpretation/AbstractSyntaxTree/Node.cs) - reuse stable ID pattern for model/projection/visual sidecar traceability
- [Poly/Interpretation/NodeMetadataStore.cs](Poly/Interpretation/NodeMetadataStore.cs) - reuse metadata pattern for graph diagnostics and projection metadata
- [Poly/Interpretation/Analysis/AnalyzerBuilder.cs](Poly/Interpretation/Analysis/AnalyzerBuilder.cs) - integrate v2 analysis/projection validation passes
- [Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs](Poly/Interpretation/LinqExpressions/LinqExpressionGenerator.cs) - adapt for v2 projected semantics execution
- [Poly.Tests/DomainModeling](Poly.Tests/DomainModeling) - replace and expand invariant/semantics tests
- [Poly.Tests/Integration](Poly.Tests/Integration) - add MCP and simulator end-to-end tests

**Verification**
1. Construction invariants: every public model object rejects invalid constructor/factory inputs and only returns valid instances.
2. Semantic completeness: at least one end-to-end domain exercises all required capabilities (types/properties/constraints/rules/relationships/FSM/commands/mutations/events).
3. Projection correctness: v2 projector emits interpretation artifacts for structural plus behavioral semantics and preserves stable IDs.
4. Simulator correctness: deterministic command sequences produce expected state transitions, mutation side effects, and emitted events.
5. Blueprint round-trip: serialize model plus visual sidecar, reload, apply graph edits, rebuild model, and verify semantic equivalence.
6. MCP contract validation: resource/tool responses support agent context usage and include actionable diagnostics.
7. Build and tests: full workspace build plus full test suite including new snapshots and integration checks.
8. Security and audit conformance: verify authorization boundaries, immutable audit records, and rejection behavior for malformed/untrusted MCP requests.
9. Multi-tenant isolation: verify tenant scoping for models, simulations, and sidecar data with negative tests for cross-tenant access.
10. Performance gates: verify p95/p99 latency and throughput targets under representative model size and concurrent request profiles.
11. Explainability quality: verify rule and transition diagnostics are machine-readable and human-readable for business analyst workflows.
12. Aggregate inference correctness: verify inferred aggregate roots and boundaries are stable, explainable, and match expected transactional consistency behavior for representative domains.
13. Virtual actor target validation: verify generated actor/grain boundaries preserve command routing, state isolation, idempotency expectations, and event ordering semantics.
14. Boundary enforcement: verify persistence, transport, MCP, and read-model artifacts are projections and cannot be used as substitutes for canonical domain model types within core runtime APIs.
15. Generated artifact quality: verify customer-facing generated code exposes meaningful business operations and aggregate-oriented contracts instead of storage-shaped CRUD DTO surfaces.
16. Anti-pattern prevention: verify banned failure modes are enforced through tests, analyzers, or build-time diagnostics rather than documentation alone.
17. Self-hosting proof: verify the platform can describe and validate its own core modeling concepts, generation pipeline, and governance workflow using its canonical model.
18. Self-implementation progression: verify at least one meaningful subsystem of the platform is generated or enforced from its self-hosted model without semantic drift from the handwritten source of truth.
19. Extension interoperability: verify standard format imports such as OpenAPI and MCP-backed integrations can be mapped into the extension model with deterministic diagnostics and no leakage into canonical semantics.
20. Adapter generation quality: verify generated integration adapters preserve boundary separation, versioning expectations, and error semantics for imported APIs.
21. Validity-only consumer support: verify customer-owned applications can consume served type and rule metadata plus evaluation endpoints through generated clients without embedding the full domain runtime.
22. Rule-change delivery: verify compatible constraint/rule changes can be published to the hosted validity surface and take effect for generated clients without requiring customer application rebuilds.

**Decisions Captured**
- Full DomainModeling replacement is allowed.
- RAII-valid construction is mandatory.
- v2 schema is approved.
- Visual metadata must be separate sidecar persistence.
- Simulator runtime is in scope now.
- Hard cutover is approved, with no migration utility.
- The architecture should support programmatic aggregate-root inference with explicit override capability.
- Build artifacts should be designed to optionally target a virtual actor runtime model similar to Orleans.
- The implementation and generated artifacts should preserve a domain-first style and must not treat persistence entities as the primary DTO or programming model.
- The ultimate architectural success criterion is self-hosting: the platform must be able to model and progressively implement itself using the same domain modeling system it provides to customers.
- The platform must support extensions and integrations via standard interface formats such as OpenAPI and MCP adapters through explicit mapping layers, not bespoke canonicalization shortcuts.
- The platform must support a validity-only delivery mode where customers consume hosted type/constraint/rule semantics through generated clients while retaining ownership of their own application runtime.

The complete plan has been persisted in session memory and is ready for handoff when you approve.
