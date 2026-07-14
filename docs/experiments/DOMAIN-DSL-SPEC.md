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

### 1. Actor as Syntactic Sugar on Entity
- `actor` is not an engine primitive. It lowers to `entity` plus actor-specific metadata (identity columns, claims, role-based policy evaluation).
- `Name: actor { ... }` declares a standalone actor — the entity's properties form the identity surface.
- `Name: actor Parent { ... }` declares an actor that extends the parent actor's identity, inheriting its properties. `Employee: actor User` means "Employee is an actor that is a User."

### 2. Stages as Declarative Lifecycle Nodes
- Stages are first-class lifecycle nodes that form a directed graph (not a linear pipeline). Cyclical transitions (e.g., `Suspended -> Active` via `Reinstate`) are valid and common.
- Stage transitions and policies will be expressed declaratively, with transitions driven by actions rather than implicit ordering.
- The DSL declares stages as an ordered list; transition edges are expressed on actions (`transition to Target`).

### 3. Effects: Expressive but Decoupled
- Effects must be expressive and composable, but should not introduce complexity or coupling that holds back the rest of the system.
- Focus on a small, powerful set of effect types: `assign` (mutate a property), `return create` (produce and return a new entity), `transition to` (stage change), and `publish` (emit an event).

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

### Downstream Benefits (Remarks)

The DSL is not just a modeling format — it is a **machine-readable specification** that enables deterministic code generation across the full stack. These benefits are emergent: the DSL grammar does not encode them, but the committed domain IR makes them straightforward to derive.

**API generation from lowering passes.** Every backend protocol becomes a lowering pass that reads the committed domain IR and emits protocol-specific artifacts:

```
DSL → DomainMutationIntent[] → Committed Domain IR
                                      │
                    ┌─────────────────┼─────────────────┐
                    ▼                 ▼                  ▼
             REST lowering      gRPC lowering      GraphQL lowering
```

Each lowering pass translates the same IR into its target format. `SKU: Text required write once` becomes `required: true, readOnly: true` in OpenAPI — no per-protocol hand-rewriting.

**HATEOAS as an emergent property.** The stage machine *is* the link generator. An entity in stage `Submitted` exposes only the actions declared on that stage (or via `when`) as links. No hand-authored `if state == "Draft" then emit Submit link` logic. The API layer walks the current stage's outgoing action edges and emits `_links` for each. HATEOAS becomes trivial because the lifecycle graph is already encoded in the domain model.

**RBAC-constrained links.** When actions reference actor entity policies in their `require` clause (Phase 2+), the API layer filters `_links` by evaluating each referenced policy against the authenticated actor. A `PurchaseOrder` in `Submitted` returns different links for a warehouse worker (`Ship`) vs. a customer service rep (`Confirm`, `Cancel`, `AddLineItem`) vs. the ordering customer (`Cancel`). Role checks, HATEOAS links, and domain authorization stay in sync because they share a single source of truth: the DSL.

**Actor authorization through policies.** Policies are the single concept. There is no separate `permit policy` keyword. The engine infers evaluation context from where a policy is declared: a policy on an actor entity (`Employee`) evaluates against the actor's properties; a policy on a regular entity (`Order`) evaluates against the entity's properties. The reserved name `actor` refers to the authenticated actor within policy expressions, allowing entity policies to mix entity and actor references:

```
// Policy on an actor entity — evaluates against the actor
Warehouse: policy { role == "Warehouse" }

// Policy on a regular entity — evaluates against the entity
HasStock: policy { QuantityOnHand > 0 }

// Policy on a regular entity that references the actor via actor keyword
OwnedByCaller: policy { customer == actor }
```

Actions reference policies by name in `require` — qualified names for cross-entity references (`Employee.Warehouse`), unqualified for same-entity policies (`OwnedByCaller`, `HasStock`). The engine evaluates each policy against its declaring entity:

```
Ship: action
  when Submitted
  require HasStock
  require Employee.Warehouse
  require OwnedByCaller
{
  transition to Shipped
}
```

Two keywords, two distinct jobs: `when` gates lifecycle stage; `require` gates policy evaluation. The engine resolves each policy against its owning entity — no separate authorization syntax, no declaration-level subject switch.

**External policy resolution.** A policy declaration may defer its expression body to an external resolver. The `external` modifier signals that the policy's evaluation logic lives outside the DSL — in a database, a tenant configuration store, or a remote authorization service:

