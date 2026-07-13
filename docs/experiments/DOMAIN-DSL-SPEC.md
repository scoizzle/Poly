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

### 2. Stages as Declarative Lifecycle Nodes
- Stages are first-class lifecycle nodes that form a directed graph (not a linear pipeline). Cyclical transitions (e.g., `Suspended -> Active` via `Reinstate`) are valid and common.
- Stage transitions and policies will be expressed declaratively, with transitions driven by actions rather than implicit ordering.
- The DSL declares stages as an ordered list; transition edges are expressed on actions (`action Name -> Target`).

### 3. Effects: Expressive but Decoupled
- Effects must be expressive and composable, but should not introduce complexity or coupling that holds back the rest of the system.
- Focus on a small, powerful set of effect types (Assign, Create, Delete, PublishEvent, TransitionStage).

### 4. Relationships as Properties
- Relationships are entity-typed properties: `orders: many owned Order`. Cardinality is expressed through `one`/`many` modifiers; ownership through optional `owned`.
- The owning side declares the edge. The domain engine synthesizes an implicit reverse navigation property on the owned entity — no separate back-reference declaration is required.
- The MCP's `add_relationship` tool and `sourceOwnsTarget` boolean are replaced by a single property line.

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

### Syntax Philosophy: `Name: Kind`

Every declaration in the Poly DSL follows a single uniform pattern: **`Name: Kind Details`**. The kind keyword tells the parser and reader what the declaration *is* in the domain. Properties are the unmarked default — a bare type name implies a property. All other concepts use an explicit kind keyword.

| Syntax | Kind | Example |
|---|---|---|
| `Name: Type mod...` (bare type + modifiers) | Property or Relationship | `SKU: Text length(1, 50)` |
| `Name: stage { ... }` | Stage | `Active: stage { ... }` |
| `Name: action -> Target` | Action with transition | `Submit: action -> Confirmed` |
| `Name: policy expr` | Policy guard | `HasStock: policy Qty > 0` |
| `Name: actor { ... }` | Actor (specialized entity) | `Customer: actor { ... }` |

This pattern has several advantages:

- **Relationships are properties** — when a property's type is another entity, it is a relationship. Cardinality and ownership are expressed through `one`/`many` and `owned` modifiers: `orders: many owned Order`, `supplier: one Supplier`. No separate `relationship` keyword or declaration block.
- **Implicit reverse navigation** — when an entity is `owned` by another, the domain engine synthesizes a reverse navigation property on the owned entity. The modeler only declares the owning side.
- **Zero ceremony for properties** — the 80% case is `name: Type`, with optional inline constraint modifiers (`range`, `length`, `pattern`) chained directly on the type line. No keyword, no indent.
- **Self-describing syntax** — the kind keyword disambiguates at a glance, without consulting docs.
- **Extensible** — new domain concepts (aggregate, projection, process manager) adopt the same pattern with new kind keywords, no grammar changes needed.
- **LLM-friendly** — a uniform declaration pattern is easier for LLMs to emit correctly than position-dependent or context-sensitive syntax.
- **Actor is an entity** — `Customer: actor { ... }` inherits all entity capabilities (properties, stages, actions, policies) plus actor-specific features (identity, claims, roles). The kind keyword is the specialization mechanism.

Stages are **cyclical** by design. The DSL does not use a linear pipeline notation. Instead, transitions are expressed on actions:

```
Active: stage {
  Suspend: action -> Suspended
}
Suspended: stage {
  Reinstate: action -> Active    // backward transition = cycle
  Blacklist: action -> Blacklisted
}
```

### Relationship Syntax

Relationships are properties whose type is another entity. Cardinality and ownership are expressed inline:

```
name: many owned Order         // one-to-many, source owns target
name: many Order               // one-to-many, no ownership
name: one owned Supplier       // one-to-one, source owns target
name: one Supplier             // one-to-one, no ownership
```

**Ownership** (`owned`) means the source entity owns the target: deleting the source cascades to the target. The domain engine synthesizes an implicit reverse navigation property on the owned entity. A modeler may optionally name the reverse side for documentation, but the parser detects that it matches an existing `owned` edge and treats it as an alias, not a second relationship.

**Cardinality** is handled by `one` (reference) vs `many` (collection). The parser infers the full cardinality type from these keywords — no separate enumeration is needed.

This replaces the MCP's separate `add_relationship` tool and `sourceOwnsTarget` boolean with a single line that reads naturally.

