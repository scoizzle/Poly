# Poly Domain System Spec

## Status

Draft

## Purpose

Define the target architecture and simplification direction for the Poly domain modeling system as a whole.

This document is system-first. The DSL is a supporting interface, not the system itself.

## Companion Documents

1. DOMAIN-SYSTEM-SPEC.md: system simplification, core model, mutation pipeline, and migration strategy.
2. DOMAIN-DSL-SPEC.md: DSL-specific authoring format, grammar direction, canonical printing, and versioning.

## Problem

The current system expresses rich concepts, but the shape is harder to evolve and reason about than needed:

- too many first-class concepts for common workflows
- overlap between intent and command layers
- stage modeling complexity compared to expected usage
- machine-oriented structures leaking into authoring and review

The goal is to simplify the core while preserving correctness, transactional safety, and future operability.

## Goals

1. Keep actors as first-class domain concepts.
2. Make record lifecycle a first-class, enforceable domain concept.
3. Simplify lifecycle representation while preserving lifecycle semantics.
4. Keep effects expressive without coupling the rest of the model to effect complexity.
5. Model relationships primarily as typed properties.
6. Keep transactional mutations while reducing duplication between intents and commands.
7. Preserve future support for event subscriptions.
8. Keep import/export interfaces aligned to the core model, not the other way around.

## Non-Goals

1. Replacing JSON as the MCP wire protocol.
2. Supporting direct cross-entity property mutation.
3. Designing a general-purpose language outside Poly domain modeling.
4. Building editor tooling in the first iteration.

## System Principles

1. Domain model is the source of truth.
2. Interfaces (JSON, DSL, UI) project from the core model and map back to it.
3. Prefer explicit semantics over transport-driven DTO shape.
4. Keep operational guarantees: transactional apply, rollback, deterministic analysis.
5. Add complexity only when required by concrete scenarios.
6. Lifecycle correctness is a primary correctness axis, not an optional modeling convenience.

## Simplification and Refactoring Plan

### 1. Actor as a First-Class Citizen

- Actors are primary concepts, not secondary special-cases.
- Identity, claim mapping, and role semantics remain explicit and discoverable.

### 2. Stages as Enum

- Keep lifecycle as a first-class model, even if runtime state is represented as enums.
- Define explicit lifecycle contracts: states, transitions, guards, required data at state, and terminal-state rules.
- Parent types define coarse lifecycle categories.
- Child types define specific lifecycle states mapped to parent categories.
- Policies targeting parent lifecycle categories evaluate over mapped child-state sets.
- Treat transition validation and lifecycle invariants as mandatory analyzer concerns.

### 3. Effects: Expressive but Decoupled

- Maintain expressive effects while keeping the core model independent of effect internals.
- Prefer a small canonical set:
  - Assign
  - Create
  - Delete
  - PublishEvent
  - TransitionStage
- Composition should stay declarative and analyzable.

### 4. Relationships as Properties

- Default representation is typed reference/collection properties with metadata.
- Use separate relationship objects only when advanced semantics truly require it.

### 5. Intent and Command Unification

- Keep command-style transactional execution.
- Move toward one unified mutation model where intent shape and executable command shape are closely aligned.
- Reduce duplicated translation layers where possible.

### 6. Cross-Entity Mutation Boundary

- Do not support direct cross-entity property mutation.
- Use explicit, well-named actions to perform required changes.
- Keep event subscriptions as a required future capability.

## Core Architecture

### Core Model

- Domain
- Primitive/value types
- Entity
- Actor
- Property
- Action and parameters
- Policy and rules
- Event type and subscriptions
- Effect graph

### Mutation Pipeline

- Interface request (JSON, DSL, future visual tooling)
- Mutation intent(s)
- Transactional mutation command execution (apply or rollback)
- Analysis/validation
- Committed domain state

### Projection Interfaces

- JSON for machine-facing MCP integration.
- DSL as a human-facing authoring and review format.
- Future visual authoring can target the same mutation pipeline.

## DSL Positioning

The DSL is one interface over the domain system:

- DSL text -> mutation intents -> transactional execution -> committed domain
- committed domain -> canonical projection -> DSL text

Use DSL for readability, review, and LLM interaction. Do not let DSL concerns drive core model design.

## Preserved DSL Workstream

The prior DSL specification work is preserved and remains strategically important.