```
// Inline — expression evaluated directly by the engine
Warehouse: policy { role == "Warehouse" }

// External — expression resolved at runtime by a registered resolver
Warehouse: policy external
```

The policy contract is the same either way: a named boolean predicate that evaluates against its declaring entity. Actions reference the name identically — `require Employee.Warehouse` works regardless of whether the expression is inline or external. At evaluation time, the engine delegates to the appropriate resolver based on the declaration kind.

This mirrors ASP.NET Core's `[Authorize(Policy = "...")]` pattern: the authorization middleware checks policy names; where policy requirements come from (a hardcoded role list, a database query, an external identity provider) is opaque to the middleware. Naming is the integration seam.

External policies enable multi-tenant authorization where role definitions differ per tenant, policy-as-code patterns where authorization rules are versioned separately from the domain model, and integration with existing enterprise identity systems without duplicating their rule configuration in the DSL.

**Policy composition.** Complex conditions are composed inside named policies using C# conditional expression syntax (`&&`/`||`/`!`) with word aliases (`and`/`or`/`not`). The `require` clause on actions stays a flat list of names — no inline expression nesting at the call site:

```
IsAvailable: policy { QuantityOnHand > 0 and not IsExpired }
IsExpired: policy { ExpiryDate < DateTime.Now }

Reserve: action
  when Available
  require IsAvailable
{
  transition to Reserved
}
```

If the composition of `IsAvailable` changes, every action that references it inherits the update automatically. Complexity lives in policies; actions name them.

**Schema-first validation.** Property constraints (`range`, `length`, `pattern`, `required`, `write once`) map directly to request validation rules. The API layer can reject invalid input before it reaches domain logic — but the rules themselves are defined once in the DSL and compiled into middleware, not duplicated across validation libraries.

**Database schema generation.** Entities map to tables, properties to columns, constraints to column constraints, relationships to foreign keys, and `owned` relationships to cascading deletes. A single DSL file can produce both the API contract and the database migration.

**Policies as query predicates.** Every policy is a named Boolean expression over entity properties — which is exactly what a query filter is. The policy name becomes the query parameter or GraphQL field name; the expression compiles directly into a `WHERE` clause, an Elasticsearch filter, or a search index predicate:

```
IsActiveSupplier: policy { IsActive == true }
// → GET /suppliers?isActive=true
// → SQL: SELECT * FROM suppliers WHERE IsActive = true

IsLowStock: policy { QuantityOnHand < 5 }
// → GET /inventory?isLowStock=true  (reorder list)
// → GraphQL: query { lowStockItems { ... } }
```

Policies serve triple duty: they guard actions, they filter HATEOAS links, and they become the search/query surface. The lowering pass derives all three from a single declaration — no separate query DSL or duplicate filter logic.

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

Every declaration in the Poly DSL follows a single uniform pattern: **`Name: Kind Details`**. The name comes first, a colon separates, and the kind keyword tells the parser what the declaration *is* in the domain. Properties are the unmarked default — a bare type name implies a property. All other concepts, including the top-level types themselves, use an explicit kind keyword.

The domain modeling system has two engine primitives:

- **Value** — no identity, no lifecycle. Compared by content. Declared with `Name: value { ... }`.
- **Entity** — has identity, a lifecycle (stages), and can own relationships. Declared with `Name: entity { ... }`.

`actor` is syntactic sugar on `entity` — an actor *is* an entity with additional authorization constraints. Declaring `Name: actor { ... }` lowers to `entity` plus actor metadata (identity columns, claims tables, auth middleware). The engine has two primitives; the DSL has three declaration keywords for human clarity.

**Actor identity extension.** `Name: actor Parent { ... }` declares an actor that asserts it is a `Parent` — it inherits the parent's identity properties and adds its own. `Employee: actor User { ... }` means "Employee is an actor that is a User" — `Employee` participates in authorization, carries `User`'s identity properties (`email`, etc.), and adds its own (`badgeNumber`, `role`). An `Employee` satisfies any policy that checks `User`.

Every other concept is a member of an entity or actor (property, stage, action, policy, event, function) or a member of a value (property). DSL keywords like `stage`, `action`, `policy`, and `event` are human-facing sugar — they all lower to entity members in the engine.