### Phase 1 Example: Supply Chain Domain

A concrete example of the minimal Phase 1 surface, derived from a real MCP session:

```poly
domain SupplyChain

entity Product {
  SKU: Text
  Name: Text
  UnitCost: Number range(0, )
  MSRP: Number range(0, )
  ReorderPoint: Number
  IsHazardous: Boolean
  WeightKg: Number

  suppliers: many Supplier
  category: one Category
  inventory: many owned InventoryItem

  Draft: stage {
    Activate: action -> Active
  }
  Active: stage {
    UpdatePricing: action
    Discontinue: action -> Discontinued
  }
  Discontinued: stage {
    Archive: action -> Archived
  }
  Archived: stage {}
}

entity Supplier {
  SupplierCode: Text
  Name: Text
  ContactEmail: Text pattern("[^@]+@[^@]+")
  LeadTimeDays: Number range(0, )
  Rating: Number range(0, 5)
  CountryOfOrigin: Text
  IsActive: Boolean

  products: many Product
  orders: many owned PurchaseOrder

  Prospective: stage {
    Approve: action -> Approved
  }
  Approved: stage {
    Activate: action -> Active
  }
  Active: stage {
    Suspend: action -> Suspended
  }
  Suspended: stage {
    Reinstate: action -> Active
    Blacklist: action -> Blacklisted
  }
  Blacklisted: stage {}

  IsActiveSupplier: policy IsActive == true
  HighRatedSupplier: policy Rating >= 4
}

entity Warehouse {
  WarehouseCode: Text
  Name: Text
  Address: Text
  CapacityCubicMeters: Number
  IsTemperatureControlled: Boolean

  items: many InventoryItem
  servedStores: many Store

  Planned: stage {
    Open: action -> Operational
  }
  Operational: stage {
    ScheduleMaintenance: action -> Maintenance
    Decommission: action -> Decommissioned
  }
  Maintenance: stage {
    Reopen: action -> Operational
  }
  Decommissioned: stage {}
}

entity PurchaseOrder {
  OrderNumber: Text
  OrderDate: DateTime
  ExpectedDeliveryDate: Date
  TotalCost: Number range(0, )
  CurrencyCode: Text

  shipments: many owned Shipment

  Draft: stage {
    Submit: action -> Submitted
  }
  Submitted: stage {
    Confirm: action -> Confirmed
    Cancel: action -> Cancelled
  }
  Confirmed: stage {
    Ship: action -> Shipped
  }
  Shipped: stage {
    Receive: action -> Received
  }
  Received: stage {
    Return: action -> Returned
  }
  Cancelled: stage {}
  Returned: stage {}
}

entity Shipment {
  TrackingNumber: Text
  EstimatedArrivalDate: Date
  ActualArrivalDate: Date
  ShippingMethod: Text

  Planned: stage {
    Dispatch: action -> InTransit
  }
  InTransit: stage {
    MarkDelivered: action -> Delivered
  }
  Delivered: stage {
    Verify: action -> Verified
  }
  Verified: stage {}
}

entity InventoryItem {
  BatchNumber: Text
  QuantityOnHand: Number range(0, )
  QuantityReserved: Number range(0, )
  ExpiryDate: Date
  BinLocation: Text

  Available: stage {
    Reserve: action -> Reserved
    MarkDamaged: action -> Damaged
    MarkExpired: action -> Expired
  }
  Reserved: stage {}
  Damaged: stage {}
  Expired: stage {}
  Depleted: stage {}

  HasStock: policy QuantityOnHand > 0
  HasMinimumStock: policy QuantityOnHand >= 10
  IsLowStock: policy QuantityOnHand < 5
}

entity Store {
  StoreCode: Text
  Name: Text
  Address: Text
  Region: Text
  Format: Text

  warehouses: many Warehouse

  Planned: stage {
    Open: action -> Active
  }
  Active: stage {
    Close: action -> Closed
    Relocate: action -> Relocated
  }
  Closed: stage {}
  Relocated: stage {}
}

entity Category {
  CategoryCode: Text
  Name: Text
  Description: Text

  products: many Product

  Active: stage {
    Archive: action -> Archived
  }
  Archived: stage {}
}

```

### Phase 2+ Example: E-Commerce with Actors & Permits

The aspirational surface covering actors, permits, richer effects, and value types:

