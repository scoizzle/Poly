# Downstream Analysis Consumption Remediation Plan

Date: 2026-07-28
Status: Draft
Related: docs/plans/analysis-consuming-lowering.md, docs/plans/domain-analysis-unification.md, docs/CORE.md

Micro-task queue: docs/plans/simple-agent-tasks/dacr-README.md

## 1) Problem Statement

Multiple downstream consumers still derive semantic facts by directly navigating Domain, Entity, Relationship, Stage, and Property trees. This duplicates analysis work already available as metadata and increases drift risk.

Current downstream state from code tour:
- Lowering/export: partially migrated (metadata-first with fallbacks).
- Runtime instance execution and subscription dispatch: mostly direct semantic rediscovery.
- Evolution mutation targeting: mostly direct semantic rediscovery.
- MCP tooling: mixed (one strong metadata-first analysis endpoint plus several direct lookup endpoints).
- DslCompiler: mixed (metadata-first core plus direct scans in specific generators).

Hard rule:
- Analysis is mandatory. There is no supported downstream execution path without an AnalysisResult.

## 2) Why This Happened

This is mostly architectural sequencing, not one bad local decision.

1. Analysis came online after several downstream surfaces were already shipping.
- Runtime, MCP, and evolution paths were implemented for correctness and product velocity before complete domain metadata coverage existed.

2. Two legitimate needs were conflated:
- Static semantics (names, relationships, ownership, effective policies, constructor order) should be analysis-owned.
- Dynamic runtime state (instance links, current stage, per-call args) cannot be analysis-owned.

3. Optional analysis wiring encouraged fallback behavior.
- AnalysisResult and INodeMetadataProvider were treated as optional in several APIs, so direct rescans became the safe fallback path.

4. Metadata shape is still optimized for analyzers, not all consumers.
- Some consumers need pre-indexed maps or resolved plans (for example per-stage action/policy resolution and relationship dispatch plans).

5. Mutation handlers optimized for local readability.
- Evolution change handlers often re-find stages/actions/relationships by name in each handler; this is easy to author but duplicates semantic targeting logic.

## 3) Migration Principle (What Should Move)

Move direct tree navigation to metadata only when the operation answers a semantic question.

Keep direct navigation when the operation is structural rendering or dynamic runtime state handling.

Use this rubric:
- Metadata-first semantic (target): "what does this mean?"
- Structural traversal (allowed): "how do I project/emit this shape?"
- Runtime dynamic (allowed with metadata assist): "what is true now for this instance graph?"

Policy:
- "Runtime dynamic" does not mean "analysis optional." Runtime paths still require AnalysisResult for static contract and semantic lookups; only live instance state remains runtime-owned.

## 4) Target Architecture for Consumers

1. Analysis is the source of static domain semantics.
2. Downstream consumers call lookup/plan helpers backed by metadata.
3. Runtime keeps dynamic instance decisions local, but consumes static metadata for contracts.
4. Fallback rescans are disallowed for semantic routes. Missing analysis in downstream consumers is a hard error.

## 5) Net-New Metadata and Helper APIs

Add only metadata that has multiple concrete consumers.

A. StageLookupMetadata (entity-scoped)
- Purpose: O(1) stage lookup by name.
- Shape:
  - IReadOnlyDictionary<string, Stage> StageByName

B. ActionResolutionMetadata (entity-scoped)
- Purpose: one source for effective action lookup and guard policy composition by stage.
- Shape:
  - IReadOnlyDictionary<string, Action> EntityActionsByName
  - IReadOnlyDictionary<string, IReadOnlyDictionary<string, Action>> StageActionsByStageAndName
  - IReadOnlyDictionary<string, IReadOnlyList<Policy>> EffectiveStagePoliciesByStage

C. RelationshipContractMetadata (domain-scoped)
- Purpose: relationship lookup + direction/cardinality contract checks without repeated scans.
- Shape:
  - IReadOnlyDictionary<string, Relationship> ByName
  - IReadOnlyDictionary<string, IReadOnlyList<Relationship>> OutboundBySourceEntity
  - IReadOnlyDictionary<string, IReadOnlyList<Relationship>> InboundByTargetEntity

