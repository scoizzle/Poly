# Phase 4 Design: Cross-Entity Mutation & Dynamic Calculation

**Date**: 2026-06
**Status**: **SUPERSEDED** — See `docs/decisions/2026-06-phase4-dynamic-calculation-and-readonly-navigation.md`
**Related**: WS7 V3 Expressiveness Audit, Library roadblocks, `docs/decisions/2026-05-31-immutable-core-domain-modeling.md`

This document proposed cross-entity mutation via `AssignEffect` + `RelationshipNavigation`. The design was superseded because cross-entity state changes are better handled through the event/subscription pattern, preserving ownership boundaries and the action-as-single-path invariant. `RelationshipNavigation` is retained as a **read-only** expression for policy rules and effect value computations. The new design retains all dynamic calculation proposals (Add, Multiply, Divide, DateOperation).

## Problem

The Library domain proof tests (WS5) identified concrete roadblocks that cannot be expressed in current V3. These are the forcing functions for Phase 4 per the immutable-core decision:

1. **CheckoutBook / ReturnBook**: Loan action needs to modify `Book.AvailableCopies` (cross-entity property mutation).
2. **RenewLoan**: Action needs to compute `RenewalCount + 1` and `DueDate + 14 days` (dynamic calculation with arithmetic).
3. **ReportLost**: Action needs conditional logic plus multiple side effects.
4. **FulfillReservation**: Action needs to invoke another action with parameter binding.

## Design Principles

- **Minimal surface increase**: Add only what the forcing functions require. Favor general patterns over ad-hoc workarounds.
- **Composable with existing types**: New effect/expression types should work with `PropertyBinding`, `DomainExpression`, and the existing `AddEffectToActionChange` / `AddOnEntryEffectToStageChange` evolution layer.
- **Analysis-gated**: New types must participate in the `Node` tree and be analyzable by the existing `PolicyConstraintAnalyzer` / `DomainModelAnalyzer`.
- **Backward-compatible**: Existing domains (PersonLifecycle, Library MVP) must continue to pass without changes.

## Proposal 1: Cross-Entity Mutation

### Approach

Augment the effect system with two additions:

**A. `RelationshipNavigation` expression** — A new `DomainExpression` subtype that navigates from the current entity through a named relationship to reach a property on a related entity.

```
RelationshipNavigation(relationshipName, targetEntityPropertyName)
```

Semantics: "Starting from the current entity, follow relationship `X`, reference property `Y` on the target entity."

This avoids a dedicated `CrossEntityMutation` effect type. Instead, any effect that accepts a `DomainExpression` (e.g., a future `AssignEffect` or existing `PropertyBinding`) can reference cross-entity values.

**B. `AssignEffect`** — A new effect type that assigns a computed `DomainExpression` value to a target property. The target is a `DomainExpression` (either a simple `Property("Name")` for same-entity or `RelationshipNavigation(...)` for cross-entity).

```csharp
public sealed record AssignEffect(
    DomainExpression Target,
    DomainExpression Value
) : Effect;
```

### Rationale

- RelationshipNavigation is composable with all existing expression consumers (PropertyBinding, policy guards, etc.)
- AssignEffect is general: it handles both same-entity mutation (via `Property("Name")`) and cross-entity mutation (via `RelationshipNavigation(...)`)
- No new `CrossEntityMutation` concept needed — it falls out naturally from the expression system
- Follows the existing pattern: `PropertyBinding` uses `DomainExpression` for values; `AssignEffect` extends that to target properties

### Limitations (Explicit)