```poly
domain ECommerce

value Currency {
  code: Text length(3, 3)
  symbol: Text
  name: Text
}

value Money {
  amount: Number range(0, )
  currency: Currency
}

actor User {
  email: Text pattern("[^@]+@[^@]+") length(5, 254)
}

actor Employee : User {
  badgeNumber: Text
}

actor Customer : User {
  orders: many owned Order

  Active: stage {
    CreateOrder: action {
      yield create new order in orders
    }
  }
}

entity Order {
  customer: one Customer
    constraint write once
  createdAt: DateTime
    constraint write once
  submittedAt: DateTime
    constraint required when Submitted
  shippedAt: DateTime
    constraint required when Shipped
  total: Money

  Draft: stage {
    AddLineItem: action(price: Money) {
      assign total to total + price
    }
      permit { CustomerServiceEmployee }

    Submit: action -> Submitted {
      assign submittedAt to now
    }
      permit { this.customer, CustomerServiceEmployee }

    Cancel: action -> Cancelled {}
      permit { this.customer, CustomerServiceEmployee }
  }

  Submitted: stage {
    AddLineItem: action(price: Money) {
      assign total to total + price
    }
      permit { CustomerServiceEmployee }

    Ship: action -> Shipped {
      assign shippedAt to now
    }
      permit { WarehouseEmployee }

    Cancel: action -> Cancelled {
      assign canceledAt to now
    }
      permit { this.customer, CustomerServiceEmployee }
  }

  Shipped: stage {}
  Cancelled: stage {}
}

policy CustomerServiceEmployee {
  actor is Employee
  actor in role "Customer Service"
}

policy WarehouseEmployee {
  actor is Employee
  actor in role "Warehouse"
}
```

## Syntax Principles

1. Prefer keywords over punctuation tricks.
2. Target natural language fragments — the DSL should read like domain assertions: `orders: many owned Order`, `QuantityOnHand > 0`.
3. Make optional semantics explicit.
4. Keep blocks shallow where possible.
5. Preserve stable ordering in printed output.
6. Support comments from the start.
7. Use canonical names in export output even if import accepts aliases.

## Semantic Coverage Requirements

The DSL must eventually cover the real domain surface. Priority is ordered by Phase:

- **Phase 1:** entities, properties (bare type with inline constraint modifiers), relationships as entity-typed properties (`one`/`many`, optional `owned`, implicit reverse navigation), stages (cyclical graph), actions with stage transitions, policies (property comparison and composite boolean)
- **Phase 2:** actors (specialized entities with identity/claims/roles), action parameters and effect blocks, event declarations, richer effects
- **Phase 3:** comments and annotations, `permit` blocks (actor/RBAC guards), migration/versioning support

## Canonical Printing Rules

Export output should be stable. At minimum:

1. sort sibling declarations deterministically
2. group declarations by kind within entities: properties first, then stages, then policies
3. print stages in declaration order (preserves canonical stage ordering)
4. print in canonical keyword order
5. normalize aliases to one preferred spelling
6. omit redundant defaults where that improves readability
7. preserve comments only when provenance is clear and deterministic

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

**Error handling for `ImportDomainDsl`:** Parsing applies atomically — either the entire DSL parses and commits successfully, or no changes are applied. This avoids the partial-apply problem observed with individual MCP tool calls (where parallel mutations can silently roll back, leaving the domain in an indeterminate state). Parse errors should include line numbers and the specific construct that failed.

### Partial Import (Incremental DSL)

`ImportDomainDsl` accepts either a full domain definition or a partial fragment. A partial fragment contains one or more entity declarations (optionally preceded by a `domain` header) and is **additive-only**:

- New entities and their contents are added to the existing session.
- Re-declaring something that already exists identically is a **no-op** (idempotent).
- Re-declaring something with different details produces an **error** with the conflicting line number.
- Partial import does **not** support deletion — there is no implicit removal of undented entities.

```
// Applying this fragment to an existing session adds one entity.
// The other 8 entities remain untouched. No deletions.
entity ReturnAuthorization {
  ReturnCode: Text
  Reason: Text
  order: one PurchaseOrder

  Draft: stage {
    Submit: action -> Submitted
  }
  Submitted: stage {
    Approve: action -> Approved
    Deny: action -> Denied
  }
  Approved: stage {}
  Denied: stage {}
}
```

This is safe by construction — applying a partial import can only add. Full domain definitions (all entities declared) can be used for greenfield or full reconciliation, but the additive-only constraint makes partial imports trustworthy for iterative evolution.