D. SubscriptionDispatchPlanMetadata (domain-scoped)
- Purpose: pre-resolved static subscription matching contract.
- Shape:
  - normalized entries keyed by subscriber entity + subscriber stage + relationship name + target stage + quantifier
  - includes resolved Relationship and target entity name

E. MutationTargetIndexMetadata (domain-scoped)
- Purpose: shared name-based targeting for evolution handlers.
- Shape:
  - entity/stage/action/policy/relationship indexes
  - optional ambiguity/missing markers for fail-closed diagnostics

Helper API surface:
- DomainSemanticLookupExtensions on AnalysisResult.
- Example methods:
  - TryGetStage(entity, stageName, out Stage)
  - TryResolveAction(entity, currentStage, actionName, out Action)
  - GetEffectiveStagePolicies(entity, currentStage)
  - TryGetRelationship(name, out Relationship)
  - GetOutboundRelationships(entityName)

## 6) Remediation Plan by Area

Execution IDs:
- P0.1-P0.4: Governance and safety guardrails
- P1.1-P1.4: Lowering cleanup and AnalysisResult-required contract
- P2.1-P2.4: MCP semantic surfaces
- P3.1-P3.4: DslCompiler semantic lookup migration
- P4.1-P4.4: Runtime static and dynamic split
- P5.1-P5.4: Evolution target resolution unification
- P6.1-P6.3: Contract enforcement and nullable signature cleanup
- G1-G5: Final gate and fail-closed verification

See detailed tasks and acceptance criteria in docs/plans/simple-agent-tasks/dacr-README.md.

Task file map:
- Phase 0: docs/plans/simple-agent-tasks/dacr-p0-guardrails.md
- Phase 1: docs/plans/simple-agent-tasks/dacr-p1-lowering-required-analysis.md
- Phase 2: docs/plans/simple-agent-tasks/dacr-p2-mcp-semantic-lookups.md
- Phase 3: docs/plans/simple-agent-tasks/dacr-p3-dslcompiler-semantic-lookups.md
- Phase 4: docs/plans/simple-agent-tasks/dacr-p4-runtime-static-dynamic.md
- Phase 5: docs/plans/simple-agent-tasks/dacr-p5-evolution-target-index.md
- Phase 6: docs/plans/simple-agent-tasks/dacr-p6-contract-enforcement.md
- Gate: docs/plans/simple-agent-tasks/dacr-gate.md

## Phase 0: Governance and Safety

Goal: Prevent further spread before major refactors.

Tasks:
1. Add a short guidance section to docs/plans/analysis-consuming-lowering.md referencing this cross-cutting plan.
2. Add a review rule: new semantic logic in downstream consumers must prefer metadata lookup helpers.
3. Tag existing fallback sites with TODO markers using a single tracker label (for example DM-META-REMOVE-FALLBACK).
4. Add guard assertions at downstream boundaries: if AnalysisResult is null/missing, fail closed with explicit error.

Exit:
- New semantic code paths do not introduce direct rescans or nullable-analysis contracts.

## Phase 1: Finish Lowering Cleanup

Goal: complete obvious remaining semantic rescans in lowering/export.

Tasks:
1. Implement StageLookupMetadata consumption in EffectLoweringPass stage transition path.
2. Ensure create-in relationship target resolution uses resolved metadata first with single shared helper.
3. Route enum/entity/relationship lookups through shared DomainSemanticLookupExtensions.
4. Remove fallback rescans and replace with fail-closed errors if required metadata is absent.

Exit:
- Lowering semantic lookups are helper-based and metadata-first.
- Lowering APIs require AnalysisResult (not optional provider).

## Phase 2: MCP Query/Describe Surfaces

Goal: make user-facing domain facts consistent with analysis truth.

Tasks:
1. Update QueryTool and OracleTool describe endpoints to use metadata-backed lookup helpers for semantics.
2. Keep structural list/projection routes direct when they are shape-only.
3. Ensure get_domain_analysis remains the canonical diagnostics and semantic facts endpoint.
4. Ensure tools fail closed when session analysis is unavailable (no semantic fallback scans).

