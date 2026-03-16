# Solo Builder Multi-Agent Task Board

Goal: Let a one-person team run many small agents in parallel with minimal coordination.

Rules:
1. Each task should be 1 to 3 hours.
2. Each task produces one concrete artifact.
3. Agent works only inside listed files/folders.
4. Agent does not expand scope.
5. Agent must include tests or validation notes.
6. If blocked, agent writes blocker and stops.
7. Each task runs on its own short-lived feature branch, opened as a PR. CI (build + unit + integration) must pass before merge.
8. During v2 rewrite, keep all new implementation inside the existing `Poly` project. Do not introduce new .csproj libraries until post-v1 extraction.

Definition of Done for every task:
1. Changes compile.
2. Tests pass at the appropriate tier: unit for isolated logic, integration for service-boundary tasks, conformance for any REST/MCP or V1/V2 parity task.
3. One short note explains what changed and why.
4. No unrelated refactors.

## Stage 0: Setup and Guardrails (Do First)

### T00 - Create architecture decision log
- Depends on: none
- Owner lane: Core
- Output: docs/adr/README.md and first ADR template
- Agent prompt:
Create a minimal ADR system under docs/adr with README and template. Keep it simple and consistent. No code changes outside docs.

### T01 - Create diagnostics code convention doc
- Depends on: none
- Owner lane: Core
- Output: docs/diagnostics/codes.md
- Agent prompt:
Create diagnostics code naming conventions aligned with semantic identifiers and categories. Include examples for Structural, Semantic, Compatibility, Authorization, Runtime.

### T02 - Create compatibility policy doc
- Depends on: none
- Owner lane: Core
- Output: docs/versioning/compatibility-policy.md
- Agent prompt:
Create a simple compatibility policy document defining additive, non-breaking, soft-breaking, breaking, contract-breaking, and publish gate behavior.

### T03 - Create tenant and auth scope matrix
- Depends on: none
- Owner lane: Serving
- Output: docs/security/tenant-auth-matrix.md
- Agent prompt:
Create a matrix of roles vs operations for model read/write, validation, stage, publish, extension import. Keep it concise and enforceable.

### T04 - Create test strategy document
- Depends on: none
- Owner lane: Core
- Output: docs/testing/test-strategy.md
- Agent prompt:
Document the three-tier test strategy: unit (isolated logic with no I/O), integration (service boundary — HTTP, evaluator pipeline, publish gate), conformance (REST/MCP parity and V1/V2 semantic equivalence). Include naming conventions, what gets mocked, and minimum expectations per tier. No code changes.

### T05 - Set up CI/CD pipeline
- Depends on: T04
- Owner lane: Ops
- Output: .github/workflows/ci.yml running build + unit + integration tests on every PR; plus a container-image build step that produces a tagged image artifact
- Agent prompt:
Create a CI pipeline that runs dotnet build and dotnet test (unit and integration projects) on every pull request and fails on test failure. Also add a container image build step (docker build or dotnet publish --os linux --arch x64) that produces a tagged image artifact. No registry push in CI yet — that is a release step. No deployment steps beyond the image build.

### T06 - Create deprecation and sunset policy
- Depends on: T02
- Owner lane: Core
- Output: docs/versioning/deprecation-policy.md
- Agent prompt:
Write a unified deprecation policy covering: (1) API contracts — how REST/MCP endpoints are versioned, marked deprecated, announced, grace period length, and hard-removal gates; (2) model contracts — how published domain model versions age out, forced migration triggers, and sunset communication. Keep it concise and machine-referenceable.

### T07 - Document V1 to V2 migration scope and hard-cutover plan
- Depends on: T10
- Owner lane: Core
- Output: docs/migration/v1-to-v2-cutover.md
- Agent prompt:
Inventory all existing Poly/DomainModeling V1 types, builders, and extension points. For each, map it to either a V2 replacement, a documented removal with rationale, or a deferred item. Define the single-commit hard-cutover checklist: tests that must pass, files to delete, and any consumer call-sites to update. No code changes — documentation only.

### T09 - Vertical slice spike: one type, one rule, one diagnostic
- Depends on: T10
- Owner lane: Core
- Output: Poly/DomainModeling/V2/Spike/ — a single self-contained working scenario with no abstraction overhead, plus a short written observation doc
- Agent prompt:
Write the absolute minimum code needed — inline, no framework, no abstraction — to prove this end-to-end scenario works: define one type with one property, attach one constraint (e.g. required/non-empty string), evaluate an instance that violates it, and return a structured diagnostic with a code and message. Everything lives in a single file under Poly/DomainModeling/V2/Spike/. There is no `SemanticId` type, no `DomainModel` hierarchy, no `ITypeDefinition` interface — use plain classes or records with no base types. The goal is to learn what the system actually needs, not to design what it should need. Once it works, write a short doc (Spike/OBSERVATIONS.md) listing: what types or concepts naturally emerged, what was painful or awkward, and what you would extract into a reusable abstraction if building this a second time. That doc becomes the specification for T11–T18. Do not clean up the spike code — leave it raw; it is an artefact of observation, not production code.

### T10 - Scaffold V2 folder boundaries
- Depends on: T00
- Owner lane: Core
- Output: Poly/DomainModeling/V2 folder layout with placeholder files and comments
- Agent prompt:
Create the V2 folder structure only: Core, Construction, Validation, Projection, Runtime, Visual, Serving, Extensions, SelfHosting, Spike. Add minimal placeholder files describing purpose.

### T11 - Define SemanticId type and validators
- Depends on: T09
- Owner lane: Core
- Output: SemanticId value type + tests
- Agent prompt:
Read Poly/DomainModeling/V2/Spike/OBSERVATIONS.md first. Implement SemanticId as an immutable value type with the following behaviour: (1) default creation auto-generates a ULID string; (2) an optional constructor overload accepts a caller-supplied string, validates it is non-null, non-empty, and matches a defined format (printable ASCII, max 128 chars, no whitespace), and rejects invalid values with a clear exception at construction time; (3) once created, the value is immutable — no setter, no mutation method; (4) equality is value-based; (5) ToString() returns the raw string. Add tests for: auto-generation produces unique non-empty values, valid custom ID accepted, invalid custom ID rejected at construction, equality and hash consistency.