This system spec intentionally moved DSL details out of the top-level architecture narrative, but it does not de-scope or invalidate that work.

### Retained DSL Decisions

1. The DSL remains the preferred human-facing authoring and review format.
2. JSON remains the machine-facing MCP protocol.
3. DSL round-tripping remains a core requirement.
4. Canonical printing, deterministic ordering, and idempotent formatting remain required.
5. Comments and annotation support remain in scope.
6. Versioned DSL evolution remains required.

### Retained DSL Coverage Targets

- primitives and constraints
- entities and actors
- inheritance
- properties
- actions and parameters
- events and event subscriptions
- relationships
- policies and rules
- actor identity metadata
- effect composition

### Retained DSL Delivery Strategy

1. Build and stabilize the simplified core model first.
2. Project that core model to a canonical DSL surface.
3. Keep parser and printer behavior aligned with transactional mutation semantics.
4. Add compatibility aliases only after canonical syntax is stable.

The DSL is therefore a protected future workstream that follows core simplification, not a discarded concept.

## Canonical Behavior Requirements

1. Deterministic ordering in exported representations.
2. Idempotent round-trips where applicable.
3. Stable naming and alias normalization.
4. Diagnostics grounded in domain semantics.

## MCP Integration Direction

The MCP surface should remain JSON-native while reflecting the simplified system model.

Potential system-level operations:

1. ExportDomainState(sessionId)
2. ImportDomainState(payload, sessionId?)
3. ExportDomainDsl(sessionId)
4. ImportDomainDsl(text, sessionId?)

## Implementation Strategy

### Delivery Model

Run the refactor as parallel workstreams with strict integration gates.

1. No interface workstream can finalize before core model contracts are stable.
2. Every workstream must ship with migration adapters and analyzer updates.
3. Each phase closes only when acceptance criteria are met and regression tests pass.

### Workstream A: Actor-First Core Model

Scope:
1. Promote actor semantics to first-class model contracts.
2. Remove ambiguity between Entity and Actor behavior.

Implementation steps:
1. Define explicit actor invariants: identity property, role claim source, claim mappings.
2. Separate actor-specific diagnostics from generic entity diagnostics.
3. Update mutation intents and commands so actor operations are direct and explicit.
4. Add import/export compatibility for existing actor metadata shapes.

Acceptance criteria:
1. Actor identity and claim rules validate at analysis time with deterministic diagnostics.
2. Actor operations require no implicit entity-only fallbacks.
3. Existing actor-focused tests pass with compatibility adapters enabled.

### Workstream B: Lifecycle Model Refactor

Scope:
1. Introduce a lifecycle-first model where state is explicit and analyzable.
2. Use enum-based state representation only as an implementation detail.
3. Preserve parent-child lifecycle semantics through mapping.

Implementation steps:
1. Define canonical lifecycle model: LifecycleState, LifecycleTransition, LifecycleGuard, LifecycleRequirement.
2. Introduce enum declaration model generated from lifecycle states per entity hierarchy.
3. Add parent-lifecycle-category mapping for child lifecycle state values.
4. Replace stage-attached policy evaluation with lifecycle-state-set evaluation.
5. Add analyzer rules for:
  - unreachable states
  - missing transition guards
  - invalid terminal transitions
  - required-property-at-state violations
6. Translate legacy stage structures through a migration adapter into canonical lifecycle model.
7. Update lowering and runtime interpretation to execute lifecycle transitions through canonical lifecycle contracts.

Acceptance criteria:
1. Parent-lifecycle policy checks correctly evaluate mapped child-state sets.
2. Lifecycle transitions compile and analyze through lifecycle contracts, not ad hoc state checks.
3. Analyzer catches invalid lifecycle graphs before lowering.
4. Legacy stage models import and project without semantic loss.
5. Runtime transition execution cannot bypass lifecycle guards.

### Workstream C: Effects Simplification

Scope:
1. Keep effect power while reducing effect model sprawl.
2. Lock to canonical effect categories.

Implementation steps:
1. Define canonical effect taxonomy and deprecate overlapping variants.
2. Standardize effect composition contracts and output binding rules.
3. Enforce no direct cross-entity property mutation in analyzers.
4. Add explicit guidance to model cross-entity behavior via actions and events.

