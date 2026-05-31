# Poly Domain Export DSL Spec

## Status

Draft

## Problem

- loosely typed discriminator/value pairs drift easily
- the structure reflects storage concerns more than domain intent

We need a format that is easier to read, easier to diff, easier for LLMs to emit correctly, and still faithful to the transactional domain model.

## Goals

1. Preserve rich domain semantics, not just current DTO structure.
2. Support clean round-tripping.
3. Be human-readable and LLM-friendly.
4. Produce stable, line-oriented diffs.
5. Allow comments and future annotations.
6. Avoid adding core dependencies.
## Simplification and Refactoring Plan

### 1. Actor as a First-Class Citizen
- Actors will be modeled as a primary, first-rate concept in the domain model, not as a subtype or afterthought.
- Actor-specific features (identity, claims, roles) will be explicit and central.

### 2. Stages as Enum
- The current stage implementation (as a first-class object with policies/actions) will be replaced by a simple enum property on the entity.
- Stage transitions and policies will be expressed declaratively, referencing the enum value.

### 3. Effects: Expressive but Decoupled
- Effects must be expressive and composable, but should not introduce complexity or coupling that holds back the rest of the system.
- Focus on a small, powerful set of effect types (Assign, Create, Delete, PublishEvent, TransitionStage).

### 4. Relationships as Properties
- Relationships will be modeled as typed properties (references or collections) with metadata, not as separate objects unless advanced features are needed.

### 5. Command/Intent System Unification
- The command pattern is retained for transactional mutation support, but the intent and command systems will be unified or closely aligned to reduce duplication and complexity.

### 6. Cross-Entity Mutation
- The system will not support direct cross-entity property mutation. Instead, well-named actions should encapsulate all required mutations.
- Event subscriptions remain a future requirement and will be designed to fit this simplified model.

---

7. Fit naturally with the existing `DomainMutationIntent[]` architecture.

## Non-Goals

1. Replacing JSON as the MCP wire protocol.
2. Designing a general-purpose language outside Poly's domain.
3. Solving syntax highlighting or editor tooling in the first iteration.

## Recommendation

Adopt a **Poly DSL** as the human-facing import/export format, while keeping **JSON** as the machine-facing MCP protocol.

The core split should be:

- **DSL**: authoring, review, diffing, prompt generation, import/export files
- **JSON / intents**: internal MCP calls, automation, replay, compatibility surfaces

The DSL should parse to `DomainMutationIntent[]`, and export should be printable back from the committed domain model in a stable form.

## Proposed Architecture

### Parse path

`Poly DSL text -> parser -> DomainMutationIntent[] -> DomainMutationIntentEngine -> committed domain`

### Export path

`committed domain -> canonical printer -> Poly DSL text`

### Compatibility path

`MCP tools -> JSON arguments / JSON responses`

This keeps the current MCP ergonomics for machines while giving humans and LLMs a better representation.

## Why the DSL is the right fit

### Strengths

1. **Diffable**: one concept per line or block.
2. **Compact**: materially smaller than nested JSON.
3. **Commentable**: easy to support line and block comments.
4. **Semantic**: models domain intent directly.
5. **Architecturally aligned**: Poly already has syntax, analysis, lowering, and intent/replay infrastructure.
6. **LLM-friendly**: block syntax with named constructs is easier to produce accurately than verbose JSON DTOs.

### Main cost

The main cost is building and maintaining a parser and printer. That is acceptable because the result is a first-class domain artifact instead of a transport-shaped snapshot.

## Rejected or Secondary Options

### Intent log

Serialize `DomainMutationIntent[]` directly as an append-only text stream.

**Good for**:

- audit history
- deterministic replay
- compact machine persistence

**Weak for**:

- understanding final domain shape
- review ergonomics
- human editing

This is better as an internal persistence or debug format than as the primary exported representation.

### YAML

YAML improves comments and diffability over JSON, but it is still a data format, not a domain language. It also adds coercion ambiguity and likely pushes the system toward an external dependency.

### S-expressions

S-expressions are easy to parse and structurally clean, but they are less readable for most users and less friendly for large domain models.

### TypeSpec or IDL-adjacent syntax

IDL-like formats are useful inspiration, especially decorators/annotations, but they do not model Poly's domain semantics directly enough. Borrow syntax ideas, not the full format.

## DSL Shape

The DSL should be block-oriented, explicit, and keyword-led.