| Syntax | Kind | Example |
|---|---|---|
| `Name: entity { ... }` | Entity type | `Product: entity { ... }` |
| `Name: value { ... }` | Value type | `Money: value { ... }` |
| `Name: actor { ... }` | Actor (entity + auth constraints; Phase 2+) | `User: actor { ... }` |
| `Name: actor Parent { ... }` | Actor extending parent identity (Phase 2+) | `Employee: actor User { ... }` |
| `Name: Type mod...` (bare type + modifiers) | Property or Relationship | `SKU: Text length(1, 50)` |
| `Name: stage { ... }` | Stage | `Active: stage { ... }` |
| `Name: action { ... }` | Action (may mutate, has when and require) | `Submit: action { ... }` |
| `Name() -> Type { ... }` | Function (read-only, no effects) | `totalValue() -> Number { ... }` |
| `Name: policy { expr }` | Named Boolean expression (inline) | `HasStock: policy { Qty > 0 }` |
| `Name: policy external` | Named Boolean expression (external resolver) | `Warehouse: policy external` |
| `Name: event { ... }` | Event (Phase 2+) | `OrderShipped: event { ... }` |

This pattern has several advantages:

- **Relationships are properties** — when a property's type is another entity or actor, it is a relationship. Cardinality and ownership are expressed through `one`/`many` and `owned` modifiers: `orders: many owned Order`, `manager: one Employee`. No separate `relationship` keyword or declaration block.
- **Implicit reverse navigation** — when an entity is `owned` by another, the domain engine synthesizes a reverse navigation property on the owned entity. The modeler only declares the owning side.
- **Zero ceremony for properties and actions** — `Submit: {}` inside a stage is a zero-ceremony action with implicit transition. `Name: Type` is a property with no keyword noise.
- **Self-describing syntax** — the kind keyword disambiguates at a glance, without consulting docs.
- **Extensible** — new domain concepts adopt the same `Name: Kind { ... }` pattern. No grammar changes needed.
- **LLM-friendly** — a uniform declaration pattern is easier for LLMs to emit correctly than position-dependent or context-sensitive syntax.
- **Actor is sugar on entity** — `actor` is not an engine primitive; it lowers to `entity` plus actor constraints. It signals to lowering passes that this type drives authorization, identity columns, and claims tables. `Name: actor Parent { ... }` extends the parent actor's identity — inheriting its properties and adding new ones. Policies declared on an actor evaluate against the actor's properties and are referenced via `require`. `actor` is also a reserved keyword in policy expressions, resolving to the authenticated caller.

Stages are **cyclical** by design:

```
Active: stage {
  Suspend: action {
    transition to Suspended
  }
}
Suspended: stage {
  Reinstate: action {
    transition to Active    // backward transition = cycle
  }
  Blacklist: action {
    transition to Blacklisted
  }
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

### Property Constraint Modifiers

Properties accept two categories of inline modifiers:

**Value constraints** — validate the property's value:

| Modifier | Example | Meaning |
|---|---|---|
| `range(min, max)` | `UnitCost: Number range(0, )` | Value must be ≥ 0 (max omitted = unbounded) |
| `length(min, max)` | `Code: Text length(3, 3)` | String must be exactly 3 characters |
| `pattern(regex)` | `Email: Text pattern("[^@]+@[^@]+")` | String must match the regex |

**Mutation constraints** — govern when the property can be set:

| Modifier | Example | Meaning |
|---|---|---|
| `required` | `SKU: Text required` | Must be set before the entity can leave its initial stage |
| `write once` | `OrderNumber: Text required write once` | Can be set once at creation; immutable after |
| `unique` | `Email: Text required unique` | Value must be unique across all instances of the entity |

Modifiers chain on the same line: `SKU: Text required unique write once`, `Email: Text pattern(...) required unique`. The parser reads them left to right; order does not affect semantics.

### Action Signatures

Actions have three forms, adding detail as needed:

**Zero-ceremony:** Inside a stage block, `Submit: {}` infers the transition from the action name (`Submit` → `Submitted`):

```
Draft: stage {
  Submit: {}
  Cancel: {}
}
Confirmed: stage {
  Ship: {}
}
```

**Action with body:** The `action` keyword marks a mutating operation. The body contains effects enclosed in `{ }`:

```
Submit: action {
  transition to Submitted
  assign submittedAt to DateTime.Now
}
```

Effects include `transition to` (stage change), `assign` (mutate a property), `return create` (produce and return a new entity), and `publish` (emit an event). Primitives expose static members via dot notation (`DateTime.Now`, `Date.Today`).

**Parameterized with return type:** Parameters appear in `()` after the name, return type uses `->`:

```
AddLineItem: action(price: Money, quantity: Number) -> OrderLineItem {
  assign total to total + price
  return create OrderLineItem { price: price, quantity: quantity }
}
```

**Read-only functions:** A bare `() -> Type` without the `action` keyword is a pure, read-only function. May contain only `return`:

```
totalValue() -> Number {
  return QuantityOnHand * UnitCost
}
```

Functions cannot contain `assign`, `transition to`, `publish`, `when`, or `require`. They compile to expression nodes only.

**Stage gates** use the `when` keyword before the action body. `when` accepts stage names only — it declares which lifecycle stages the action is available in. `when` may appear multiple times for logical grouping; all `when` lines are evaluated together (OR semantics — the action is available in any listed stage).

```
// Single stage
Cancel: action
  when Draft
{
  transition to Cancelled
}