Exit:
- MCP semantic descriptions do not re-derive relationships/stage capabilities ad hoc.

## Phase 3: DslCompiler Simplification

Goal: reduce duplicated semantic inference in generators.

Tasks:
1. Keep DslCompiler metadata-first core (already present).
2. In HttpFileGenerator and MinimalApiGenerator, replace repeated enum/relationship/constructor semantics with metadata-backed helper lookups where behavior depends on semantics.
3. Keep direct traversal for pure output formatting and ordering.
4. Make generator entry points require AnalysisResult for semantic decisions.

Exit:
- Generator decisions that affect behavior are metadata-backed; cosmetic formatting remains structural.

## Phase 4: Runtime Static/Dynamic Split

Goal: unify static contracts via metadata while keeping runtime dynamic state local.

Tasks:
1. DomainEntityInstance:
- use ActionResolutionMetadata for stage/entity action resolution and guard policy composition.
- use StageLookupMetadata for stage transitions.
- use RelationshipContractMetadata for relationship direction/cardinality checks.
2. DomainInstanceStore:
- use SubscriptionDispatchPlanMetadata for static matching contract.
- keep instance link checks and quantifier evaluation runtime-local.
3. Preserve fail-closed runtime behavior (missing target, ambiguous links, unsupported direction).
4. Require AnalysisResult in runtime entry paths that resolve actions/stages/relationships.

Exit:
- Runtime no longer re-derives static contracts by scanning trees in hot paths.

## Phase 5: Evolution Handler Unification

Goal: remove duplicated name-resolution logic across mutation handlers.

Tasks:
1. Introduce MutationTargetIndexMetadata and shared resolution helpers for handlers.
2. Refactor DomainMutationContext and selected DomainChange.ApplyTo handlers to consume unified targeting.
3. Keep handler-specific mutation effects local; centralize only resolution logic and diagnostics.
4. Require AnalysisResult for mutation-target resolution.

Exit:
- Evolution target resolution is centralized and fail-closed.

## Phase 6: Contract Enforcement and Cleanup

Goal: enforce AnalysisResult-required contracts and remove legacy optional signatures.

Tasks:
1. Remove nullable AnalysisResult parameters from downstream semantic APIs.
2. Remove compatibility shims that allow semantic execution without analysis.
3. Add tests that assert missing analysis fails closed at API boundaries.

Exit:
- No semantic downstream route supports execution without AnalysisResult.

## 7) What Not To Migrate

Do not force these into analysis metadata:
1. Instance link presence/absence and current stage values at runtime.
2. Per-invocation argument values and intermediate VM execution state.
3. Pure projection/render ordering that has no semantic effect.

## 8) Verification Strategy

1. Behavior parity tests for each migrated area (before/after output equivalence where required).
2. Metadata-shape tests for new metadata records.
3. Fail-closed tests:
- missing relationship/stage/action should fail loudly
- missing AnalysisResult at required boundaries should fail loudly
- no vacuous success when required metadata is absent in required paths
4. Existing suite baseline:
- dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
- dotnet run --project Poly.Tests/Poly.Tests.csproj

## 9) Prioritized Start Order

1. Phase 1 (finish lowering)
2. Phase 2 (MCP semantic surfaces)
3. Phase 4 (runtime static/dynamic split)
4. Phase 3 (DslCompiler cleanup)
5. Phase 5 (evolution unification)
6. Phase 6 (contract enforcement and cleanup)

Reasoning:
- Highest leverage first where drift risk is high and scope is constrained.
- Runtime split is high value but should follow helper/metadata stabilization.

## 10) Deliverables

1. New metadata records and helper extension APIs.
2. Refactored consumers by phase.
3. Route inventory table updated with status per path: metadata-first, structural, runtime-dynamic.
4. Boundary contract table listing each downstream API now requiring AnalysisResult.
5. Updated docs/plans/analysis-consuming-lowering.md with a link to this plan and current status pointer.