## DSL Affordances

Key design affordances the parser and printer must address, derived from real MCP session experience:

### 1. Idempotent Import (P0)

Re-declaring an existing construct identically is a no-op, not an error and not a duplicate. `ImportDomainDsl` is safe to re-run. The DSL describes **desired state**, not imperative mutations. Contrast with the MCP's imperative `add_*` tools where calling twice may produce duplicates.

### 2. Line-Level Error Reporting (P0)

Parse and commit errors must reference DSL line numbers, not internal `DomainMutationIntent` indices. "line 42: entity 'Order' not found" — not "mutation index 7 failed." This is critical for human debugging and LLM self-correction loops.

### 3. Reference Validation — All At Once (P1)

Cross-entity type references (`category: one Category`, `inventory: many owned InventoryItem`) are resolved at the end of the parse pass. Forward references are accepted. All unresolved references are reported together, not one at a time. This avoids whack-a-mole error fixing.

### 4. Whitespace Resilience (P1)

Indentation is cosmetic, not structural. The parser accepts 2-space, 4-space, or tab indentation within blocks. The canonical printer normalizes. This prevents "correct model, import failed due to a tab" scenarios, which are common with LLM-generated DSL text.

### 5. Version Pragma (P1)

The first non-comment line of a DSL file must be a version pragma:

```
# poly-dsl v1
```

or

```
domain SupplyChain format v1
```

The parser uses this to select the correct grammar. Without a pragma, evolving the DSL means either breaking existing files or building a heuristic version detector. The pragma is cheaper.

### 6. Comment Round-Trip (P2)

Comments survive `parse → print`. At minimum, structural comments attached to declarations:

```
entity Product {
  // Pricing fields
  UnitCost: Number range(0, )
  MSRP: Number range(0, )
}
```

If a comment's attachment point is deleted, the comment is dropped. If it survives, it round-trips. This makes the DSL a true source-of-truth artifact that can live in version control.

### 7. Policy Expression Validation at Parse Time (P2)

The parser detects type mismatches in policy expressions (e.g., comparing a `Text` property to a number literal) and reports them at parse time, before the intent engine runs. This catches the class of evaluation bugs discovered during MCP policy testing where operators like `>=` and `<` returned incorrect results for certain types.

### 8. Minimal Canonical Output (P3)

The printer emits only what exists. If an entity has no policies, no empty `policies` block or trailing whitespace is emitted. This keeps diff noise low — adding a policy adds exactly one line, not structural reformatting.

### 9. Naming Rules (P3)

- Entity names must be unique across the domain.
- Stage names must be unique within their entity.
- Property names must be unique within their entity.
- Action names must be unique within their stage.
- Policy names must be unique within their entity.

These are documented constraints, not parser enforcement in Phase 1. Formal validation is added in Phase 2.

## Implementation Strategy

### Phase 1

1. Define the minimal grammar for:
   - domain
   - entity
   - property (bare type: `Name: Type`) — when the type is another entity, it is a relationship
   - relationship modifiers inline on properties (`one`/`many`, optional `owned`)
   - inline constraint modifiers on properties (`range`, `length`, `pattern`)
   - stage (`Name: stage { ... }`)
   - action with stage transition (`Name: action -> Target`)
   - policy — property comparison and composite boolean (`Name: policy expr`)
2. Build a parser that emits `DomainMutationIntent[]`.
3. Build a canonical printer from the committed domain model.
4. Round-trip those supported constructs.
5. Expose `ExportDomainDsl(sessionId)` and `ImportDomainDsl(text, sessionId?)` as MCP tools.

### Phase 2

Add:

- actor specialization (`Name: actor { ... }`) — inherits entity grammar plus identity, claims, roles
- action parameters and effect blocks (`assign`, `yield create`)
- event declarations and subscriptions
- actor identity configuration
- richer effects (Create, PublishEvent, TransitionStage)

### Phase 3

Add:

- comments/annotations
- compatibility aliases
- migration/versioning support
- `permit` blocks on actions (actor/RBAC guards)

## Recommendation Summary

The recommended path is:

1. **Keep JSON for machine-facing MCP calls.**
2. **Introduce a Poly DSL for human-facing import/export.**
3. **Parse DSL into `DomainMutationIntent[]`.**
4. **Print DSL canonically from the committed domain model.**

That gives Poly a format that is semantic, compact, reviewable, and consistent with the transactional architecture already in place.