// Multiple stages — available in any of Draft, Submitted, Confirmed
Cancel: action
  when Draft, Submitted, Confirmed
{
  transition to Cancelled
}
```

An action declared directly inside a stage block inherits that stage as an implicit gate. Additional `when` lines extend availability to other stages:

```
Active: stage {
  Suspend: action
    when Suspended     // inherits Active, adds Suspended
  {
    transition to Suspended
  }
}
```

**Policy guards** use the `require` keyword before the action body. `require` accepts policy names (including qualified cross-entity names like `Employee.Warehouse`) and inverted policies (`!PolicyName` or `not PolicyName`). `require` may appear multiple times for logical grouping; all `require` lines are evaluated together (AND semantics — all policies must be satisfied).

Authorization is not a separate clause. Policies on actor entities define actor-scoped guards, and actions reference them in `require` alongside entity policies. The engine evaluates each policy against its declaring entity.

```
// Entity policy only
Reserve: action
  require HasStock
{
  transition to Reserved
}

// Inverted policy
Suspend: action
  require not HighRatedSupplier
{
  transition to Suspended
}

// Business rule + actor policy — cross-entity reference evaluates against Employee
Ship: action
  when Submitted
  require HasStock
  require Employee.Warehouse
{
  transition to Shipped
  assign shippedAt to DateTime.Now
}