Acceptance criteria:
1. All supported effects map to one canonical category.
2. Analyzer rejects disallowed cross-entity mutation patterns.
3. Existing scenarios can be expressed through action orchestration and subscriptions.

### Workstream D: Relationship-as-Property Model

Scope:
1. Make typed properties the default relationship expression.
2. Keep advanced relationship semantics optional.

Implementation steps:
1. Define navigation metadata on properties for ownership and cardinality hints.
2. Add adapter from legacy relationship objects to property representation.
3. Preserve advanced relationship handling only where required by explicit use cases.
4. Update analyzers to validate property-based relationship semantics.

Acceptance criteria:
1. Common relationship patterns are fully representable as properties.
2. Legacy relationship imports project to the new model deterministically.
3. Ownership and cardinality diagnostics remain precise.

### Workstream E: Intent and Command Unification

Scope:
1. Reduce duplication between declarative intents and executable commands.
2. Preserve transactional guarantees.

Implementation steps:
1. Define one canonical mutation schema with clear apply and rollback semantics.
2. Generate or derive executable command behavior from canonical mutation definitions.
3. Keep mutation traceability and affected-node reporting intact.
4. Remove redundant translation code paths after compatibility window.

Acceptance criteria:
1. Every mutation has one canonical definition and one transactional execution path.
2. Rollback behavior is deterministic and covered by tests.
3. Mutation traces remain as rich as current behavior.

### Workstream F: Event Subscription Continuity

Scope:
1. Preserve event subscription capability during simplification.
2. Keep subscription routing compatible with actor-first and stage-enum semantics.

Implementation steps:
1. Keep subscription model in core contracts while refactoring adjacent areas.
2. Validate routing and correlation semantics after stage and relationship changes.
3. Ensure effect and action models still publish and consume events coherently.

Acceptance criteria:
1. Existing subscription flows continue to analyze and execute.
2. Correlation and audience semantics remain explicit and validated.
3. No subscription regressions across migration fixtures.

### Migration and Compatibility Plan

Phase order:
1. Stabilize new core contracts behind compatibility adapters.
2. Migrate internal analyzers and lowerers.
3. Migrate JSON projections.
4. Migrate DSL projection.
5. Remove deprecated paths after two stable milestones.

Compatibility requirements:
1. Importers must accept legacy model shapes during migration window.
2. Exporters must support canonical new shape and optional legacy compatibility mode.
3. Analyzer messages must identify when compatibility translation occurred.

### Verification Plan

Test layers:
1. Unit tests for each workstream contract.
2. Analyzer regression suite for diagnostics and invariants.
3. Transactional mutation tests for apply and rollback parity.
4. Integration tests for JSON and DSL round-trip behavior.
5. Scenario tests for actor identity, stage mapping, effects, and subscriptions.

Quality gates per milestone:
1. No new analyzer severity regressions without approved diagnostics updates.
2. Round-trip stability for committed domain projections.
3. Transaction trace parity for migrated mutation paths.
4. Passing benchmark domains with no semantic drift.

### Rollout Plan

Milestone 1:
1. Ship actor-first contracts and stage enum model with adapters.
2. Freeze canonical mutation schema draft.

Milestone 2:
1. Ship effect and relationship simplifications.
2. Enable unified mutation execution path by default.

Milestone 3:
1. Ship aligned JSON and DSL projections.
2. Remove deprecated translation layers after compatibility confirmation.

### Risks and Mitigations

Risk: Semantic drift during adapter translation.
Mitigation: Golden fixtures plus analyzer diff assertions.

Risk: Stage mapping ambiguity in inheritance-heavy models.
Mitigation: Explicit mapping requirement and validation errors for unmapped child stages.

Risk: Oversimplifying lifecycle into enums loses critical domain semantics.
Mitigation: Keep lifecycle contracts first-class and use enums only as generated/state-representation artifacts.

Risk: Mutation unification weakens rollback guarantees.
Mitigation: Rollback contract tests on every mutation type before path cutover.

## Recommendation Summary

1. Treat this as a domain system simplification effort first.
2. Keep actors first-class and stages enum-based with parent-child mapping.
3. Keep effects expressive but constrained.
4. Model relationships as properties by default.
5. Unify intents and commands while preserving transactional safety.
6. Keep event subscriptions in scope for future capability.
7. Keep DSL as a supporting interface over the core system.