- Single relationship hop only (e.g., `Loan → Book`, not `Loan → Book → Author`)
- No filtering across OneToMany (targets the "one" side of a relationship)
- No transactional guarantees across entity boundaries (each AssignEffect is a single operation on the action's entity context)
- These limitations are acceptable for the forcing functions and can be relaxed later

## Proposal 2: Dynamic Calculation

### Approach

Add three new `DomainExpression` subtypes to complement the existing `Subtract`:

```csharp
// Arithmetic: Add, Subtract, Multiply, Divide
public sealed record Add(DomainExpression Left, DomainExpression Right) : DomainExpression;
public sealed record Subtract(DomainExpression Left, DomainExpression Right) : DomainExpression; // existing
public sealed record Multiply(DomainExpression Left, DomainExpression Right) : DomainExpression;
public sealed record Divide(DomainExpression Left, DomainExpression Right) : DomainExpression;
```

For date arithmetic, a `DateOperation` expression:

```csharp
public sealed record DateOperation(
    DomainExpression Date,
    DomainExpression Offset,
    DateOperationKind Kind  // AddDays, AddMonths, DiffDays
) : DomainExpression;
```

### Rationale

- `Add` is the minimal addition to unblock `RenewalCount + 1`
- `Multiply`/`Divide` are included for symmetry and anticipated Phase 4 use cases
- `DateOperation` handles the date-arithmetic forcing function (`DueDate + 14 days`)
- All follow the existing `Subtract` pattern (two operands, no surprises)
- Lowering handles type checking: `Subtract` on a date is not valid; `DateOperation.AddDays` on a count is not valid

## Proposal 3: Conditional Effect (Lower Priority for Initial Phase 4)

For `ReportLost`-style scenarios, a conditional effect that gates a list of child effects:

```csharp
public sealed record ConditionalEffect(
    DomainExpression Condition,
    IReadOnlyList<Effect> ThenEffects,
    IReadOnlyList<Effect>? ElseEffects
) : Effect;
```

Deferred to later in Phase 4 — `ReportLost` can be approximated with a policy guard and multiple sequential actions.

## Proposal 4: InvokeAction Enhancement (Lower Priority)

Improve `InvokeAction` parameter binding by adding a simple map from parameter name to expression:

```csharp
public sealed record InvokeActionEffect(
    string TargetEntity,
    string TargetAction,
    IReadOnlyList<PropertyBinding> ParameterBindings
) : Effect;
```

Leverages the existing `PropertyBinding` type. Deferred to later in Phase 4.

## Implementation Checklist (Priority Order)

### Phase 4a — Dynamic Calculation (RenewLoan unblocker)
- [ ] Add `DomainExpression.Add` record (mirroring `Subtract`)
- [ ] Add `DomainExpression.Multiply`, `DomainExpression.Divide` records
- [ ] Add `DomainExpression.DateOperation` record with `DateOperationKind` enum
- [ ] Add lowering for new expression types in `DomainExpressionLoweringPass` (Phase 2 WS8)
- [ ] Update `AddEffectToActionChange` to accept `AssignEffect` if needed (it already accepts any `Effect`)
- [ ] Add/update builder methods on `EvolutionBuilder`
- [ ] Tests: RenewLoan proof via evolution layer

### Phase 4b — Cross-Entity Mutation (CheckoutBook/ReturnBook unblocker)
- [ ] Add `DomainExpression.RelationshipNavigation` record
- [ ] Add `AssignEffect` record in `Poly/DomainModeling/Effects/`
- [ ] Add `AddAssignEffectToActionChange` (or reuse `AddEffectToActionChange` with `AssignEffect`)
- [ ] Add lowering support for `RelationshipNavigation` and `AssignEffect`
- [ ] Add/update builder methods on `EvolutionBuilder`
- [ ] Tests: CheckoutBook/ReturnBook via evolution layer

### Phase 4c — Conditional + InvokeAction (ReportLost/FulfillReservation)
- [ ] Add `ConditionalEffect` record
- [ ] Add `InvokeActionEffect` record with `PropertyBinding` parameter bindings
- [ ] DomainChange support (reuses existing `AddEffectToActionChange`)
- [ ] Lowering + tests
- [ ] Tests: Full Library Loan Lifecycle with all scenarios

### Phase 4d — Library Domain Full Proof
- [ ] Extended `LibraryDomain_LoanLifecycle_ProvenViaEvolutionLayer` with CheckoutBook, ReturnBook, RenewLoan, ReportLost, FulfillReservation

## Risk Mitigation

- **Cross-entity side effects violate the single-entity mutation boundary** — The evolution layer's atomicity is per-action, not per-entity. Cross-entity effects are applied as part of the action that triggers them; if analysis rejects, the entire action's changes are rolled back. This is acceptable for Phase 4 because the domain model bears the ownership relationships explicitly.
- **Dynamic calculation type safety** — The analyzer must verify that arithmetic operands are numeric and date operations receive dates. This is a natural extension of the existing `PolicyConstraintAnalyzer` or a new dedicated pass.
- **Relationship not found at evolution time** — The relationship name in `RelationshipNavigation` must refer to an existing named relationship in the domain. Analysis should catch this.

## Appendix: Reference Pattern — Existing Effect Types

```csharp
public abstract record Effect(InvocationResult? Result = null) : DomainObject;

public sealed record AssignEffect(
    DomainExpression Target,
    DomainExpression Value
) : Effect;

public sealed record ConditionalEffect(
    DomainExpression Condition,
    IReadOnlyList<Effect> ThenEffects,
    IReadOnlyList<Effect>? ElseEffects
) : Effect;

public sealed record InvokeActionEffect(
    string TargetEntity,
    string TargetAction,
    IReadOnlyList<PropertyBinding> ParameterBindings
) : Effect;
```