// Authorization only — anonymous welcome if no require lines are present.
// The presence of an actor policy reference in require gates authentication.
BrowseCatalog: action
  when Active
{
  // public — no require = no auth required
}
```

**Full action with both gates:**

```
Cancel: action
  when Draft, Submitted, Confirmed    // stage gates (OR)
  require OwnedByCaller               // entity policy (AND)
  require Employee.CustomerService    // actor policy (AND)
{
  transition to Cancelled
  assign canceledAt to DateTime.Now
}
```

`when` is OR (any listed stage), `require` is AND (all policies must pass). Stage gates and policy guards are two orthogonal dimensions — an action must be in a valid stage AND satisfy all required policies to execute.

### Policy Expression Grammar

Policy expressions inside `{ }` follow a subset of the C# conditional expression grammar:

| Category | Operators | Example |
|---|---|---|
| **Comparison** | `==` `!=` `>` `>=` `<` `<=` | `Age >= 18`, `Status == "Active"` |
| **Boolean logic** | `&&` / `and`, `||` / `or`, `!` / `not` (C# and word aliases) | `Qty > 0 and not IsExpired` |
| **Grouping** | `( )` | `(A and B) or C` |
| **Literals** | numbers, `true`/`false`, strings (double-quoted), `null` | `42`, `true`, `"Warehouse"`, `null` |
| **Property references** | unqualified property name on the declaring entity | `QuantityOnHand`, `customer` |
| **Reserved identifiers** | `actor` (authenticated caller) | `customer == actor` |
| **Static members** | `Type.Member` on primitives | `DateTime.Now`, `Date.Today` |

Precedence: `!`/`not` → comparisons (`==`, `!=`, `>`, `>=`, `<`, `<=`) → `&&`/`and` → `||`/`or`. Parentheses override.

`&&`/`||`/`!` and `and`/`or`/`not` are interchangeable within the same expression — the canonical printer normalizes to the word form.

Type checking is deferred to lowering — the parser accepts syntactically valid expressions and reports type mismatches (`Text == 42`) during analysis.

### Name Resolution

Names referenced in `when` and `require` clauses are resolved hierarchically:

1. **Current entity** — look for a matching stage name (`when` only) or policy name in the declaring entity.
2. **Parent entity** — if the entity extends another (e.g. `Employee: actor User`), look in the parent. An `Employee` satisfies `require User.SomePolicy` because it inherits `User`'s policies.
3. **Domain level** — reserved names defined at the domain scope (currently `actor`, resolving to the authenticated caller).

Primitive types expose static members via dot notation: `DateTime.Now`, `Date.Today`, `Text.Empty`. These resolve against the type, not the domain scope — no new keywords are added to the global namespace.

Qualified names (`Employee.Warehouse`) bypass the hierarchy and resolve directly against the named entity. Unqualified names walk current → parent → domain.

Actor identity extension drives resolution: `Employee: actor User` means every policy declared on `User` is available to `Employee`. `require Warehouse` on an action resolves against `Employee` first, then `User` — so `Warehouse` declared on `Employee` shadows a `Warehouse` declared on `User`.

### Namespace Rules

Properties, policies, stages, and actions share a single namespace per entity. `HasStock` cannot simultaneously name a property and a policy on `InventoryItem`. Entity names are globally unique across the domain.

```
// Error — HasStock is both a property and a policy
InventoryItem: entity {
  HasStock: Boolean
  HasStock: policy { QuantityOnHand > 0 }
}
```

Duplicate names within the same entity are parse errors. This prevents LLM-generated collisions and keeps resolution unambiguous.

Blocks are always delimited by `{ }` — never by whitespace indentation. The parser uses braces for all structural grouping.

Parameters and return types feed directly into API generation: a parameterized action becomes a request body schema, and a return type becomes a response schema. The lowering pass has all the information it needs to produce typed API contracts.

### Phase 1 Example: Supply Chain Domain

A concrete example of the minimal Phase 1 surface, derived from a real MCP session:

```poly
domain SupplyChain

Product: entity {
  SKU: Text required unique write once
  Name: Text required
  UnitCost: Number range(0, ) required
  MSRP: Number range(0, ) required
  ReorderPoint: Number
  IsHazardous: Boolean write once
  WeightKg: Number

  suppliers: many Supplier
  category: one Category
  inventory: many owned InventoryItem

  Draft: stage {
    Activate: {}
  }
  Active: stage {
    UpdatePricing: {}
    Discontinue: {}
  }
  Discontinued: stage {
    Archive: {}
  }
  Archived: stage {}
}

Supplier: entity {
  SupplierCode: Text required unique write once
  Name: Text required
  ContactEmail: Text pattern("[^@]+@[^@]+")
  LeadTimeDays: Number range(0, )
  Rating: Number range(0, 5)
  CountryOfOrigin: Text write once
  IsActive: Boolean

  products: many Product
  orders: many owned PurchaseOrder

  Prospective: stage {
    Approve: {}
  }
  Approved: stage {
    Activate: {}
  }
  Active: stage {
    Suspend: action
      require not HighRatedSupplier
    {
      transition to Suspended
    }
  }
  Suspended: stage {
    Reinstate: action transition to Active
    Blacklist: action transition to Blacklisted
  }
  Blacklisted: stage {}

  IsActiveSupplier: policy { IsActive == true }
  HighRatedSupplier: policy { Rating >= 4 }
}

Warehouse: entity {
  WarehouseCode: Text required unique write once
  Name: Text required
  Address: Text required
  CapacityCubicMeters: Number
  IsTemperatureControlled: Boolean write once

  items: many InventoryItem
  servedStores: many Store

  Planned: stage {
    Open: {}
  }
  Operational: stage {
    ScheduleMaintenance: action transition to Maintenance
    Decommission: action transition to Decommissioned
  }
  Maintenance: stage {
    Reopen: action transition to Operational
  }
  Decommissioned: stage {}
}

PurchaseOrder: entity {
  OrderNumber: Text required unique write once
  OrderDate: DateTime required write once
  ExpectedDeliveryDate: Date
  TotalCost: Number range(0, ) required
  CurrencyCode: Text write once

  shipments: many owned Shipment

  Draft: stage {
    Submit: {}
  }

  // Multi-stage action — valid from any listed stage
  action Cancel
    when Draft, Submitted, Confirmed
  {
    transition to Cancelled
  }

  Submitted: stage {
    Confirm: {}
  }
  Confirmed: stage {
    Ship: {}
  }
  Shipped: stage {
    Receive: {}
  }
  Received: stage {
    Return: action transition to Returned
  }
  Cancelled: stage {}
  Returned: stage {}
}