Illustrative direction:

```poly
domain ECommerce

value Currency {
  code: Text length(min 3)
  symbol: Text
  name: Text
}

value Money {
  amount: Decimal
  currency: Currency

  action Sum(Money other) -> Money {
    require currency equals other.currency
    yield this with { amount: amount + other.amount }
  }
}

value Email {
  value: Text length(min 5, max 254)
}

policy CustomerServiceEmployee {
  actor is Employee
  actor in role "Customer Service"
}

policy WarehouseEmployee {
  actor is Employee
  actor in role "Warehouse"
}

actor User {
  email: Email required
}

actor Employee : User {
  badgeNumber: Number required
}

actor Customer : User {
  orders: Order[]

  action CreateOrder() {
    yield create new order in orders
  }
}

record Order {
  permit read any { actor is this.customer, actor is Employee }
  permit create { actor is Customer }

  customer: owner Customer permit write once
  createdAt: Timestamp permit write once
  submittedAt: Timestamp required when Submitted
  shippedAt: Timestamp required when Fulfilled
  canceledAt: Timestamp required when Canceled
  total: Money

  action AddLineItem(price: Money) {
    assign total to total + price
  }

  action Submit {
    assign submittedAt to now
    transition to Submitted
  }

  action Ship {
    assign shippedAt to now
    transition to Fulfilled
  }

  action Cancel {
    assign canceledAt to now
    transition to Canceled
  }

  stage Draft {
    permit action AddLineItem { actor = this.customer }
    permit action Submit      { actor = this.customer }
    permit action Cancel      { any { actor = this.customer, CustomerServiceEmployee } }
  }

  stage Submitted {
    require submittedAt

    permit action AddLineItem { CustomerServiceEmployee }
    permit action Ship        { WarehouseEmployee }
    permit action Cancel      { any { actor = this.customer, CustomerServiceEmployee } }
  }

  stage Fulfilled {
    require shippedAt
  }

  stage Canceled {
  }
}
```

## Syntax Principles

1. Prefer keywords over punctuation tricks.
2. Make optional semantics explicit.
3. Keep blocks shallow where possible.
4. Preserve stable ordering in printed output.
5. Support comments from the start.
6. Use canonical names in export output even if import accepts aliases.

## Semantic Coverage Requirements

The DSL must eventually cover the real domain surface, not just the current export subset:

- primitives and constraints
- entities and actors
- inheritance
- properties
- actions and parameters
- events and event subscriptions
- relationships
- stages
- policies and rules
- actor identity configuration
- effect composition and event publishing
- comments and annotations where appropriate

## Canonical Printing Rules

Export output should be stable. At minimum:

1. sort sibling declarations deterministically
2. print in canonical keyword order
3. normalize aliases to one preferred spelling
4. omit redundant defaults where that improves readability
5. preserve comments only when provenance is clear and deterministic

The printer should aim for idempotence:

`parse -> print -> parse -> print`

should converge quickly to a stable representation.

## Versioning

The DSL should carry an explicit version once it leaves draft state.

Examples:

- file header directive
- top-level `format` declaration
- versioned parser mode

This is important because the domain surface is still evolving quickly.

## MCP Integration

The MCP layer should eventually expose:

1. `ExportDomainDsl(sessionId)` -> returns canonical DSL text
2. `ImportDomainDsl(text, sessionId?)` -> parses and applies the DSL

JSON import/export can remain available for compatibility and programmatic automation, but the DSL should become the preferred human-facing format.

## Implementation Strategy

### Phase 1

1. Define the minimal grammar for:
   - domain
   - primitive
   - entity
   - property
   - action
   - event
   - relationship
2. Build a parser that emits `DomainMutationIntent[]`.
3. Build a canonical printer from the committed domain model.
4. Round-trip those supported constructs.

### Phase 2

Add:

- stages
- subscriptions
- policies
- rules
- actor identity metadata
- richer effects

### Phase 3

Add:

- comments/annotations
- compatibility aliases
- migration/versioning support

## Recommendation Summary

The recommended path is:

1. **Keep JSON for machine-facing MCP calls.**
2. **Introduce a Poly DSL for human-facing import/export.**
3. **Parse DSL into `DomainMutationIntent[]`.**
4. **Print DSL canonically from the committed domain model.**

That gives Poly a format that is semantic, compact, reviewable, and consistent with the transactional architecture already in place.