### T12 - Define ModelVersion and RuleSetVersion types
- Depends on: T09
- Owner lane: Core
- Output: version value types + tests
- Agent prompt:
Read Poly/DomainModeling/V2/Spike/OBSERVATIONS.md first. Implement two immutable value types: ModelVersion (SemVer: Major.Minor.Patch, all non-negative integers) and RuleSetVersion (same SemVer scheme, independently versioned from ModelVersion). Both must: parse from string ("1.2.3"), reject invalid formats at construction, support equality and comparison (1.2.0 < 1.3.0 < 2.0.0), and produce a canonical string via ToString(). Add unit tests for valid/invalid parsing, ordering, and equality.

### T13 - Define core contract: DomainModel
- Depends on: T11, T12
- Owner lane: Core
- Output: immutable DomainModel contract + invariant checks + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md. Create an immutable DomainModel record/class with these required fields: SemanticId (non-null), Name (non-null, non-empty string), ModelVersion, RuleSetVersion, LifecycleModelId (SemanticId?, nullable — the governance lifecycle model that governs this DomainModel record's own publication workflow), and a read-only collection of BoundedContextIds (SemanticIds of member BoundedContexts, may be empty at construction). Invariants enforced at construction: SemanticId and Name non-null, ModelVersion and RuleSetVersion non-null, Name must not contain whitespace-only. No setters. Factory method or constructor throws on violation.
IMPORTANT — navigation note: DomainModel is a header/envelope record. Navigation from a DomainModel to its DomainTypes, Aggregates, Commands, Mutations, DomainEvents, and LifecycleModels is NOT through DomainModel's own fields. That graph lives in an external model-graph registry (interface to be defined before T70). Document this in a class-level XML summary comment: "DomainModel is an envelope. Full graph traversal requires an IModelGraphRegistry (see T70)." Do not add collection fields for types or aggregates to DomainModel.
Add unit tests for: valid construction, each invariant violation, LifecycleModelId is nullable and accepted as null.

### T14 - Define core contract: BoundedContext
- Depends on: T11
- Owner lane: Core
- Output: immutable BoundedContext contract + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md. Create an immutable BoundedContext with required fields: SemanticId (non-null), Name (non-null, non-empty), and an optional Description string. Invariants: SemanticId and Name non-null, Name non-whitespace. No setters. Add tests for valid construction and each violation.

### T15 - Define core contract: Aggregate + AggregateRoot reference
- Depends on: T11
- Owner lane: Core
- Output: aggregate contracts + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md. Create two immutable types:
(1) Aggregate with required fields: SemanticId, Name, BoundedContextId (SemanticId reference), AggregateRootTypeId (SemanticId — the explicit SemanticId of the DomainType that is the aggregate root), and a read-only collection of DomainTypeIds (SemanticIds of all participating DomainTypes, including the root). Invariants: must have at least one DomainTypeId; AggregateRootTypeId must appear in the DomainTypeIds collection. Do NOT use positional ordering to identify the root — the explicit AggregateRootTypeId field is the only mechanism.
(2) AggregateRoot is a thin reference type carrying SemanticId (equal to AggregateRootTypeId of the owning Aggregate) and Name, for use in projection contexts only — it is not independently stored, it is a projection of the Aggregate's root reference.
No setters on either. Add tests for: valid construction, the one-type-minimum invariant, the root-must-be-in-collection invariant, and construction failure when AggregateRootTypeId is not present in DomainTypeIds.

### T16 - Define core contract: DomainType and DomainProperty
- Depends on: T11, T19
- Owner lane: Core
- Output: type/property contracts + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md and docs/core/type-expression-vocabulary.md (T19 output). Create immutable DomainType with required fields: SemanticId, Name, BoundedContextId (SemanticId reference), a read-only collection of DomainProperty, and an optional LifecycleModelId (SemanticId reference, nullable).
DomainProperty requires: SemanticId, Name (unique within its owning DomainType), TypeExpression (non-null string, must pass TypeExpression.TryParse validation from T19), IsRequired (bool), IsReadOnly (bool, default false), IsDerivedFromLifecycle (bool, default false).
Invariants enforced at DomainType construction:
- SemanticId and Name non-null on both DomainType and DomainProperty.
- DomainProperty names must be unique within the provided property collection.
- Any DomainProperty with IsDerivedFromLifecycle=true must also have IsReadOnly=true. Enforce this as a consistency invariant at DomainType construction (regardless of LifecycleModelId presence).
- If LifecycleModelId is set, any property whose Name matches "status", "state", or "phase" (case-insensitive) must have IsDerivedFromLifecycle=true and IsReadOnly=true.
IMPORTANT — uniqueness of DomainType.Name within a BoundedContext is a multi-object invariant. It cannot and must not be enforced in DomainType's own constructor (the constructor does not see sibling types). Document in a class-level XML summary: "DomainType.Name uniqueness within its BoundedContext is enforced by the model-graph builder, not at construction time."
Add tests for: valid type, duplicate property names rejected, IsDerivedFromLifecycle=true + IsReadOnly=false rejected, LifecycleModelId set + status-named property without IsDerivedFromLifecycle=true rejected, TypeExpression validation failure rejected.

### T17 - Define core contracts: Command, Mutation, DomainEvent
- Depends on: T11, T19
- Owner lane: Core
- Output: behavior contracts + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md and docs/core/type-expression-vocabulary.md (T19 output). Create four immutable contracts:
(1) ParameterDefinition: SemanticId, Name (non-null, non-empty), TypeExpression (non-null string, validated via TypeExpression.TryParse from T19), IsOptional (bool, default false). This is an INLINE definition on Command and DomainEvent — it is NOT a reference to a DomainProperty. Commands own their own input schema independently from stored domain state.
(2) Command: SemanticId, Name, TargetTypeId (SemanticId of the DomainType it acts on), InitiatedBy (string?, nullable — the actor identity populated from evaluation context at invocation time, null at definition time), a read-only collection of ParameterDefinition (may be empty), and an optional PreconditionExpression (nullable string).
(3) Mutation: SemanticId, Name, SourceCommandId (SemanticId), TargetTypeId, and a read-only collection of PropertyEffect (PropertyId: SemanticId of a target DomainProperty + EffectKind enum: Set/Clear/Append/Remove). IMPORTANT — a model-level validator (not this constructor) must ensure no PropertyEffect targets a DomainProperty with IsReadOnly=true or IsDerivedFromLifecycle=true. Document this in the class-level XML summary: "Cross-property lifecycle invariant is enforced by the model-graph validator, not at Mutation construction time."
(4) DomainEvent: SemanticId, Name, SourceMutationId (SemanticId), and a read-only collection of payload ParameterDefinition (inline definitions, not DomainProperty references).
Invariants on all four: SemanticId and Name non-null, non-empty. SourceCommandId on Mutation and SourceMutationId on DomainEvent non-null (the causal chain must be traceable). Add tests for valid construction and each invariant violation.

### T18 - Define core contracts: LifecycleModel, State, Transition
- Depends on: T11
- Owner lane: Core
- Output: lifecycle contracts + tests
- Agent prompt:
Read Spike/OBSERVATIONS.md. Create three immutable contracts:
(1) LifecycleState: SemanticId, Name, IsInitial (bool), IsTerminal (bool). Invariants: SemanticId and Name non-null; IsInitial and IsTerminal must NOT both be true on the same state (mutually exclusive — a state cannot simultaneously be the entry point and a terminal sink).
(2) Transition: SemanticId, FromStateId (SemanticId), ToStateId (SemanticId), TriggerName (non-null string — matched against Command.Name at evaluation time), optional TriggerCommandId (SemanticId?, nullable — if set, provides a type-safe SemanticId link to the triggering Command; model-level validator should confirm the referenced Command's Name matches TriggerName), IsExternallyApproved (bool, default false — if true, state advancement is deferred: an approval record is created and state does not advance until an external approval signal resolves it; see T47/T48), optional GuardExpression (nullable string).
(3) LifecycleModel: SemanticId, Name, a read-only collection of LifecycleState, and a read-only collection of Transition. Invariants: exactly one State must have IsInitial=true; all Transition FromStateId/ToStateId values must reference a LifecycleState SemanticId within the same collection; Name non-null. No setters.
Command-to-Transition linkage protocol: document in a class-level XML summary on LifecycleModel: "When the evaluation pipeline processes a Command invocation against a DomainType with a linked LifecycleModel, it attempts to fire the Transition whose TriggerName equals the Command.Name (case-sensitive). If the matched Transition has IsExternallyApproved=true, the pipeline emits a pending-approval record instead of advancing state immediately."
Add tests for: valid model, missing initial state, multiple initial states, dangling transition reference, IsInitial=true AND IsTerminal=true on same state rejected, IsExternallyApproved=true transition valid construction.

### T19 - Define TypeExpression vocabulary and parsing helper
- Depends on: T11
- Owner lane: Core
- Output: docs/core/type-expression-vocabulary.md + TypeExpression parsing helper + tests
- Agent prompt:
Before T33 (constraint evaluator) and T70 (interpretation projection) can be implemented, all tasks that handle DomainProperty.TypeExpression must agree on legal values. Define the canonical TypeExpression vocabulary and a lightweight parsing helper. This task is a blocker for T16, T17, T33, T34, T70, T71, T72, T73.
Vocabulary (v1 only — do not add more):
- Primitive scalars: "string", "int", "long", "decimal", "bool", "date", "datetime", "guid"
- Nullable variants: append "?" (e.g. "string?", "int?")
- Type references: "{BoundedContextName}.{TypeName}" (e.g. "Billing.Invoice") — dot-separated, no spaces
- List variants: append "[]" (e.g. "string[]", "Billing.Invoice[]")
Deliverables:
1. docs/core/type-expression-vocabulary.md — canonical reference with every legal pattern, examples, and the full legal syntax in ABNF notation.
2. A static `TypeExpression.TryParse(string input, out TypeExpressionKind kind, out string? referencedTypeName)` method (or equivalent static helper) that validates input and returns false for invalid strings. TypeExpressionKind enum: Primitive, PrimitiveNullable, TypeReference, TypeReferenceNullable, PrimitiveList, TypeReferenceList.
3. Unit tests for: each primitive, nullable scalar, type reference, nullable type reference, list variants, invalid strings (empty, whitespace, unknown keyword, missing dot in type ref, multiple brackets).

## Stage 2: Diagnostics and Compatibility Engine

### T20 - Implement Diagnostic contract envelope
- Depends on: T01, T11, T12
- Owner lane: Validation
- Output: diagnostic classes + tests
- Agent prompt:
Implement structured diagnostic envelope and item model with required fields and validation. Add tests for serialization shape.

### T21 - Implement compatibility classifier utility
- Depends on: T02
- Owner lane: Validation
- Output: compatibility classifier + tests
- Agent prompt:
Implement utility that classifies change kinds using the documented taxonomy. Add table-driven tests.

### T22 - Implement publish gate checker
- Depends on: T21
- Owner lane: Validation
- Output: publish gate logic + tests
- Agent prompt:
Implement publish gate checker enforcing major version bump for breaking changes. Add tests covering pass/fail cases.

## Stage 3: Evaluation Core (Minimal, Deterministic)

### T30 - Create evaluation context contract
- Depends on: T11, T12
- Owner lane: Runtime
- Output: evaluation context + tests
- Agent prompt:
Create evaluation context contract including tenant, mode, version refs, correlation metadata. Add tests.

### T31 - Implement fast-fail and full-report modes
- Depends on: T20, T30
- Owner lane: Runtime
- Output: mode behavior in evaluator + tests
- Agent prompt:
Add evaluation mode support for fast-fail and full-report in a deterministic pipeline skeleton. Add tests proving behavior.

### T32 - Implement deterministic ordering helper
- Depends on: T11
- Owner lane: Runtime
- Output: ordering utility + tests
- Agent prompt:
Implement deterministic ordering helper using declaration order plus SemanticId tie-break. Add tests.

### T33 - Implement simple constraint evaluator
- Depends on: T31, T32
- Owner lane: Runtime
- Output: constraint evaluator baseline + tests
- Agent prompt:
Implement baseline constraint evaluation with deterministic diagnostics and no side effects. Add tests.

### T34 - Implement rule evaluator composition
- Depends on: T33
- Owner lane: Runtime
- Output: rule composition evaluator + tests
- Agent prompt:
Implement composable rule evaluation (and/or/not baseline) producing deterministic diagnostics. Add tests.

## Stage 4: Service Surfaces (REST + MCP)

### T40 - Define REST API contracts and OpenAPI skeleton
- Depends on: T20, T22, T34
- Owner lane: Serving
- Output: OpenAPI file and DTO contracts
- Agent prompt:
Create a minimal versioned OpenAPI contract covering: metadata (GET /models, GET /models/{id}), evaluate (POST /models/{id}/evaluate), explain, and compatibility check. All endpoints must carry tenant context (X-Tenant-Id header or JWT tid claim — document both). Include a sharing/grants sub-surface: GET /models/{id}/grants and POST /models/{id}/grants to allow a model owner to grant read access to a specific tenant. Model visibility is tenant-private by default; no cross-tenant read without an explicit grant. Keep DTO shapes minimal and schema-validated.

### T41 - Implement REST metadata endpoints
- Depends on: T40, T56
- Owner lane: Serving
- Output: metadata endpoints + tests
- Agent prompt:
Implement REST metadata endpoints returning canonical model descriptors and versions. Enforce tenant scope (T50) on every request. Enforce grant-based cross-tenant access via IGrantStore (T56): if the requester's tenantId does not match the model's owning tenantId, call IGrantStore.IsGranted(modelId, requesterTenantId); if false, return 403 with diagnostic code AUTH.GRANT_REQUIRED. Own-tenant reads are always allowed. Add tests for own-tenant access (allowed), cross-tenant without grant (blocked with AUTH.GRANT_REQUIRED), cross-tenant with active grant (allowed).

### T42 - Implement REST evaluate endpoint
- Depends on: T34, T40, T56
- Owner lane: Serving
- Output: evaluate endpoint + tests
- Agent prompt:
Implement evaluate endpoint using runtime evaluator with deterministic diagnostics envelope. Enforce tenant scope (T50). Enforce grant-based cross-tenant access via IGrantStore (T56) before dispatching evaluation: if requester's tenantId != model owner's tenantId and IGrantStore.IsGranted returns false, return 403 AUTH.GRANT_REQUIRED without running the evaluation. Add tests.

### T43 - Implement REST compatibility endpoint
- Depends on: T21, T40
- Owner lane: Serving
- Output: compatibility endpoint + tests
- Agent prompt:
Implement compatibility endpoint returning classification and publish gate result. Add tests.

### T44 - Implement MCP read/evaluate tools parity
- Depends on: T41, T42, T56
- Owner lane: Serving
- Output: MCP tools/resources + parity tests
- Agent prompt:
Implement MCP capabilities matching REST metadata and evaluate semantics. Add parity tests ensuring same outcomes. IMPORTANT: MCP operations must be read-only (discovery, query, evaluation) and must not mutate model state. No MCP tool may bypass the construction or governance pipeline. Enforce grant-based cross-tenant access via IGrantStore (T56) for every MCP discovery and evaluation tool, identical to T41/T42 enforcement. Add an explicit test asserting that no registered MCP tool performs a state mutation, and that cross-tenant discovery without a grant is blocked.

### T45 - Define and document SLO baseline after evaluate endpoint
- Depends on: T42
- Owner lane: Ops
- Output: docs/operations/slo-baseline.md + benchmark notes
- Agent prompt:
Run the evaluate endpoint under representative load and capture baseline measurements: p50/p95/p99 latency, throughput (req/s), max diagnostics payload size, and cold-start time. Record these as the initial SLO targets in docs/operations/slo-baseline.md with notes on test conditions. Do not invent targets; measure and record actuals.

### T08 - Simulator strategy benchmark spike
- Depends on: T34
- Owner lane: Runtime
- Output: docs/adr/ADR-002-simulator-strategy.md with benchmark evidence
- Agent prompt:
Before committing to a simulator implementation strategy, run a micro-benchmark comparing: (A) expression-compiled delegates via System.Linq.Expressions with Dictionary<string,object?> shims, and (B) direct interpreter over a projected node graph. Use the V2 evaluation contracts from T34 as the test surface. Measure: cold-start latency, steady-state throughput, memory allocation per evaluation, and code complexity. Record results and a selection rationale in an ADR. Do not implement the full simulator — this is evidence gathering only.
## Stage 5: Security, Tenant, Idempotency, Governance

### T47 - Define IApprovalStore interface and in-memory default
- Depends on: T11
- Owner lane: Serving
- Output: IApprovalStore interface + in-memory TTL-backed implementation + tests
- Agent prompt:
Define the IApprovalStore interface for tracking pending and resolved governance approvals. ApprovalRecord fields: ApprovalId (SemanticId), ModelId (SemanticId), TenantId (SemanticId), RequestedAt (DateTimeOffset), ResolvedAt (DateTimeOffset?), Status (enum: Pending|Approved|Rejected), ResolvedBy (string?). Interface methods: Create(ApprovalRecord), TryGet(SemanticId approvalId, out ApprovalRecord?), Resolve(SemanticId approvalId, ApprovalStatus status, string resolvedBy). Implement an in-memory default backed by ConcurrentDictionary with configurable TTL (records expire after TTL from ResolvedAt, or from RequestedAt if unresolved). This is the v1 default; the interface is the swap point for a persistent store in production. Add tests: create, get, resolve, TTL expiry, unknown ID returns null.

### T48 - Implement approval polling endpoint
- Depends on: T40, T47, T50
- Owner lane: Serving
- Output: GET /models/{id}/approvals/{approvalId} endpoint + OpenAPI amendment + tests
- Agent prompt:
Add GET /models/{id}/approvals/{approvalId} to the OpenAPI contract (amend T40's output file). Implement the endpoint returning: { approvalId, modelId, status: "pending"|"approved"|"rejected", requestedAt, resolvedAt?, resolvedBy? }. Enforce tenant scope (T50): the caller's tenantId must match the model's owning tenantId. Unknown approvalId returns 404. Resolved records beyond TTL return 410 Gone. Add integration tests for: pending status, approved status, rejected status, unknown ID (404), expired record (410), cross-tenant access denied.

### T49 - Define ApprovalPolicy model, IApprovalPolicyStore, and management endpoints
- Depends on: T11, T60g
- Owner lane: Serving
- Output: ApprovalPolicy model + IApprovalPolicyStore interface + in-memory default + management endpoints + tests
- Agent prompt:
Define ApprovalPolicy enum: SelfApprove (default), RoleGatedSync, AsyncPending. Define IApprovalPolicyStore interface: GetPolicy(SemanticId tenantId) → ApprovalPolicy (returns SelfApprove if no record), SetPolicy(SemanticId tenantId, ApprovalPolicy policy). Implement an in-memory default backed by ConcurrentDictionary. This is the v1 default; the interface is the swap point for persistent storage in production. Add management endpoints: GET /tenants/{id}/approval-policy (returns current policy), PUT /tenants/{id}/approval-policy (sets policy, requires admin role enforced via T51 role check, validates tenantId via ITenantRegistry from T60g). Add tests: unset returns SelfApprove, set and get round-trips, non-admin PUT rejected.

### T56 - Define IGrantStore interface, in-memory default, and grant management endpoints
- Depends on: T40, T11
- Owner lane: Serving
- Output: IGrantStore interface + in-memory default + /grants endpoints in OpenAPI + tests
- Agent prompt:
Define IGrantStore interface. GrantRecord fields: GrantId (SemanticId), ModelId (SemanticId), GrantorTenantId (SemanticId), GranteeTenantId (SemanticId), GrantedAt (DateTimeOffset), RevokedAt (DateTimeOffset?). Interface methods: Grant(GrantRecord), Revoke(SemanticId grantId), IsGranted(SemanticId modelId, SemanticId requesterTenantId) → bool. Implement in-memory default. Amend T40's OpenAPI file to add grant management endpoints: GET /models/{id}/grants (list active grants, requires owner role), POST /models/{id}/grants body:{granteeTenantId} (create grant, requires owner role), DELETE /models/{id}/grants/{grantId} (revoke, requires owner role). Add tests: owner's model accessible without grant, cross-tenant blocked without grant, grant created allows access, revoked grant blocks access, grant for model A does not extend to model B.

### T57 - Conformance: grant lifecycle access control
- Depends on: T56
- Owner lane: Serving
- Output: conformance test suite + evidence doc
- Agent prompt:
Write a conformance test suite asserting the complete grant lifecycle across REST and MCP paths: (1) model owner reads own model — always allowed; (2) cross-tenant read with no grant — blocked with AUTH.GRANT_REQUIRED; (3) owner creates grant → cross-tenant read succeeds; (4) owner revokes grant → cross-tenant read blocked; (5) grant for model A does not allow access to model B (no spillover); (6) evaluate endpoint respects grant identically to metadata; (7) MCP discovery respects grant identically to metadata. Add evidence doc (docs/testing/conformance-grant-lifecycle.md).

### T58 - Define IApiKeyStore interface and in-memory default
- Depends on: T11, T03
- Owner lane: Serving
- Output: IApiKeyStore interface + in-memory default seeded from configuration + tests
- Agent prompt:
Define IApiKeyStore interface. ApiKeyRecord fields: KeyId (SemanticId), HashedSecret (string — HMAC-SHA256, never plaintext), TenantId (SemanticId), Roles (string[]), IsRevoked (bool), CreatedAt (DateTimeOffset). Interface methods: TryAuthenticate(string rawKey, out ApiKeyPrincipal?) (hashes raw key and looks up; returns false if not found or revoked), Revoke(SemanticId keyId). Implement in-memory default that can be seeded from a configuration section (list of {tenantId, roles, unhashedSecret} entries for dev only — hash at startup, discard plaintext). Never store or log unhashed secrets after startup. The interface is the swap point for a persistent key store in production. Add tests: valid key authenticates, invalid key rejected, revoked key rejected, config seed hashes and stores correctly.

### T59 - Implement API key management endpoints
- Depends on: T51, T58
- Owner lane: Serving
- Output: POST /api-keys, DELETE /api-keys/{keyId}, GET /api-keys endpoints + tests
- Agent prompt:
Implement API key lifecycle management endpoints. POST /api-keys: requires admin role; creates a new key in IApiKeyStore; returns { keyId, plainTextSecret } exactly once (the only time the secret is visible — never return it again). DELETE /api-keys/{keyId}: requires admin role; calls IApiKeyStore.Revoke. GET /api-keys: requires admin role; returns [{ keyId, tenantId, roles, isRevoked, createdAt }] with no secrets. Enforce that POST /api-keys only creates keys for the caller's own tenant (tenant scope from T50). Add integration tests for all three operations including role enforcement and that the secret is not returned by GET.

### T60f - Define IAuditLogSink interface, AuditEvent contract, and in-memory default
- Depends on: T11
- Owner lane: Serving
- Output: IAuditLogSink interface + AuditEvent contract + in-memory append-only default + tests
- Agent prompt:
Define AuditEvent contract fields: EventId (SemanticId), EventType (string, e.g. "governance.publish", "auth.tenant_mismatch"), TenantId (SemanticId), CallerIdentity (string — JWT subject or API key ID), ResourceId (SemanticId?), OccurredAt (DateTimeOffset), Outcome (enum: Success|Denied|Failed), DiagnosticCode (string?). Define IAuditLogSink: Append(AuditEvent), IReadOnlyList<AuditEvent> GetAll() (for test inspection). Implement an in-memory default (thread-safe append-only list, evict oldest beyond 10,000 entries). This is the v1 default; the interface is the swap point for a structured log or SIEM sink in production. T46 and T50 MUST inject IAuditLogSink and call Append for every governance transition and every auth rejection — not ILogger. T53 and T54 conformance tests MUST assert audit entries via GetAll() on the injected sink, not by log scraping. Add tests: append, GetAll, capacity eviction.

### T60g - Define ITenantRegistry interface and in-memory default
- Depends on: T11
- Owner lane: Serving
- Output: ITenantRegistry interface + in-memory default + tests
- Agent prompt:
Define ITenantRegistry interface. TenantRecord fields: TenantId (SemanticId), Name (non-null string), IsActive (bool), CreatedAt (DateTimeOffset). Interface methods: Register(TenantRecord), TryGet(SemanticId tenantId, out TenantRecord?) → bool, IsActive(SemanticId tenantId) → bool. Implement in-memory default backed by ConcurrentDictionary. T50 MUST inject ITenantRegistry and call IsActive(tenantId) after extracting tenant identity; if not registered or inactive, return AUTH.TENANT_UNKNOWN. The interface is the swap point for a persistent registry in production. Add tests: register, IsActive for active tenant (true), IsActive for unknown tenant (false), IsActive for inactive tenant (false).

### T60h - Implement tenant provisioning endpoints
- Depends on: T60g, T51
- Owner lane: Serving
- Output: POST /tenants, GET /tenants/{id}, DELETE /tenants/{id} endpoints + tests
- Agent prompt:
Implement tenant lifecycle management endpoints. POST /tenants: requires platform-admin role (add to T03's auth matrix as a role outside normal tenant scope); registers a new tenant in ITenantRegistry; returns TenantRecord. GET /tenants/{id}: requires platform-admin role; returns TenantRecord. DELETE /tenants/{id}: requires platform-admin role; marks tenant inactive (does not delete data). All endpoints enforce authorization via T51's role/scope check. Add integration tests for all three endpoints including the platform-admin role requirement.

### T50 - Implement tenant scope middleware/policy checks
- Depends on: T03, T41, T60g
- Owner lane: Serving
- Output: tenant policy enforcement + tests
- Agent prompt:
Implement tenant scope checks as middleware that runs on every service request. Extract tenant identity from: the JWT Bearer `tid` claim for interactive callers, or a dedicated `X-Tenant-Id` header for API key callers (API key is validated separately in T51). After extracting the tenant ID, validate it exists and is active by calling ITenantRegistry.IsActive(tenantId) from T60g; if not registered or inactive, return AUTH.TENANT_UNKNOWN. Reject requests with no resolvable tenant with AUTH.TENANT_MISSING. Reject cross-tenant access attempts with AUTH.TENANT_MISMATCH. Emit an audit log entry (via IAuditLogSink from T60f) for every rejection. Add integration tests covering: valid JWT tenant, valid API key + header tenant, missing tenant, unknown tenant, mismatched tenant.

### T51 - Implement role/scope authorization checks
- Depends on: T03, T41, T58
- Owner lane: Serving
- Output: authorization policy checks + tests
- Agent prompt:
Implement role/scope authorization as a policy layer that runs after T50 tenant resolution. Support two auth mechanisms: (1) JWT Bearer — extract roles from the `roles` or `scp` claim; (2) API key — inject IApiKeyStore (T58) and call TryAuthenticate(rawKey, out principal) to resolve the key's associated role set. Apply the role matrix from docs/security/tenant-auth-matrix.md. Reject with AUTH.INSUFFICIENT_SCOPE and a diagnostic identifying which role is required. Add integration tests for: allowed operation with JWT role, allowed operation with API key role, denied operation with insufficient role on both mechanisms.

### T52 - Implement idempotency key enforcement for control operations
- Depends on: T03, T40
- Owner lane: Serving
- Output: idempotency handling + tests
- Agent prompt:
Implement the IIdempotencyStore interface with an in-memory default implementation (ConcurrentDictionary-backed, TTL per entry). Wire it into the governance control-plane endpoints (T46). Note: T52 defines the interface and wiring independently — its dependency is on T40 (the OpenAPI contract that defines which endpoints require idempotency enforcement), not on T46's full completion, to avoid a circular dependency. T46 will consume the IIdempotencyStore registered in DI. Contract: every stage/publish/deprecate/sunset request must carry an Idempotency-Key header; absent key returns IDEMPOTENCY.KEY_MISSING; duplicate key within TTL replays the original response without re-executing the operation; expired key is treated as a new request. The in-memory store is the v1 default; the interface is the swap point for Redis in production. Add tests: first request executes, duplicate replays, missing key rejected, expired key re-executes.

### T53 - Conformance: cross-tenant access breakout
- Depends on: T50, T51
- Owner lane: Serving
- Output: conformance test suite + evidence doc
- Agent prompt:
Write a conformance test suite that exhaustively verifies tenant isolation cannot be bypassed. Test matrix: (1) caller with valid JWT for Tenant A attempts to read Tenant B’s model — must be rejected with AUTH.TENANT_MISMATCH; (2) caller with API key scoped to Tenant A attempts evaluate against Tenant B’s ruleset — rejected; (3) caller with no tenant identity on any endpoint — rejected with AUTH.TENANT_MISSING; (4) caller with admin role but wrong tenant — rejected (roles do not override tenant boundary). All rejections must produce a structured diagnostic and an audit log entry. Add an evidence doc (docs/testing/conformance-cross-tenant.md) listing each scenario and its expected outcome.

### T54 - Conformance: idempotency replay edge cases
- Depends on: T52
- Owner lane: Serving
- Output: conformance test suite + evidence doc
- Agent prompt:
Write a conformance test suite for idempotency replay edge cases: (1) first request succeeds and response is stored; (2) identical second request within TTL returns identical stored response without re-executing; (3) request with expired key is treated as a new request and re-executes; (4) partial failure (operation throws after key is stored) — verify the stored response reflects failure, not a false success; (5) two concurrent requests with the same key — exactly one executes, the other replays. Add evidence doc (docs/testing/conformance-idempotency.md).

### T55 - Conformance: diagnostic-code parity across evaluation profiles
- Depends on: T42, T44, T34
- Owner lane: Runtime
- Output: conformance test suite + evidence doc
- Agent prompt:
Write a conformance test suite asserting that the same input evaluated via (a) the in-process evaluator (T34), (b) the REST evaluate endpoint (T42), and (c) the MCP evaluate tool (T44) produces identical diagnostic codes, severities, and affected-path references. Use at least five representative rule scenarios: required field missing, range violation, lifecycle transition guard failure, cross-property rule failure, and a passing evaluation. Any divergence between profiles is a test failure. Add evidence doc (docs/testing/conformance-diagnostic-parity.md).

### T46 - Implement governance control plane endpoints
- Depends on: T40, T47, T48, T49, T50, T51, T52, T60f
- Owner lane: Serving
- Output: governance REST endpoints (stage, publish, deprecate, sunset) + MCP equivalents + tests
- Agent prompt:
Implement governance control-plane endpoints: POST /models/{id}/stage, POST /models/{id}/publish, POST /models/{id}/deprecate, POST /models/{id}/sunset. Each endpoint must: (1) enforce tenant scope (T50); (2) require appropriate role/scope (T51); (3) require an Idempotency-Key header (T52); (4) validate the governance state transition against the model's LifecycleModel (T91 lifecycle rules; for v1 use the self-hosted lifecycle); (5) before executing publish, read the tenant's approval policy from IApprovalPolicyStore (T49) — SelfApprove: proceed immediately; RoleGatedSync: check publisher role then proceed; AsyncPending: create an IApprovalStore (T47) record, return 202 Accepted with Location header pointing to the poll endpoint (T48), do not advance state; (6) emit an AuditEvent via IAuditLogSink (T60f) for every successful transition and every rejection. Add equivalent MCP tools for each write operation (read-only discovery is T44). Add integration tests for: happy path on all three approval policies, invalid transition, insufficient role, missing idempotency key, audit events emitted via inspectable IAuditLogSink.

## Stage 6: SDK and Customer Proof

### T60a - Generate C# SDK skeleton from OpenAPI
- Depends on: T40
- Owner lane: SDK
- Output: sdk/csharp folder with generated client + generation notes
- Agent prompt:
Generate a C# SDK skeleton from the OpenAPI contract (T40). Use NSwag or Kiota. Commit generated client artifacts, a generation script/config file, and a brief note describing how to regenerate. This is the primary SDK; it must compile and cover the metadata and evaluate endpoints.

### T60b - Generate TypeScript SDK skeleton from OpenAPI
- Depends on: T40
- Owner lane: SDK
- Output: sdk/typescript folder with generated client + generation notes
- Agent prompt:
Generate a TypeScript SDK skeleton from the OpenAPI contract (T40). Use Kiota or openapi-typescript. Commit generated client artifacts, a generation script/config file, and a brief note describing how to regenerate. Must cover the metadata and evaluate endpoints. Must compile under tsc with strict mode.

### T60c - Stub WASM SDK target (tracked, not yet implemented)
- Depends on: T60a
- Owner lane: SDK
- Output: sdk/wasm/README.md describing approach and prerequisites
- Agent prompt:
Create sdk/wasm/README.md documenting the intended WASM SDK approach (e.g. dotnet-wasm or wasm-bindgen), the prerequisites for implementation, and the trigger criteria (stable OpenAPI contract + identified customer). Do not implement any code. This is a planning stub only.

### T60d - Stub Zig SDK target (tracked, not yet implemented)
- Depends on: T60a
- Owner lane: SDK
- Output: sdk/zig/README.md describing approach and prerequisites
- Agent prompt:
Create sdk/zig/README.md documenting the intended Zig SDK approach (HTTP client + JSON parsing strategy), prerequisites, and trigger criteria. Do not implement any code. This is a planning stub only.

### T61 - Add diagnostics-first SDK helpers
- Depends on: T60a, T20
- Owner lane: SDK
- Output: helper APIs + tests (C# SDK)
- Agent prompt:
Add helper APIs for structured diagnostics handling and compatibility-aware responses to the C# SDK (T60a). Add tests.

### T62 - Build validity-only reference app sample
- Depends on: T61, T42
- Owner lane: SDK
- Output: sample app consuming hosted validation
- Agent prompt:
Build a minimal sample app that calls metadata and evaluate endpoints through the SDK and displays diagnostics.

### T63 - Demonstrate no-rebuild compatible rule rollout
- Depends on: T62, T22, T43
- Owner lane: Serving
- Output: scripted scenario + verification notes
- Agent prompt:
Create and document an end-to-end scenario where compatible rule changes are published and consumed by sample app without rebuild.

## Stage 7: Projection and Interoperability Minimum

### T70 - Implement minimal interpretation projection adapter
- Depends on: T13-T18, T34
- Owner lane: Core
- Output: projection adapter + tests
- Agent prompt:
Implement minimal canonical-to-interpretation projection required for evaluator/runtime path with SemanticId trace mapping. Add tests.

### T73 - Implement read-model projection adapter
- Depends on: T13-T18, T70
- Owner lane: Core
- Output: read-model projection + tests
- Agent prompt:
Implement a canonical-to-read-model projection that produces query-optimised, flat representations of DomainType and DomainProperty suitable for list/search/filter operations, without exposing persistence or canonical model internals. The read model must be a separate type hierarchy from the canonical contracts and the interpretation projection (T70). Add tests asserting projection outputs are flat, serializable, and do not contain canonical V2 type references. This enforces Architectural Constraint #5 (projection-specific models).

### T71 - Implement extension ingestion: OpenAPI and JSON Schema
- Depends on: T40
- Owner lane: Extensions
- Output: ingestion + mapping + tests
- Agent prompt:
Implement ingestion pipeline for OpenAPI and JSON Schema into normalized extension contract with deterministic diagnostics.

### T72 - Implement extension ingestion: AsyncAPI and gRPC/proto
- Depends on: T71
- Owner lane: Extensions
- Output: ingestion + mapping + tests
- Agent prompt:
Implement AsyncAPI and gRPC/proto ingestion into same normalized extension pipeline with anti-corruption mapping tests.

## Stage 8: Ops and Release Gates

### T80 - Implement trace and metric contract instrumentation
- Depends on: T42, T44, T50-T52
- Owner lane: Ops
- Output: trace + metrics baseline
- Agent prompt:
Add required trace and metric emissions for evaluate, compatibility, governance operations with correlation identifiers.

### T81 - Create runbooks for auth/tenant/idempotency failures
- Depends on: T80
- Owner lane: Ops
- Output: ops runbooks
- Agent prompt:
Write concise runbooks for diagnosing and mitigating auth, tenant isolation, and idempotency incidents.

### T82 - Execute release gate checklist and capture evidence
- Depends on: T63, T80, T81
- Owner lane: Ops
- Output: release evidence package
- Agent prompt:
Create release gate evidence package mapping artifacts to acceptance criteria and list remaining blockers.

## Stage 9: Self-Hosting L0–L2

### T90 - L0: Model registry describes its own schema
- Depends on: T13, T14, T15, T16
- Owner lane: Core
- Output: self-description bootstrap + tests
- Agent prompt:
Using the canonical V2 contracts, create a DomainModel that represents the model registry's own bounded context, aggregate, and core types using the same contracts the system exposes to customers. This is L0: the system can describe its own data shape. Add tests asserting the self-description is valid and passes invariant checks.

### T91 - L1: Governance lifecycle self-modeled
- Depends on: T90, T18
- Owner lane: Core
- Output: governance lifecycle self-model + tests
- Agent prompt:
Extend the self-hosted model from T90 to encode the governance lifecycle (draft/stage/publish/deprecate/sunset) as a LifecycleModel using V2 contracts. This is L1: the system models its own publish workflow. Add tests asserting valid and invalid transitions are correctly enforced.

### T92 - L2: Governance commands and transitions self-validated through the evaluation pipeline
- Depends on: T91, T34
- Owner lane: Core
- Output: self-hosted governance evaluation scenario + tests
- Agent prompt:
Route at least one governance command (e.g. StageModel, PublishModel) through the V2 evaluation pipeline using the self-hosted lifecycle model from T91. This is L2: the system evaluates its own business rules using the same engine it offers to customers. Add tests proving the round-trip works end-to-end.

## Parallelization Map (How to run multiple agents)

Each wave has internal sub-batches. Follow the batches — a wave is NOT a single parallel set.

Wave A (Stage 0 — setup and guardrails):
- Batch A-1 (all parallel, no dependencies): T00, T01, T02, T03, T04.
- Batch A-2 (after A-1): T05 (needs T04), T06 (needs T02), T10 (needs T00). All three in parallel.
- Batch A-3 (after A-2): T07 (needs T10), T09 (needs T10). Both in parallel.

Wave B (Stage 1 — canonical contracts, after T09):
- Batch B-1 (after T09): T11, T12 in parallel. Both must read Spike/OBSERVATIONS.md first.
- Batch B-2 (after T11; T12 may still be running): T14, T15, T17, T18 in parallel. Also start T19 (TypeExpression vocabulary, needs T11) in parallel. Note: T16 depends on T11 AND T19 — start T16 only after T19 completes.
- Batch B-3 (after BOTH T11 AND T12 done): T13 alone (needs both T11 and T12). Also start T16 now if T19 is done.

Wave C (Stage 2+3 — diagnostics, compatibility, evaluation):
- After T11: start T32 immediately (needs T11 only).
- After T01 + T11 + T12: T20. After T02: T21. After T21: T22. After T11 + T12: T30.
- After T20 + T30 + T32 all done: T31.
- T33 needs T31 + T32 + T19 all done (TypeExpression vocabulary must be defined before constraint evaluator).
- After T33: T34.

Wave D (Stage 4 — service surfaces, after T34 + T20 + T22):
- Batch D-1: T40 (needs T20 + T22 + T34). Also start T08 (needs T34) and T56 (needs T40 + T11) in parallel with or right after T40.
- Batch D-2 (after T40 + T56 both done): T41 (needs T40 + T56), T42 (needs T34 + T40 + T56), T43 (needs T21 + T40), T60a (needs T40), T60b (needs T40) — all in parallel.
- Batch D-3 (after T60a done): T60c (needs T60a), T60d (needs T60a), T61 (needs T60a + T20) — all in parallel.
- After T41 + T42: T44 (needs T41 + T42 + T56).
- T45 (needs T42): after T42 done.
- T47 (needs T11), T48 (needs T40 + T47 + T50 — start T47 as early as Wave B-1), T49 (needs T11 + T60g — start after T60g), T58 (needs T11 + T03 — start as early as Wave A-1 + B-1).
  Note: T47 and T58 can start very early (they only need T11 and T03 respectively); dispatch them alongside Wave D-1 or even Wave B-1.

Wave E (Stage 5 — security/tenant/idempotency/governance, after T03 + T41):
- T60f (needs T11) and T60g (needs T11): start as early as Wave B-1; must complete before T50 and T49.
- T60h (needs T60g + T51): after T60g + T51 both done.
- T58 (needs T11 + T03): can run as early as Wave A-1 + B-1.
- T50 (needs T03 + T41 + T60g): after T03 + T41 + T60g all done.
- T51 (needs T03 + T41 + T58): after T03 + T41 + T58 all done.
- T52 (needs T03 + T40): after T03 + T40 done (NOTE: T52 does NOT depend on T46 — cycle resolved).
- T49 (needs T11 + T60g): after T60g done.
- T53 (needs T50 + T51): after T50 + T51 done. Run in parallel with T54.
- T54 (needs T52): after T52 done.
- T55 (needs T42 + T44 + T34): after T44 done.
- T57 (needs T56): after T56 done (may already be done from Wave D).
- T59 (needs T51 + T58): after T51 + T58 both done.
- T48 (needs T40 + T47 + T50): after T50 done.
- T46 (needs T40 + T47 + T48 + T49 + T50 + T51 + T52 + T60f): runs LAST in Wave E — all store and security tasks must be complete.

Wave F (Stage 6+8 — customer proof and ops, near finish):
- Batch F-1 (parallel): T62 (needs T61 + T42), T80 (needs T42 + T44 + T50 + T51 + T52 + T60f).
- Batch F-2 (parallel, after F-1): T63 (needs T62 + T22 + T43), T81 (needs T80).
- Batch F-3: T82 (needs T63 + T80 + T81 all done).

Wave I (Stage 7 — projections and interoperability, after Waves B-3 + C + D-1):
- Batch I-1 (parallel): T70 (needs T13 + T14 + T15 + T16 + T17 + T18 + T34) and T71 (needs T40) — start when T34 and T40 are both done and all T13–T18 are done.
- Batch I-2 (after I-1): T73 (needs T70) and T72 (needs T71) in parallel.

Wave H (Stage 9 — self-hosting, after T13–T18 and T34):
- T90 (needs T13 + T14 + T15 + T16): after those complete (Wave B-3).
- T91 (needs T90 + T18): after T90 + T18.
- T92 (needs T91 + T34): after T91 + T34.
- T08 (needs T34): run alongside T90 — both depend on T34 and are independent of each other.

## Task Template for Any Agent
Use this exact format in each agent request:
1. Task ID and objective.
2. Allowed files/folders only.
3. Non-goals (what not to change).
4. Acceptance criteria.
5. Validation steps to run.
6. Output summary format.

## Minimal Daily Operating Rhythm (for one person)
1. Morning: dispatch 3 to 5 parallel low-conflict tasks.
2. Midday: merge completed tasks with tests.
3. Afternoon: resolve blockers and dispatch next wave.
4. End of day: update dependency board and release risk list.

## Post-v1 Deferred Structural Refactor

### T99 - Extract v2 subsystems into dedicated libraries
- Depends on: T82, T92
- Owner lane: Core
- Output: extraction plan + project split PR sequence
- Agent prompt:
After v1 acceptance gates are complete, design and execute a safe extraction of v2 subsystems from the current `Poly` project into dedicated libraries (Core/Construction/Validation/Runtime/Serving/Projection) with unchanged public behavior. This task is explicitly deferred and must not be pulled into current v1 execution waves.