Shipment: entity {
  TrackingNumber: Text required unique write once
  EstimatedArrivalDate: Date
  ActualArrivalDate: Date
  ShippingMethod: Text

  Planned: stage {
    Dispatch: {}
  }
  InTransit: stage {
    MarkDelivered: action transition to Delivered
  }
  Delivered: stage {
    Verify: {}
  }
  Verified: stage {}
}

InventoryItem: entity {
  BatchNumber: Text required write once
  QuantityOnHand: Number range(0, ) required
  QuantityReserved: Number range(0, )
  ExpiryDate: Date write once
  BinLocation: Text

  Available: stage {
    Reserve: action
      require HasStock
    {
      transition to Reserved
    }
    MarkDamaged: action transition to Damaged
    MarkExpired: action transition to Expired
  }
  Reserved: stage {}
  Damaged: stage {}
  Expired: stage {}
  Depleted: stage {}

  HasStock: policy { QuantityOnHand > 0 }
  HasMinimumStock: policy { QuantityOnHand >= 10 }
  IsLowStock: policy { QuantityOnHand < 5 }
}

Store: entity {
  StoreCode: Text required unique write once
  Name: Text required
  Address: Text required
  Region: Text required
  Format: Text write once

  warehouses: many Warehouse

  Planned: stage {
    Open: {}
  }
  Active: stage {
    Close: action transition to Closed
    Relocate: action transition to Relocated
  }
  Closed: stage {}
  Relocated: stage {}
}

Category: entity {
  CategoryCode: Text required unique write once
  Name: Text required
  Description: Text

  products: many Product

  Active: stage {
    Archive: {}
  }
  Archived: stage {}
}
```

### Phase 2+ Example: E-Commerce with Actors & Authorization

The aspirational surface covering actors, authorization, richer effects, and value types.

- **Value** types have no identity and no lifecycle. They are compared by their contents. `Currency`, `Money`, `Email` — these describe data shapes, not domain objects.
- **Entity** types have identity, a lifecycle (stages), and can own relationships. `Product`, `Order`, `Shipment` are entities.
- **Actor** is sugar on `entity`. An actor is an entity that participates in authorization — it has identity, claims, and role-based policy evaluation. `Employee: actor { ... }` declares a standalone actor; `Employee: actor User { ... }` declares an actor that extends `User`'s identity, inheriting its properties.

**Authorization through policies.** Policies are the single declaration kind — no `permit policy` keyword. A policy on an actor entity evaluates against the actor. A policy on a regular entity evaluates against the entity. The reserved name `actor` refers to the authenticated actor within policy expressions, enabling entity policies to reference actor identity. Actions reference policies by qualified or unqualified name in `require`; stage gates use `when`.

```poly
domain ECommerce

// Value types — no identity, no lifecycle, compared by content
Currency: value {
  code: Text length(3, 3) required
  symbol: Text required
  name: Text required
}

Money: value {
  amount: Number range(0, ) required
  currency: Currency required
}

// Actor — fancy entity with auth constraints. Identity properties go here.
User: actor {
  email: Text pattern("[^@]+@[^@]+") length(5, 254) required unique
}

// Actor extending User — inherits User's identity, adds Employee-specific properties
Employee: actor User {
  badgeNumber: Text required unique write once
  role: Text required

  // Policies on actor — evaluate against the actor's properties
  CustomerService: policy { role == "Customer Service" }
  Warehouse: policy { role == "Warehouse" }
}

Customer: actor User {
  orders: many owned Order

  Active: stage {
    CreateOrder: action {
      return create order in orders
    }
  }
}

