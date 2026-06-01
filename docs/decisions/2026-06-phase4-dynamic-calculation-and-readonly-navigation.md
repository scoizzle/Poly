# Phase 4 Design: Dynamic Calculation & Read-Only Relationship Navigation

**Date**: 2026-06
**Status**: Design Proposal — Ready for Implementation Planning
**Related**: WS7 V3 Expressiveness Audit, Library roadblocks, `docs/decisions/2026-05-31-immutable-core-domain-modeling.md`, `docs/decisions/2026-06-phase4-cross-entity-mutation-and-dynamic-calculation.md` (superseded)

## Problem

Library domain proof tests (WS5) identified forcing functions for Phase 4:

1. **RenewLoan**: Action needs to compute `RenewalCount + 1` and `DueDate + 14 days` (dynamic calculation with arithmetic).
2. **Cross-entity policy rules**: e.g., "CanCloseProject" policy needs `Project.CloseDate >= max of related Tasks' DueDate` — requires read-only relationship traversal.

Cross-entity mutation (e.g., `CheckoutBook` modifying `Book.AvailableCopies`) is deliberately excluded. The event/subscription pattern handles cross-entity workflows: entity A emits an event, entity B handles it via its own action, owning its state change.

## Design Principles

- **Minimal surface increase**: Add only what the forcing functions require.
- **Composable with existing types**: New expression types work with `PropertyBinding`, `DomainExpression`, and existing evolution layer.
- **Analysis-gated**: New types must participate in the `Node` tree and be analyzable.
- **Read-only navigation only**: `RelationshipNavigation` evaluates to a value for use in expressions and policy rules. It is not a mutation target.

## Proposal 1: Dynamic Calculation

Add three new `DomainExpression` subtypes to complement the existing `Subtract`:

```csharp
public sealed record Add(DomainExpression Left, DomainExpression Right) : DomainExpression;
public sealed record Subtract(DomainExpression Left, DomainExpression Right) : DomainExpression; // existing
public sealed record Multiply(DomainExpression Left, DomainExpression Right) : DomainExpression;
public sealed record Divide(DomainExpression Left, DomainExpression Right) : DomainExpression;
```

For date arithmetic:

```csharp
public sealed record DateOperation(
    DomainExpression Date,
    DomainExpression Offset,
    DateOperationKind Kind  // AddDays, AddMonths, DiffDays
) : DomainExpression;
```

### Rationale

- `Add` is the minimal addition to unblock `RenewalCount + 1`.
- `Multiply`/`Divide` are included for symmetry and anticipated use.
- `DateOperation` handles `DueDate + 14 days`.
- All follow the existing `Subtract` pattern (two operands).
- Lowering handles type checking: `Subtract` on a date is not valid; `DateOperation.AddDays` on a count is not valid.

## Proposal 2: Read-Only Relationship Navigation

A new `DomainExpression` subtype that navigates from the current entity through a named relationship to read a property on a related entity.

```csharp
public sealed record RelationshipNavigation(
    string RelationshipName,
    string TargetPropertyName
) : DomainExpression;
```

### Semantics

Starting from the current entity, follow relationship `RelationshipName`, read property `TargetPropertyName` on the target. Used in policy rule conditions and effect value computations — never as an assignment target.

### Rationale

- Enables cross-entity policy rules without cross-entity mutation.
- Composable with all existing expression consumers: `PropertyBinding`, `AssignEffect` value side, policy condition expressions.
- Single hop only (no chaining `Loan → Book → Author`).
- No filtering across `OneToMany` (targets the "one" cardinality side).

### What This Is Not

`RelationshipNavigation` is **read-only**. It evaluates to a value. It is not a mutation target. Cross-entity state changes are handled by the event/subscription pattern: entity A publishes an event, entity B subscribes and mutates its own state via its own action. This preserves ownership boundaries and the action-as-single-path invariant.

## Implementation Checklist

### Phase 4a — Dynamic Calculation (RenewLoan unblocker)
- [ ] Add `DomainExpression.Add` record (mirroring `Subtract`)
- [ ] Add `DomainExpression.Multiply`, `DomainExpression.Divide` records
- [ ] Add `DomainExpression.DateOperation` record with `DateOperationKind` enum
- [ ] Add lowering for new expression types in `DomainExpressionLoweringPass` (Phase 2 WS8)
- [ ] Add/update builder methods on `EvolutionBuilder`
- [ ] Tests: RenewLoan proof via evolution layer

### Phase 4b — Read-Only Relationship Navigation
- [ ] Add `DomainExpression.RelationshipNavigation` record
- [ ] Add lowering support for `RelationshipNavigation` in policy rule expressions
- [ ] Add/update builder methods on `EvolutionBuilder`
- [ ] Tests: cross-entity policy rule via evolution layer

## Risk Mitigation

- **Dynamic calculation type safety**: analyzer must verify arithmetic operands are numeric and date operations receive dates. Natural extension of existing analysis passes.
- **Relationship not found**: relationship name must refer to an existing named relationship. Analysis catches this.
- **Cross-entity side effects**: explicitly excluded. The event/subscription pattern provides clean ownership boundaries without implicit coupling.

## Supersedes

This design replaces `docs/decisions/2026-06-phase4-cross-entity-mutation-and-dynamic-calculation.md` (2026-06), which proposed cross-entity mutation via `AssignEffect` + `RelationshipNavigation`. Cross-entity state changes are now handled exclusively through the event/subscription pattern, preserving ownership boundaries and the action-as-single-path invariant.