Order: entity {
  customer: one Customer required write once
  createdAt: DateTime required write once
  submittedAt: DateTime
    // Phase 2+: required when Submitted
  shippedAt: DateTime
    // Phase 2+: required when Shipped
  total: Money required

  OwnedByCaller: policy { customer == actor }

  Draft: stage {
    AddLineItem: action(price: Money)
      require Employee.CustomerService
    {
      assign total to total + price
    }

    Submit: action
      require OwnedByCaller
      require Employee.CustomerService
    {
      transition to Submitted
      assign submittedAt to DateTime.Now
    }

    Cancel: action
      require OwnedByCaller
      require Employee.CustomerService
    {
      transition to Cancelled
    }
  }

  Submitted: stage {
    AddLineItem: action(price: Money)
      require Employee.CustomerService
    {
      assign total to total + price
    }

    Ship: action
      require Employee.Warehouse
    {
      transition to Shipped
      assign shippedAt to DateTime.Now
    }

    Cancel: action
      require OwnedByCaller
      require Employee.CustomerService
    {
      transition to Cancelled
      assign canceledAt to DateTime.Now
    }
  }

  Shipped: stage {}
  Cancelled: stage {}
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

- **Phase 1:** entities, properties (bare type with inline value constraints `range`/`length`/`pattern` and mutation constraints `required`/`write once`/`unique`), relationships as entity-typed properties (`one`/`many`, optional `owned`, implicit reverse navigation), stages (cyclical graph), actions with stage transitions (`when` for stage gates, `require` for policy guards), policies (property comparison and composite boolean)
- **Phase 2:** actors (`Name: actor { ... }` — first-class declaration with identity/claims/roles, inherits all entity grammar), actor-scoped authorization via `require` referencing actor policies, action parameters and effect blocks, event declarations, richer effects
- **Phase 3:** comments and annotations, `require` actor type references (e.g. `require User` without a policy — means "caller must be an instance of User"), compatibility aliases, migration/versioning support

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
ReturnAuthorization: entity {
  ReturnCode: Text required write once
  Reason: Text required
  order: one PurchaseOrder

  Draft: stage {
    Submit: {}
  }
  Submitted: stage {
    Approve: action {
      transition to Approved
    }
    Deny: action {
      transition to Denied
    }
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

Enforced at parse time:

- **Single namespace per entity** — properties, policies, stages, and actions share one namespace. Duplicate names are parse errors.
- **Entity names** are globally unique across the domain.
- **Name resolution** is hierarchical: current entity → parent entity → domain scope. Qualified names (`Employee.Warehouse`) resolve directly against the named entity.
- **Actor inheritance** — policies declared on a parent actor (`User`) are available to extending actors (`Employee`). An unqualified name on `Employee` shadows the parent's name.

See [Name Resolution](#name-resolution) and [Namespace Rules](#namespace-rules) for details.

## Implementation Strategy

### Phase 1

1. Define the minimal grammar for:
   - domain
   - entity (`Name: entity { ... }`) — the `Name: kind` format applied at the top level
   - property (bare type: `Name: Type`) — when the type is another entity, it is a relationship
   - relationship modifiers inline on properties (`one`/`many`, optional `owned`)
   - inline constraint modifiers on properties (`range`, `length`, `pattern` for value constraints; `required`, `write once`, `unique` for mutation constraints)
   - stage (`Name: stage { ... }`)
   - action with `transition to` syntax; zero-ceremony inferred transitions (`{}`) when action name matches stage name by convention
   - multi-stage actions with `when` clause
   - `when` (stage gate) — accepts stage names only; repeatable; OR semantics
   - `require` (policy guard) — accepts policy names and inverted policies (`!` or `not` prefix); repeatable; AND semantics
- policy — named Boolean expression (`Name: policy { expr }`); optionally external (`Name: policy external`) for runtime-resolved authorization
2. Build a parser that emits `DomainMutationIntent[]`.
3. Build a canonical printer from the committed domain model.
4. Round-trip those supported constructs.
5. Expose `ExportDomainDsl(sessionId)` and `ImportDomainDsl(text, sessionId?)` as MCP tools.

### Phase 2

Add:

- value types (`Name: value { ... }`) — the second core primitive alongside entity; no identity, no lifecycle, compared by content
- actor specialization (`Name: actor [Parent] { ... }`) — an entity with identity, claims, and role-based policy evaluation; inherits all entity grammar (properties, stages, actions, policies)
- action parameters and return types (`action(Param: Type, ...) -> ReturnType`); `return` for value and `return create` for entity production; effect blocks (`assign`, `publish`)
- event declarations within entities (`Name: event { ... }`); event subscriptions with implicit publisher subject
- actor identity configuration
- richer effects (Create, PublishEvent, TransitionStage)

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


