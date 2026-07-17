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
## Design Principles

### 1. Actors Are First-Class DSL Primitives
- `actor` is a top-level declaration keyword in the DSL, alongside `entity` and `value`.
- In the engine, an actor lowers to `entity` plus actor-specific metadata (identity columns, claims, role-based policy evaluation). Value and entity/actor cover all stateful domain concepts.
- `Name: actor { ... }` declares a standalone actor.
- `Name: Parent { ... }` extends an existing entity or actor. If `Parent` is an actor, the child inherits actor-ness automatically — no `actor` keyword needed. `Employee: User` means "Employee extends User" — if `User` is an actor, `Employee` is too.

### 2. Stages as Declarative Lifecycle Nodes
- Stages are first-class lifecycle nodes that form a directed graph (not a linear pipeline). Cyclical transitions (e.g., `Suspended -> Active` via `Reinstate`) are valid and common.
- Stage transitions and policies will be expressed declaratively, with transitions driven by actions rather than implicit ordering.
- The DSL declares stages as an ordered list; transition edges are expressed on actions (`transition to Target`).

### 3. Effects: Expressive but Decoupled
- Effects must be expressive and composable, but should not introduce complexity or coupling that holds back the rest of the system.
- Focus on a small, powerful set of effect types: `assign` (mutate a property), `create` (create and return a new entity), `transition to` (stage change), and `invoke` (call an action on a reachable entity).

### 4. Relationships as Properties
- Relationships are entity-typed properties: `orders: many owned Order`. Cardinality is expressed through `many` (collection) vs bare (singular) modifiers; ownership through optional `owned`.
- The owning side declares the edge. The domain engine synthesizes an implicit reverse navigation property on the owned entity — no separate back-reference declaration is required.
- The MCP's `add_relationship` tool and `sourceOwnsTarget` boolean are replaced by a single property line.

### 5. Command/Intent System Unification
- The command pattern is retained for transactional mutation support, but the intent and command systems will be unified or closely aligned to reduce duplication and complexity.

### 6. Cross-Entity Mutation
- The system does not support direct cross-entity property mutation. Instead, `invoke target.Action(params)` calls an action on a reachable entity, and `when property Stage` subscribes to related entity lifecycle transitions.
- An entity can only be the subject of its own effects — the analyzer enforces this at parse time.

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

## Authoring Architecture

The `.poly` file is the shared serialization format for four authoring paths, each converging through the same analyzer:

```
Human (visual)     Human (text)       Agent (MCP)         Agent (text)
      │                 │                 │                   │
  Card Editor      .poly + LSP      MCP Tools          .poly + LSP
      │                 │                 │                   │
      └─────────────────┴─────────────────┴───────────────────┘
                                │
                    Domain Analyzer (shared)
                                │
                          .poly file (VCS, diff, review)
```

**Human visual (primary).** A Blueprint-style card editor displays entities as cards, stages as nodes, relationships and subscriptions as visual connection lines. The human never types DSL keywords — they drag connections between cards, add properties to property lists, and configure stage transitions through dropdowns. The card editor serializes to `.poly` text.

**Human text (power users).** A developer opens a `.poly` file directly and edits it with full LSP support — syntax highlighting, completions on relationship paths, inline diagnostics for unresolved references or unreachable subscriptions. The LSP server pushes the same diagnostics the analyzer produces.

**Agent MCP (exploration).** An LLM agent uses MCP tools (`add_entity`, `add_stage`, `add_relationship`, `get_domain_analysis`) to incrementally discover and decompose an unknown codebase. Each mutation is validated by the analyzer. The agent exports a `.poly` file as a checkpoint or handoff artifact.

**Agent text (generation).** An LLM agent writes DSL text directly — one statement at a time, receiving LSP diagnostics after each edit. The feedback loop is identical to a human's: write, see the diagnostic, fix, repeat. The agent doesn't need MCP for construction; it just needs programmatic access to LSP diagnostic output.

All four paths produce the same `.poly` format and are validated by the same analyzer. The canonical printer ensures stable output regardless of which path produced the model.

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

Each lowering pass translates the same IR into its target format. `SKU: Text required` becomes `required: true, readOnly: true` in OpenAPI — no per-protocol hand-rewriting.

**HATEOAS as an emergent property.** The stage machine *is* the link generator. An entity in stage `Submitted` exposes only the actions declared on that stage (or via `when`) as links. No hand-authored `if state == "Draft" then emit Submit link` logic. The API layer walks the current stage's outgoing action edges and emits `_links` for each. HATEOAS becomes trivial because the lifecycle graph is already encoded in the domain model.

**RBAC-constrained links.** When actions reference actor entity policies in their `require` clause (Phase 2+), the API layer filters `_links` by evaluating each referenced policy against the authenticated actor. A `PurchaseOrder` in `Submitted` returns different links for a warehouse worker (`Ship`) vs. a customer service rep (`Confirm`, `Cancel`, `AddLineItem`) vs. the ordering customer (`Cancel`). Role checks, HATEOAS links, and domain authorization stay in sync because they share a single source of truth: the DSL.

**Actor authorization through policies.** Policies are the single concept. There is no separate `permit policy` keyword. The engine infers evaluation context from where a policy is declared: a policy on an actor entity (`Employee`) evaluates against the actor's properties; a policy on a regular entity (`Order`) evaluates against the entity's properties. The reserved name `actor` refers to the authenticated actor within policy expressions, allowing entity policies to mix entity and actor references:

```swift
// Policy on an actor entity — evaluates against the actor
Warehouse: policy { role is "Warehouse" }

// Policy on a regular entity — evaluates against the entity
HasStock: policy { QuantityOnHand > 0 }

// Policy on a regular entity that references the actor via actor keyword
OwnedByCaller: policy { customer is actor }
```

Actions reference policies by name in `require` — qualified names for cross-entity references (`Employee.Warehouse`), unqualified for same-entity policies (`OwnedByCaller`, `HasStock`). The engine evaluates each policy against its declaring entity:

```swift
Ship: action
  when Submitted
  require HasStock, OwnedByCaller          // two policies on the entity (AND)
  require Employee.Warehouse               // OR actor policy (warehouse bypass)
{
  transition to Shipped
}
```

Two keywords, two distinct jobs: `when` gates lifecycle stage; `require` gates policy evaluation. The engine resolves each policy against its owning entity — no separate authorization syntax, no declaration-level subject switch.

**External policy resolution.** A policy declaration may defer its expression body to an external resolver. The `external` modifier signals that the policy's evaluation logic lives outside the DSL — in a database, a tenant configuration store, or a remote authorization service:

```swift
// Inline — expression evaluated directly by the engine
Warehouse: policy { role is "Warehouse" }

// External — expression resolved at runtime by a registered resolver
Warehouse: policy external
```

The policy contract is the same either way: a named boolean predicate that evaluates against its declaring entity. Actions reference the name identically — `require Employee.Warehouse` works regardless of whether the expression is inline or external. At evaluation time, the engine delegates to the appropriate resolver based on the declaration kind.

This mirrors ASP.NET Core's `[Authorize(Policy = "...")]` pattern: the authorization middleware checks policy names; where policy requirements come from (a hardcoded role list, a database query, an external identity provider) is opaque to the middleware. Naming is the integration seam.

External policies enable multi-tenant authorization where role definitions differ per tenant, policy-as-code patterns where authorization rules are versioned separately from the domain model, and integration with existing enterprise identity systems without duplicating their rule configuration in the DSL.

**Policy composition.** Complex conditions are composed inside named policies using C# conditional expression syntax (`&&`/`||`/`!`) with word aliases (`and`/`or`/`not`). The `require` clause on actions stays a flat list of names — no inline expression nesting at the call site:

```swift
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

**Schema-first validation.** Property constraints (`range`, `length`, `pattern`, `required`, `unique`) map directly to request validation rules. The API layer can reject invalid input before it reaches domain logic — but the rules themselves are defined once in the DSL and compiled into middleware, not duplicated across validation libraries.

**Database schema generation.** Entities map to tables, properties to columns, constraints to column constraints, relationships to foreign keys, and `owned` relationships to cascading deletes. A single DSL file can produce both the API contract and the database migration.

**Policies as query predicates.** Every policy is a named Boolean expression over entity properties — which is exactly what a query filter is. The policy name becomes the query parameter or GraphQL field name; the expression compiles directly into a `WHERE` clause, an Elasticsearch filter, or a search index predicate:

```swift
IsActiveSupplier: policy { IsActive is true }
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

The DSL has three top-level type declaration keywords, which map to three engine primitives:

- **Value** — no identity, no lifecycle. Compared by content. Declared with `Name: value { ... }`.
- **Entity** — has identity, a lifecycle (stages), and can own relationships. Declared with `Name: entity { ... }`.
- **Actor** — an entity that participates in authorization. Declared with `Name: actor { ... }`. Lowers to `entity` plus actor metadata (identity columns, claims, role-based policy evaluation).

The `domain` header precedes the types and declares the domain name plus an optional execution kind:

```swift
domain FranchiseCRM                   // implicit :service
domain Grep: cli                      // explicit :cli
domain PaymentCore: library           // explicit :library
```

The kind tells the lowering pass which execution model to target. A `cli` domain produces a synchronous executable with a `main()` entry point. A `service` domain produces a long-running process with async event handling and durable storage. A `library` domain produces a reusable package with no entry point. Default is `service`.

The domain kind also determines the **durability profile** of its entities. Entities in different domains with different durability needs are separated at the domain boundary and composed at the application level:

| Kind | Durability | Storage profile | Entity lifetime | Use case |
|---|---|---|---|---|
| `service` | Durable | Transactional, ACID, survives restarts | Persistent | CRM, orders, inventory |
| `cli` | Ephemeral | Transient, process-lifetime only | Process exits → reclaimed | grep, one-shot tools |
| `library` | Depends on consumer | No storage of its own | N/A | Reusable types |

A single application can compose multiple domains with different durability profiles. The platform routes each domain's entities to the appropriate storage provider. If an entity needs different durability than its sibling entities, it belongs in a separate domain.

```swift
// Durable — survives restarts
domain MyApp.CRM: service

Customer: entity { name: Text; orders: many Order }
Order: entity { total: Money }
```

```swift
// Ephemeral — one-shot execution
domain MyApp.Reporting: cli

ReportRun: entity { generatedAt: DateTime }
```

```swift
// Volatile — session state, TTL-based
domain MyApp.Sessions: service

Session: entity { token: Text; expiresAt: DateTime }
```

**Library distribution and import.** A `library` domain is a distributable Poly artifact — a self-contained package that other domains can reference via `import`. The `import` resolves against a package registry at build time, providing version resolution and contract validation:

```swift
// Poly Package: acme-crm v2.1.0 — distributed as a .poly package
domain AcmeCRM: library

Customer: entity { ... }
Project: entity { ... }
```

```swift
// Consumer domain — imports and extends the library
domain MyFranchise: service

import "acme-crm" version 2

Customer: AcmeCRM.Customer {
  franchiseId: Text required
}
```

The analyzer validates that imported contracts haven't drifted, extensions obey library constraints, and version compatibility holds. Library domains cannot have runtime dispatch (no subscriptions that require live event loops) — they define types and behaviors that service domains assemble into running systems.

This establishes a supply chain: `library` types are built and distributed by platform teams; `service` domains compose them into deployable applications; `cli` domains wrap services for single-shot execution.

**Entity extension.** `Name: Parent { ... }` extends an existing entity or actor — it inherits the parent's identity properties and adds its own. If the parent is an actor, the child is an actor too without repeating the `actor` keyword. `Employee: User { ... }` means "Employee extends User" — `Employee` inherits `User`'s identity properties (`email`, etc.) and adds its own (`badgeNumber`, `role`). An `Employee` satisfies any policy that checks `User`. The explicit `Name: actor Parent { ... }` form is also valid where needed (e.g. extending a plain entity into an actor).

Every other concept is a member of an entity or actor (property, stage, action, policy, function) or a member of a value (property, function).

| Syntax | Kind | Example |
|---|---|---|
| `domain Name[: kind]` | Domain header | `domain Grep: cli` |
| `Name: entity { ... }` | Entity type | `Product: entity { ... }` |
| `Name: value { ... }` | Value type | `Money: value { ... }` |
| `Name: Parent { ... }` | Extends a parent entity/actor (inherits its kind) | `Employee: User { ... }` |
| `Name: actor { ... }` | Actor (entity + auth; Phase 2+) | `User: actor { ... }` |
| `Name: Type mod...` (bare type + modifiers) | Property or Relationship | `SKU: Text length(1, 50)` |
| `Name: stage { ... }` | Lifecycle stage | `Active: stage { ... }` |
| `Name: action { ... }` | Action (may mutate, has when/require) | `Submit: action { ... }` |
| `Name() -> Type { ... }` | Function (pure, no effects) | `totalValue() -> Number { ... }` |
| `Name: policy { expr }` | Named Boolean expression | `HasStock: policy { Qty > 0 }` |
| `Name: policy external` | External policy (runtime resolver) | `Warehouse: policy external` |
| `invoke target.Action(params)` | Call action on reachable entity | `invoke customer.SendConfirmation(id)` |
| `start EntityName(params)` | Factory/initialization pattern | `start FulfillOrder(order)` |
| `schedule at expr { effects }` | Execute effects at future time | `schedule at endDate { transition to Completed }` |
| `when property Stage { effects }` | React to related entity's stage transition | `when calls Ended { assign total to event.duration }` |
| `for var in coll where cond { effects }` | Iterate and apply effects per element | `for line in lines where line.matches(p) { create MatchLine{content:line.text} }` |
| `parallel { step require deps { effects } }` | Parallel fork/join with constraint solving | `parallel { step require src, cfg { assign r to process(src,cfg) } }` |

This pattern has several advantages:

- **Relationships are properties** — when a property's type is another entity or actor, it is a relationship. Cardinality and ownership are expressed through `many` and `owned` modifiers: `orders: many owned Order`, `manager: Employee`. No separate `relationship` keyword or declaration block.
- **Implicit reverse navigation** — when an entity is `owned` by another, the domain engine synthesizes a reverse navigation property on the owned entity. The modeler only declares the owning side.
- **Zero ceremony for properties and actions** — `Submit: {}` inside a stage is a zero-ceremony action with implicit transition. `Name: Type` is a property with no keyword noise.
- **Self-describing syntax** — the kind keyword disambiguates at a glance, without consulting docs.
- **Extensible** — new domain concepts adopt the same `Name: Kind { ... }` pattern. No grammar changes needed.
- **LLM-friendly** — a uniform declaration pattern is easier for LLMs to emit correctly than position-dependent or context-sensitive syntax.
- **Actor is a first-class DSL primitive** — `actor` lowers to `entity` plus actor metadata in the engine. It signals to lowering passes that this type drives authorization, identity columns, and claims tables. `Name: Parent { ... }` extends a parent entity/actor — inheriting its properties and kind. If the parent is an actor, the child inherits actor-ness. Policies declared on an actor evaluate against the actor's properties and are referenced via `require`. `actor` is also a reserved keyword in policy expressions, resolving to the authenticated caller.

Stages are **cyclical** by design, and can optionally define **entry** and **exit** actions — effects that fire automatically on stage transition. Entry `require` guards prevent entering the stage; exit `require` guards prevent leaving. This is the natural place for stage-specific data constraints (e.g. "property must be set before entering this stage"):

```swift
Account: entity {
  accountNumber: Text required unique       // always NOT NULL, immutable
  closedAt: DateTime                          // optional at top level

  Active: stage {
    entry
      require CanActivate                     // policy guard
    {
      assign activatedAt to DateTime.Now
    }

    exit
      require { balance is 0 }               // expression guard: can't leave with non-zero balance
    { }

    Suspend: action {
      transition to Suspended
    }
  }

  Suspended: stage {
    entry { assign suspendedAt to DateTime.Now }

    Reinstate: action { transition to Active }
    Blacklist: action { transition to Blacklisted }
  }

  Closed: stage {
    entry require closedAt                   // shorthand → closedAt is not null
    { assign closedAt to DateTime.Now }
  }
}
```

**`require` forms.** `require` in entry/exit blocks follows the same grammar as action guards — comma-separated names for AND, separate lines for OR. Names resolve to policies or properties:

```swift
// Single policy
entry require CanActivate { ... }

// Multiple policies (AND)
entry require CanActivate, IsVerified { ... }

// Property shorthand — expands to is not null
entry require submittedAt, carrier { ... }

// Complex expression — braces for custom logic
entry require { items.all(i => i.stage is Reserved) } { ... }

// Multiple require lines (OR)
entry require VerifiedCustomer
      require ManualApproval
{ ... }
```

**`when` drives automatic transitions.** A stage's `when` subscriptions are reactive — they fire when the subscribed entity reaches the target stage. A `when` body can call `transition to` directly, making the entity advance automatically without an explicit action call:

```swift
Order: entity {
  reservations: many owned ItemReservation
  payment: Payment

  Awaiting: stage {
    // Reactive — when all reservations and payment are ready, auto-advance
    when all reservations Reserved and payment Captured {
      transition to ReadyToShip
    }
    when cancelRequested {
      transition to Cancelled
    }
    // Timeout — schedule transitions directly when deadline passes
    entry { schedule at deadline { transition to Failed } }
  }

  ReadyToShip: stage {
    // Gate — can't enter without meeting conditions
    entry require { reservations.all(r => r.stage is Reserved)
                 and payment.stage is Captured }
    { }
  }
  Cancelled: stage { }
  Failed: stage { }
}
```

The `when` subscription IS the exit condition. The body has access to the entity's full state and can evaluate compound conditions or call `transition to` when satisfied. No separate `exit auto` mechanism is needed — subscriptions are reactive by nature.

**`require` on entry/exit follows the same grammar as action guards**:

**Stage data shorthand.** A flat list of property names after `require` expands to `is not null` checks for each:

```swift
// Verbose:
entry require { shippedAt is not null }
       require { trackingNumber is not null }
       require { carrier is not null }

// Shorthand — expands to the same three checks:
entry require shippedAt, trackingNumber, carrier
```

The shorthand works for both entry and exit blocks. It only produces `is not null` assertions — for complex expressions (`is not null and carrier != "UPS"`), use the brace-delimited form.

### Relationship Syntax

Relationships are properties whose type is another entity. Cardinality and ownership are expressed inline:

```swift
name: many owned Order         // one-to-many, source owns target
name: many Order               // one-to-many, no ownership
name: owned Supplier           // one-to-one, source owns target
name: Supplier                 // one-to-one, no ownership (cardinality is the default)
```

**Ownership** (`owned`) means the source entity owns the target: deleting the source cascades to the target. The domain engine synthesizes an implicit reverse navigation property on the owned entity. A modeler may optionally name the reverse side for documentation, but the parser detects that it matches an existing `owned` edge and treats it as an alias, not a second relationship.

**Cardinality** is handled by `many` (collection) vs bare reference (singular). The parser infers the full cardinality type from presence of the `many` keyword — no separate enumeration is needed.

This replaces the MCP's separate `add_relationship` tool and `sourceOwnsTarget` boolean with a single line that reads naturally.

### Property Constraint Modifiers

Properties accept two categories of inline modifiers:

**Value constraints** — validate the property's value:

| Modifier | Example | Meaning |
|---|---|---|
| `range(min, max)` | `UnitCost: Number range(0, )` | Value must be ≥ 0 (max omitted = unbounded) |
| `length(min, max)` | `Code: Text length(3, 3)` | String must be exactly 3 characters |
| `pattern(regex)` | `Email: Text pattern("[^@]+@[^@]+")` | String must match the regex |

**Mutation constraints** — govern whether the property can be null and when it can be set:

| Modifier | Example | Meaning |
|---|---|---|
| `required` | `SKU: Text required` | NOT NULL — set at creation, immutable thereafter |
| `unique` | `Email: Text required unique` | Value must be unique across all instances of the entity |

**Stage-specific data constraints** belong in the stage's entry `require` block, not on the property declaration:

```swift
Account: entity {
  closedAt: DateTime                    // optional at the property level

  Closed: stage {
    entry require { closedAt is not null }    // required to enter this stage
    { assign closedAt to DateTime.Now }
  }
}
```

Modifiers chain on the same line: `SKU: Text required unique`, `Email: Text pattern(...) required`. The parser reads them left to right; order does not affect semantics.

Constraint parameters support named arguments for clarity and partial application. Unnamed positional arguments are also valid for single-parameter cases:

```swift
range(min: 0)       // ≥ 0, no upper bound
range(min: 1, max: 100)  // 1 to 100
range(0)            // positional shorthand for range(min: 0)
range(0, )          // also valid, trailing comma for min-only
pattern("[^@]+@[^@]+")  // single parameter — positional is natural
length(3)           // shorthand for length(min: 3)
length(min: 2, max: 10) // named for clarity
```

The canonical printer normalizes all forms to named syntax to avoid ambiguity.

### Action Signatures

Actions have three forms, adding detail as needed:

**Zero-ceremony:** Inside a stage block, `Submit: {}` infers the transition from the action name (`Submit` → `Submitted`):

```swift
Draft: stage {
  Submit: {}
  Cancel: {}
}
Confirmed: stage {
  Ship: {}
}
```

**Action with body:** The `action` keyword marks a mutating operation. The body contains effects enclosed in `{ }`:

```swift
Submit: action {
  transition to Submitted
  assign submittedAt to DateTime.Now
}
```

### Effect Types

Action bodies can contain the following effects, each expressing a different category of domain behavior:

| Effect | Syntax | Semantics |
|---|---|---|
| **Stage transition** | `transition to StageName` | Move entity to named lifecycle stage |
| **Property mutation** | `assign property to expr` | Set entity property to computed value |
| **Create entity** | `create Entity { props }` | Create new entity in its initial stage |
| **Create in collection** | `create in rel { props }` | Create child, add to owned collection |
| **Create with local** | `create name in rel { props }` | Create child, bind to local variable |
| **Iteration** | `for var in coll where cond { effects }` | Execute effects per element in filtered collection |
| **Parallel fork/join** | `parallel { step require deps { effects } }` | Execute independent steps concurrently, blocking until all complete |
| **Schedule** | `schedule at expr { effects }` | Execute effects at a future time |
| **Invoke action** | `invoke target.Action(params)` | Call action on reachable entity instance |
| **Start entity** | `start EntityName(params)` | Initialize new entity (factory pattern) |

**Stage transition.** Moves the entity to a new lifecycle stage. Must target a valid stage on the entity. Stage transitions are inherently observable — any entity with a `when property Stage` subscription on the transitioning entity will be notified. No separate `publish` construct is needed.

```swift
transition to Submitted
transition to Cancelled
```

**Property mutation.** Assigns a value to a property. The right side may be a literal, a property reference, an expression, or a static member:

```swift
assign submittedAt to DateTime.Now
assign total to total + price
assign status to "Active"
```

**Create entity.** Creates a new entity and returns it from the action. The body braces specify initial property values:

```swift
create OrderLineItem { price: price, quantity: quantity }
```

The new entity is created in its initial stage. Properties not listed in the initializer use their type's default. Required properties must be provided.

**Create in owned relationship.** Creates a new entity and atomically adds it to a `many owned` relationship collection on the current entity. The `in` keyword links the new entity to the collection. The entity type is inferred from the relationship's target type:

```swift
// No local — entity is created and added, no reference needed
create in entries {
  kind: "Deposit"
  amount: amount
  postedAt: DateTime.Now
}

// With local — needed for subsequent use
create entry in entries {
  kind: "Deposit"
  amount: amount
}
return entry.entryId
```

The local variable name is optional. When omitted, the entity is created as a side effect only. When present, it binds the created entity for subsequent expressions.

The relationship must be declared as `owned` on the current entity:

```swift
Customer: actor {
  orders: many owned Order      // owned relationship

  Active: stage {
    CreateOrder: action(price: Money) {
      create in orders {
        customer: this
        total: price
      }
    }
  }
}
```

The `in relationship` clause resolves against the entity's owned relationships at parse time.

**Stage transitions are inherently observable.** When an action calls `transition to StageName`, that transition itself is observable by any entity that has a relationship path to the transitioning entity. No separate `publish` effect or event declaration is needed — the fact of the transition, the target stage, the transitioning entity's identity, and the timestamp are all available to subscribers.

**`when property StageName` — React to related entity transitions.** An entity subscribes to stage transitions on related entities using `when property StageName` inside its own stage block:

```swift
Order: entity {
  payment: Payment
  items: many InventoryItem

  Submitted: stage {
    // React when the specific Payment I'm related to enters Received
    when payment Received {
      assign paidAt to DateTime.Now
    }
    // React when any of my items enters Reserved
    when items Reserved {
      assign itemsReserved to items.all(i => i.stage is Reserved)
    }
  }
}
```

The subscription is **scoped to the subscriber's current stage** — leaving the stage removes the subscription automatically. The `event` variable inside a `when` block refers to the transitioning entity instance. Correlation is automatic via the declared relationship path — no correlation keys, no event payload, no `subscribe to`.

**Collection-aware subscriptions.** The `when` keyword supports quantifiers for `many` relationships:

| Syntax | Behavior |
|---|---|
| `when path Stage { effects }` | Fires per element — each time any related entity enters `Stage` |
| `when any path Stage { effects }` | Fires once when at least one related entity is in `Stage` |
| `when all path Stage { effects }` | Fires once when every related entity is in `Stage` |
| `when all path not Stage { effects }` | Fires once when no related entity is in `Stage` (inverse) |

Multiple stages can be listed: `when all targets Scanned, Errored { transition to Exiting }`. The condition is "all targets are in Scanned or Errored." The `not` form inverts: `when all targets not Scanned, Errored { }` means "all targets are still outside the terminal stages."

```poly
GrepExecution: entity {
  targets: many SearchTarget

  Scanning: stage {
    // Per-element — fire each time a target errors
    when any targets Errored {
      assign exitCode to 2
    }
    // Collective — fire once when all targets reach a terminal stage
    when all targets Scanned, Errored, Skipped {
      transition to Exiting
    }
  }
}
```

The analyzer validates that the quantifier targets a `many` relationship, all stage names exist on the target type, and collective conditions are reachable.

**`schedule at expr { effects }` — Time-based effects.** A scheduled effect executes a block of effects at a specified future time:

```swift
InService: stage {
  entry {
    schedule at inServiceEndDate {
      transition to Completed
    }
  }
  exit {
    // Schedule is automatically cancelled when leaving the stage
  }
}
```

The time expression must produce a `DateTime`. The schedule fires once. Leaving the stage that declared the schedule automatically cancels it. If the `at` expression references entity properties, the schedule is evaluated at the time of stage entry — property changes after entry do not automatically reschedule (an explicit `schedule` call in an action can update it).

**Parallel fork/join.** The `parallel` effect executes independent steps concurrently, blocking until all complete. Each `step` declares its input dependencies via `require`. The constraint solver builds a dependency graph from `require` names and `assign` targets — steps with no interdependencies run in parallel; steps whose dependencies are satisfied by sibling outputs wait for those siblings to complete. Steps within a `parallel` block share a local scope — any variable assigned by one step is readable by subsequent steps via `require`.

```swift
AudioEncodeJob: entity {
  source: RawAudio
  config: AudioConfig

  Processing: stage {
    Run: action {
      parallel {
        step require source {
          assign loudness to analyze(this.source)
        }
        step require source, config {
          assign encoded to encode(this.source, this.config)
        }
        step require loudness, encoded {
          assign result to merge(this.loudness, this.encoded)
        }
      }
      assign finalResult to result
      transition to Completed
    }
  }
  Completed: stage { }
}
```

**Validation rules (enforced at parse time):**
- Every `require` name must resolve to an entity property, an output of a sibling step, or a `this`-scoped expression. Unreachable dependencies are parse errors.
- Every step must produce new values — `assign` targets must be unique across all steps in the block. Two steps cannot assign the same name.
- The dependency graph must be acyclic. Cycles are parse errors.
- The `parallel` block completes when all steps finish. The entity does not advance past the block until all steps resolve — the same blocking semantics regardless of nesting context.

Lowering to C# is straightforward — `Task.WhenAll` with waves computed from the dependency graph:

```csharp
// Step 1 and 2 have no interdependencies — Wave 1
var loudnessTask = Task.Run(() => analyze(source));
var encodedTask = Task.Run(() => encode(source, config));
await Task.WhenAll(loudnessTask, encodedTask);

// Step 3 depends on both outputs — Wave 2
var loudness = await loudnessTask;
var encoded = await encodedTask;
var result = await Task.Run(() => merge(loudness, encoded));
```

**Iteration.** The `for` construct iterates over a collection, optionally filtered by `where`, with effects applied per element:

```swift
for line in this.content.lines where line.matches(this.pattern) {
  create MatchLine { content: line.text }
}
```

`for var in collection where cond { effects }` — every modeler recognizes the pattern. The analyzer infers the loop variable's type from the collection element type. If the effect creates an entity, the analyzer infers which owned collection to add it to by matching the created type to the entity's relationships.

Without a filter:

```swift
for item in this.backlog {
  assign item.priority to "high"
}
```

**Implicit returns.** When an action or function declares a return type (`-> Type`), the last statement in the body is implicitly the return value. The parser verifies the last statement's produced type matches the declared type — mismatches are parse errors:

```swift
// Action with return type — last statement is implicit return
Submit: action -> Transaction {
  assign postedAt to DateTime.Now
  create Transaction { kind: "Deposit", amount: amount }    // ← implicit return
}

// Action without return type — no implicit return
Suspend: action {
  transition to Suspended
}

// Function — last expression is implicit return
totalValue() -> Number {
  QuantityOnHand * UnitCost                                  // ← implicit return
}
```

A `return` keyword is valid for early exits or explicitness, but the standard pattern omits it. Functions with `->` must end with an expression of the declared type.

`create` is an effect, not a return statement. Actions with `-> Type` use the last `create` statement (standalone or `in relationship`) as the implicit return value. Actions without `->` can also contain `create` — it produces the entity as a side effect, populating an owned relationship or spawning a new top-level record:

**Parameterized with return type.** Parameters appear in `()` after the name, return type uses `->`:

```swift
AddLineItem: action(price: Money, quantity: Number) -> OrderLineItem {
  assign total to total + price
  create OrderLineItem { price: price, quantity: quantity }  // ← implicit return
}
```

Parameters and return types feed directly into API generation: a parameterized action becomes a request body schema, and a return type becomes a response schema. The lowering pass has all the information it needs to produce typed API contracts.

### Stage Gates

Stage gates use the `when` keyword before the action body. `when` accepts stage names only — it declares which lifecycle stages the action is available in. `when` may appear multiple times for logical grouping; all `when` lines are evaluated together (OR semantics — the action is available in any listed stage).

```swift
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

```swift
Active: stage {
  Suspend: action
    when Suspended     // inherits Active, adds Suspended
  {
    transition to Suspended
  }
}
```

**Policy guards** use the `require` keyword before the action body. `require` accepts a comma-separated list of policy names (including qualified cross-entity names like `Employee.Warehouse`) and inverted policies (`!PolicyName` or `not PolicyName`). Multiple policies on the same `require` line are AND — all must pass. Multiple `require` lines are OR — any line that fully satisfies its policies authorizes the action. Lines may repeat for logical grouping.

This same semantics model — AND within a group, OR across groups — allows flexible authorization:

```swift
// Single line, single policy (trivial AND of one)
Reserve: action
  require HasStock
{
  transition to Reserved
}

// Single line, two policies (AND — both must pass)
Reserve: action
  require HasStock, IsAvailable
{
  transition to Reserved
}

// Two lines (OR — either path authorizes)
Cancel: action
  require OwnedByCaller
  require Employee.CustomerService
{
  transition to Cancelled
}

// Mixed: (A AND B) OR (C)
Cancel: action
  require OwnedByCaller, CustomerApproved
  require Employee.CustomerService
{
  transition to Cancelled
}

// Inverted policy in a group
Suspend: action
  require not HighRatedSupplier
{
  transition to Suspended
}
```

Authorization is not a separate clause. Policies on actor entities define actor-scoped guards, and actions reference them in `require` alongside entity policies. The engine evaluates each policy against its declaring entity.

```swift
// Business rule + actor policy — cross-entity reference evaluates against Employee
Ship: action
  when Submitted
  require HasStock, Employee.Warehouse
{
  transition to Shipped
  assign shippedAt to DateTime.Now
}

// Authorization only — anonymous welcome if no require lines are present
BrowseCatalog: action
  when Active
{
  // public — no require = no auth required
}

**Full action with both gates:**

```swift
Cancel: action
  when Draft, Submitted, Confirmed       // stage gates (OR)
  require OwnedByCaller, CustomerApproved  // (AND) entity + policy
  require Employee.CustomerService         // (OR) actor policy
{
  transition to Cancelled
  assign canceledAt to DateTime.Now
}
```

`when` is OR across stage names (any listed stage). `require` is AND within a line (comma-separated policies), OR across lines. Stage gates and policy guards are two orthogonal dimensions — an action must be in a valid stage AND satisfy at least one `require` line.

### Policy Expression Grammar

Policy expressions inside `{ }` follow a subset of the C# conditional expression grammar:

| Category | Operators | Example |
|---|---|---|
| **Match expression** | `match { cond -> value, else -> value }` | `exitCode: match { hasMatches() -> 0, else -> 1 }` |
| **Comparison** | `is` / `is not`, `>` `>=` `<` `<=` (`==` `!=` also valid as C# aliases) | `Age >= 18`, `Status is "Active"` |
| **Boolean logic** | `&&` / `and`, `||` / `or`, `!` / `not` (C# and word aliases) | `Qty > 0 and not IsExpired` |
| **Grouping** | `( )` | `(A and B) or C` |
| **Literals** | numbers, `true`/`false`, strings (double-quoted), `null` | `42`, `true`, `"Warehouse"`, `null` |
| **Property references** | unqualified property name on the declaring entity | `QuantityOnHand`, `customer` |
| **Reserved identifiers** | `actor` (authenticated caller), `this` (current entity instance) | `customer is actor`, `owner: this` |
| **Static members** | `Type.Member` on primitives | `DateTime.Now`, `Date.Today` |

### Collection operations

Query-style operations on `many`-typed properties:

```swift
items.all(i => i.stage is Completed)       // Boolean — all match
items.any(i => i.priority is "high")       // Boolean — any matches
items.count                                 // Number
items.sum(i => i.total)                    // value type
items.first(i => i.isUrgent)               // entity or null
items.filter(i => i.priority is "high")    // collection
```

### Match expressions

Pattern matching for branching logic without control flow:

```swift
exitCode: match {
  this.hasMatches()  -> 0
  this.exitCode is 2 -> 2
  else               -> 1
}
```

Precedence: `!`/`not` → comparisons (`is`/`is not`/`==`/`!=`, `>`, `>=`, `<`, `<=`) → `&&`/`and` → `||`/`or`. Parentheses override.

`&&`/`||`/`!` and `and`/`or`/`not` are interchangeable within the same expression — the canonical printer normalizes to the word form.

Type checking is deferred to lowering — the parser accepts syntactically valid expressions and reports type mismatches (`Text == 42`) during analysis.

### Name Resolution

Names referenced in `when` and `require` clauses are resolved hierarchically:

1. **Current entity** — look for a matching stage name (`when` only) or policy name in the declaring entity.
2. **Parent entity** — if the entity extends another (e.g. `Employee: User`), look in the parent. An `Employee` satisfies `require User.SomePolicy` because it inherits `User`'s policies.
3. **Domain level** — reserved names defined at the domain scope (currently `actor`, resolving to the authenticated caller).

**`this`** is a reserved keyword that resolves to the current execution context:

- **In an entity action body** — the entity instance
- **In a policy expression on an entity** — the entity instance
- **In an initializer block** — the entity being created
- **In a subscription body (`when property Stage`)** — the subscriber entity instance (not the transitioning entity). The transitioning entity is accessed via the implicit `event` variable.
- **In a value function body** — the value instance

```swift
// In policy expressions — compare property against the entity itself
OwnedByCaller: policy { customer is actor }

// In action bodies and create initializers — reference the owning entity
create order in orders {
  customer: this
}

// In entry/exit blocks — reference the entity instance
entry {
  assign activatedAt to DateTime.Now
}

// In subscription bodies — this is the subscriber entity instance
// event is the transitioning entity
when payment Received {
  require { this.balance > 0 }
}
```

Primitive types expose static members via dot notation: `DateTime.Now`, `Date.Today`, `Text.Empty`. These resolve against the type, not the domain scope — no new keywords are added to the global namespace.

Qualified names (`Employee.Warehouse`) bypass the hierarchy and resolve directly against the named entity. Unqualified names walk current → parent → domain.

Actor identity extension drives resolution: `Employee: User` means every policy declared on `User` is available to `Employee`. `require Warehouse` on an action resolves against `Employee` first, then `User` — so `Warehouse` declared on `Employee` shadows a `Warehouse` declared on `User`.

### Namespace Rules

Properties, policies, stages, and actions share a single namespace per entity. `HasStock` cannot simultaneously name a property and a policy on `InventoryItem`. Entity names are globally unique across the domain.

```swift
// Error — HasStock is both a property and a policy
InventoryItem: entity {
  HasStock: Boolean
  HasStock: policy { QuantityOnHand > 0 }
}
```

Duplicate names within the same entity are parse errors. This prevents LLM-generated collisions and keeps resolution unambiguous.

Blocks are always delimited by `{ }` — never by whitespace indentation. The parser uses braces for all structural grouping.

### Phase 1 Example: Supply Chain Domain

A concrete example of the minimal Phase 1 surface, derived from a real MCP session:

```swift
domain SupplyChain

Product: entity {
  SKU: Text required unique
  Name: Text required
  UnitCost: Number range(0, ) required
  MSRP: Number range(0, ) required
  ReorderPoint: Number
  IsHazardous: Boolean
  WeightKg: Number

  suppliers: many Supplier
  category: Category
  inventory: many owned InventoryItem

  Draft: stage {
    Activate: {}
  }
  Active: stage {
    UpdatePricing: {}
    Discontinue: {}
  }
  Discontinued: stage {
    entry require SKU, Name, UnitCost
    { }
    Archive: {}
  }
  Archived: stage {}
}

Supplier: entity {
  SupplierCode: Text required unique
  Name: Text required
  ContactEmail: Text pattern("[^@]+@[^@]+")
  LeadTimeDays: Number range(0, )
  Rating: Number range(0, 5)
  CountryOfOrigin: Text
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
  Blacklisted: stage {
    entry require SupplierCode, Name
    { }
  }

  IsActiveSupplier: policy { IsActive is true }
  HighRatedSupplier: policy { Rating >= 4 }
}

Warehouse: entity {
  WarehouseCode: Text required unique
  Name: Text required
  Address: Text required
  CapacityCubicMeters: Number
  IsTemperatureControlled: Boolean

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
  Decommissioned: stage {
    entry require WarehouseCode
    { }
  }
}

PurchaseOrder: entity {
  OrderNumber: Text required unique
  OrderDate: DateTime required
  ExpectedDeliveryDate: Date
  TotalCost: Number range(0, ) required
  CurrencyCode: Text

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
    entry require ExpectedDeliveryDate
    { }
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
  Cancelled: stage {
    entry require OrderNumber, OrderDate, TotalCost
    { }
  }
  Returned: stage {}
}

Shipment: entity {
  TrackingNumber: Text required unique
  EstimatedArrivalDate: Date
  ActualArrivalDate: Date
  ShippingMethod: Text

  Planned: stage {
    Dispatch: {}
  }
  InTransit: stage {
    entry require TrackingNumber
    { }
    MarkDelivered: action transition to Delivered
  }
  Delivered: stage {
    entry require ActualArrivalDate
    { }
    Verify: {}
  }
  Verified: stage {}
}

InventoryItem: entity {
  BatchNumber: Text required
  QuantityOnHand: Number range(0, ) required
  QuantityReserved: Number range(0, )
  ExpiryDate: Date
  BinLocation: Text

  Available: stage {
    entry require BatchNumber
    { }
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
  Depleted: stage {
    entry require QuantityOnHand, QuantityReserved
    { }
  }

  HasStock: policy { QuantityOnHand > 0 }
  HasMinimumStock: policy { QuantityOnHand >= 10 }
  IsLowStock: policy { QuantityOnHand < 5 }
}

Store: entity {
  StoreCode: Text required unique
  Name: Text required
  Address: Text required
  Region: Text required
  Format: Text

  warehouses: many Warehouse

  Planned: stage {
    Open: {}
  }
  Active: stage {
    entry require StoreCode, Name, Address, Region
    { }
    Close: action transition to Closed
    Relocate: action transition to Relocated
  }
  Closed: stage {}
  Relocated: stage {}
}

Category: entity {
  CategoryCode: Text required unique
  Name: Text required
  Description: Text

  products: many Product

  Active: stage {
    Archive: {}
  }
  Archived: stage {}
}
```

### Phase 2+ Example: E-Commerce with Actors, Authorization, Events & Workflows

*(This example uses the older event/workflow model. The current spec replaces `event`/`publish`/`subscribe to` with `when property Stage` subscriptions and `schedule at` for time-based triggers. See `docs/experiments/examples/phone-call.poly` and `docs/experiments/examples/franchise-crm.poly` for current models.)*

The aspirational surface covering actors, authorization, richer effects, value types, events, and workflow-driven sagas.

- **Value** types have no identity and no lifecycle. They are compared by their contents. `Currency`, `Money`, `Email` — these describe data shapes, not domain objects.
- **Entity** types have identity, a lifecycle (stages), and can own relationships. `Product`, `Order`, `Shipment` are entities.
- **Actor** is a first-class DSL primitive that lowers to an entity with authorization metadata — identity columns, claims, role-based policy evaluation. `User: actor { ... }` declares an actor; `Employee: User { ... }` extends `User` and inherits actor-ness automatically. `Employee: actor User { ... }` is also valid (explicit) but unnecessary when the parent is already an actor.
- **Event** types are declared inside entity blocks. Events carry implicit subject metadata (the publishing entity's identity plus timestamp). Entities publish events via `publish` effects in actions. Workflows subscribe to events via `subscribe` blocks, with automatic correlation via the relationship graph from the event's subject to the workflow's input type.
- **Workflow** is a top-level declaration that models processing graphs and sagas. Workflows subscribe to events, execute typed processing stages, and can publish events themselves.

**Authorization through policies.** Policies are the single declaration kind — no `permit policy` keyword. A policy on an actor entity evaluates against the actor. A policy on a regular entity evaluates against the entity. The reserved name `actor` refers to the authenticated actor within policy expressions, enabling entity policies to reference actor identity. Actions reference policies by qualified or unqualified name in `require`; stage gates use `when`.

```swift
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

// User is an actor — identity + authorization baseline
User: actor {
  email: Text pattern("[^@]+@[^@]+") length(5, 254) required unique
}

// Extends User — inherits identity properties AND actor-ness
Employee: User {
  badgeNumber: Text required unique
  role: Text required

  // Policies on actor — evaluate against the actor's properties
  CustomerService: policy { role is "Customer Service" }
  Warehouse: policy { role is "Warehouse" }
}

Customer: User {
  orders: many owned Order

  Active: stage {
    CreateOrder: action {
      create order in orders { customer: this }
    }
  }
}

Order: entity {
  customer: Customer required
  createdAt: DateTime required
  submittedAt: DateTime
  shippedAt: DateTime
  total: Money required

  OwnedByCaller: policy { customer is actor }

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
    entry require submittedAt
    { }

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

  Shipped: stage {
    entry require shippedAt
    { }
  }
  Cancelled: stage {}

  // Events — subject is implicitly the Order instance
  OrderSubmitted: event { }
  OrderShipped: event {
    trackingNumber: Text
    shippedAt: DateTime
  }
  OrderFulfilled: event {
    fulfilledAt: DateTime
  }
}

InventoryItem: entity {
  order: Order required
  quantityOnHand: Number range(0, ) required

  // Events — subject is implicitly the InventoryItem instance
  // 'order' (Order) is implicit from the subject's this.order relationship
  Reserved: event {
    quantity: Number
  }
  OutOfStock: event {
    requestedQuantity: Number
  }

  Available: stage {
    Reserve: action
      require { quantityOnHand >= requestedQuantity }
    {
      publish Reserved {
        quantity: requestedQuantity
      }
      assign quantityOnHand to quantityOnHand - requestedQuantity
      transition to Reserved
    }
    MarkOutOfStock: action {
      publish OutOfStock {
        requestedQuantity: requestedQuantity
      }
    }
  }
  Reserved: stage {}
}

// Workflow orchestrates fulfillment via event subscriptions
// Correlation is automatic: InventoryItem.subject.order -> Order = workflow input
FulfillOrder: workflow Order -> FulfillmentResult {
  subscribe to Inventory.Reserved {
    // Inventory reserved — workflow runtime marks this event as received
    // Stage guards evaluate automatically when all required events arrive
  }
  subscribe to Inventory.OutOfStock {
    // Out of stock — trigger escalation workflow
    start StockoutEscalation(this.input)
  }

  ReserveInventory: stage Order -> InventoryReserved {
    rollback { transition to ReleaseInventory }
  }
  ShipOrder: stage InventoryReserved -> ShipmentConfirmation {
    rollback { transition to CancelShipment }
  }
  ConfirmDelivery: stage ShipmentConfirmation -> FulfillmentResult {
    publish Order.OrderFulfilled {
      fulfilledAt: DateTime.Now
    }
  }
}
```

*(Previous drafts of this spec included `workflow` as a separate top-level type, an `event` keyword, `publish`/`subscribe to` effects, and a dedicated event infrastructure. During design validation against real domains (phone call, franchise CRM, grep), these constructs proved redundant: every observable behavior maps to an entity entering a stage, relationships provide the correlation fabric, and time-based transitions are handled by `schedule at`. The current model — entities, values, actors, `when property Stage` for subscriptions, `schedule at` for timers — covers all cases with fewer primitives. See `docs/experiments/examples/` for worked models of phone-call, franchise-crm, and grep domains.)*

## Deployment Modes

The DSL model is independent of how it executes. The same domain — entities, stages, subscriptions, `schedule at` — runs in three deployment modes, selected by the runtime configuration, not by the domain definition:

| Mode | Stage transition delivery | Schedule persistence | Use case |
|---|---|---|---|
| **In-memory** | Synchronous in-process dispatch | None — timer held in process memory | Unit testing, simulator, `cli` domains |
| **Queue-backed** | At-least-once delivery via durable queue | Timer stored alongside queue | Production async processing |
| **Database outbox** | Transactional outbox table — stage change + event in same DB transaction | Timer persisted in entity row (`schedule_at` column) | Transactional reliability without distributed transactions |

A `cli` domain defaults to in-memory. A `service` domain defaults to database outbox. A `library` domain has no runtime — it's compile-time only.

The mode does not change the model. An entity's `when targets Scanned` subscription and a `schedule at endDate { transition to Completed }` work identically in all three modes — only the delivery and durability differ.

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

- **Phase 1:** entities, properties (bare type with inline value constraints `range`/`length`/`pattern` and mutation constraint `required`/`unique`), relationships as entity-typed properties (`one`/`many`, optional `owned`, implicit reverse navigation), stages (cyclical graph, entry/exit with `require`), actions with stage transitions (`when` for stage gates, `require` for policy guards), policies (property comparison and composite boolean)
- **Phase 2:** value types (`Name: value { ... }`) with pure functions and `require` guards; actors (`Name: actor { ... }` — entity with authorization, inherits all entity grammar); action parameters and return types (`action(Param: Type) -> ReturnType`); entity functions (`Name() -> Type { expression }`); stage subscriptions (`when property StageName { effects }` with automatic correlation via relationship graph); `schedule at expr { effects }` for time-based transitions; `for var in coll where cond { effects }` for collection iteration and bulk creation; collection query operations (`all`, `any`, `sum`, `count`, `first`, `filter`); `match` expressions for pattern matching; cross-entity mutation rule enforcement; `invoke` effect for calling actions on reachable entity instances
- **Phase 3:** comments and annotations round-trip; `require` actor type references; compatibility aliases; migration/versioning support; ACL/import syntax; lowering passes (REST, OpenAPI, schema generation); scoped export (windowed DSL for agents); agent-driven decomposition affordances

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

```swift
// Applying this fragment to an existing session adds one entity.
// The other 8 entities remain untouched. No deletions.
ReturnAuthorization: entity {
  ReturnCode: Text required
  Reason: Text required
  order: PurchaseOrder

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

### Scoped Export (Windowed DSL)

`ExportDomainDsl` accepts an optional scope parameter, returning a valid DSL fragment instead of the full model. This lets agents request exactly the slice they need without consuming the entire domain in context:

| Scope | Description | Use case |
|---|---|---|
| `by-entity: name` | One or more named entities + their stages, policies, subscriptions | Agent analyzing a single concept |
| `by-depth: N` | Entity + its relationships up to N hops from a seed | Agent understanding a bounded context |
| `by-subscription: Entity.Stage` | Entity, stage, all subscription targets it references via `when` | Agent tracing impact of a stage transition |
| `full` | Complete domain (default) | Checkpointing, persistence, handoff between sessions |

```swift
// Agent requests the Awaiting stage of the Order entity and everything it references
ExportDomainDsl(sessionId, scope: "by-subscription: Order.Awaiting")

// Returns only:
// - The FulfillOrder workflow with its subscriptions and stages
// - The entities/types referenced by the workflow (Order, InventoryItem, etc.)
// - The events the workflow subscribes to (Inventory.Reserved, etc.)
// - Nothing else — even if the domain has 50 other entities
```

Scoped output is a valid DSL fragment that can be imported back. The printer walks the dependency graph from the scope seed outward, including every type reachable through declared references, relationships, and subscriptions. The agent works in a small window, requests the next window when needed — the bounded domain size makes reasoning tractable within a fixed context budget.

## DSL Affordances

Key design affordances the parser and printer must address, derived from real MCP session experience:

### 1. Idempotent Import (P0)

Re-declaring an existing construct identically is a no-op, not an error and not a duplicate. `ImportDomainDsl` is safe to re-run. The DSL describes **desired state**, not imperative mutations. Contrast with the MCP's imperative `add_*` tools where calling twice may produce duplicates.

### 2. Line-Level Error Reporting (P0)

Parse and commit errors must reference DSL line numbers, not internal `DomainMutationIntent` indices. "line 42: entity 'Order' not found" — not "mutation index 7 failed." This is critical for human debugging and LLM self-correction loops.

### 3. Reference Validation — All At Once (P1)

Cross-entity type references (`category: Category`, `inventory: many owned InventoryItem`) are resolved at the end of the parse pass. Forward references are accepted. All unresolved references are reported together, not one at a time.

### 4. Whitespace Resilience (P1)

Indentation is cosmetic, not structural. The parser accepts 2-space, 4-space, or tab indentation within blocks. The canonical printer normalizes. This prevents "correct model, import failed due to a tab" scenarios, which are common with LLM-generated DSL text.

### 5. Version Pragma (P1)

The first non-comment line of a DSL file must be a version pragma:

```swift
# poly-dsl v1
```

or

```swift
domain SupplyChain format v1
```

The parser uses this to select the correct grammar. Without a pragma, evolving the DSL means either breaking existing files or building a heuristic version detector. The pragma is cheaper.

### 6. Comment Round-Trip (P2)

Comments survive `parse → print`. At minimum, structural comments attached to declarations:

```swift
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

### 9. Agent-Driven Decomposition (P2)

The DSL is designed for **incremental discovery via MCP tools**, not just bulk import/export. An agent exploring an unknown codebase follows this workflow, guided by analyzer diagnostics at every step:

1. **Discover a core concept** → `add_entity` with its known properties
2. **Find related types** → `add_property`, `add_relationship`
3. **Run analysis** → `get_domain_analysis` reports unresolved references, missing types, reachability issues
4. **Follow diagnostics** → each diagnostic is a concrete todo item: "type 'Order' not found" → add Order
5. **Add behavior when stable** → `add_stage`, `add_action`, `add_policy`
6. **Wire subscriptions** → `when property Stage` subscriptions within stage blocks

At every step the domain is valid. Partial models parse successfully — they carry unresolved references and unreachable subscriptions as diagnostics, not fatal errors. The agent treats each diagnostic as the next thing to investigate.

```swift
// Agent's session, step by step:
// Step 1: add_entity("InventoryItem")
// Step 2: add_property("InventoryItem", "quantityOnHand", "Number")
// Step 3: add_property("InventoryItem", "order", "Order")
// Step 4: get_domain_analysis() → diagnostic: "type 'Order' not found"
// Step 5: add_entity("Order")  // follows the diagnostic
```

**The DSL is the recording, not the workflow.** The MCP tools are the exploration surface; the canonical DSL export is what the agent produces after learning enough to emit a coherent block. An agent may build a model entirely through MCP mutations and only call `ExportDomainDsl` at checkpoint boundaries or session handoff.

### 10. Naming Rules (P3)

Enforced at parse time:

- **Single namespace per entity** — properties, policies, stages, and actions share one namespace. Duplicate names are parse errors.
- **Entity names** are globally unique across the domain.
- **Name resolution** is hierarchical: current entity → parent entity → domain scope. Qualified names (`Employee.Warehouse`) resolve directly against the named entity.
- **Actor inheritance** — policies declared on a parent actor (`User`) are available to extending children (`Employee: User`). An unqualified name on the child shadows the parent's name.

See [Name Resolution](#name-resolution) and [Namespace Rules](#namespace-rules) for details.

## Poly Library Import

`library`-kind domains are the primary distribution mechanism for reusable Poly artifacts. The Poly package registry stores and versions these libraries:

```swift
// Publishing a library
// acme-crm v2.1.0 — built and distributed by the platform team
domain AcmeCRM: library

Customer: entity { ... }
Project: entity { ... }
```

```swift
// Consuming a library at build time
domain MyFranchise: service

import "acme-crm" version 2

// Extend library types
Customer: AcmeCRM.Customer {
  franchiseId: Text required
}
```

The analyzer at import time:
- Resolves the imported library against the package registry
- Validates version compatibility (major version pinning)
- Checks that extensions don't violate base type constraints
- Reports contract drift as compile-time diagnostics

Library imports are distinct from external API imports. A library import pulls in Poly types — entities, values, actors, stages, actions, policies — all within the same type system. An API import (see Third-Party Contracts) pulls in external schemas as opaque payloads behind an Anti-Corruption Layer.

## Third-Party Contracts

Domains don't exist in isolation. Real models must integrate with ERPs, CRMs, payment gateways, shipping carriers, and other external systems whose schemas live outside Poly. The DSL needs a vocabulary for declaring and consuming these external contracts without pretending they're native domain primitives.

### Problem

External schemas have different constraints from Poly domain models:

- No lifecycle (stages, transitions)
- May have mutable state that Poly would model as immutable
- Ownership and cardinality are implicit
- Fields may be optional when Poly would require them
- Schema versioning is controlled by the external party

Mixing imported schemas directly into domain declarations creates coupling — upstream spec changes break the domain model. The DSL needs an explicit **Anti-Corruption Layer (ACL)**: imports are scoped to their own namespace and mapped into local domain objects through bindings. Domain logic never references imported types directly.

### Existing Engine Model

The domain engine already models imported external APIs:

- **`ImportedContract`** — a named reference to an imported API spec (e.g. OpenAPI). Has a `SourceKind` (InternalDomain / ExternalProvider), `SourceIdentifier` (URL, file path), and `Version`. Contains `ContractEndpoint`s.
- **`ContractEndpoint`** — a single operation from the imported spec. Has a `Kind` (Operation / Event), `Direction` (Inbound / Outbound), and a `PayloadType` (the request/response body type from the spec).
- **`ContractBinding`** — connects a domain action to an imported endpoint. Carries `ContractFieldMap[]` entries mapping `RemoteFieldName ↔ LocalFieldName`.
- **`ContractIntegrationAnalyzer`** — validates bindings: endpoints exist, actions exist, types are compatible, field maps are non-empty.

The seam is the **Anti-Corruption Layer**: imports are always scoped; bindings are always field-mapped.

### Import as Scoped Namespace

Every import lives in its own scope. Imported types are never referenced directly in domain entities or actions — bindings are the enforcement mechanism:

```swift
import "https://erp.example.com/openapi/v2.json" version 2 as Erp

// Erp.InvoicePayload exists but is not directly consumable.
// Local domain objects are the only valid parameter types.
```

**Rules:**
- Imported types are accessible only through qualified names (`Erp.InvoicePayload`). Using an imported type directly as a property or action parameter type is a parse error.
- The import resolver fetches the spec and extracts endpoints. Each endpoint becomes a qualified name (`Erp.SubmitInvoice`).
- Payload types are internal to the import — the domain never consumes them directly.

### Bind Block Scope

`bind Left to Right { ... }` establishes two scopes for every `map` line inside, and the ordering determines data flow:

| Line | Meaning | Flow |
|---|---|---|
| `bind Erp.SubmitInvoice to order` | Endpoint produces, domain consumes | Inbound |
| `bind order to Erp.CustomerNotification` | Domain produces, endpoint consumes | Outbound |

`map a to b` always reads as "map Left.a to Right.b" — no dot notation on either side. Direction is explicit in the bind line ordering, not inferred from endpoint metadata.

**Inbound** (endpoint → parameter):

```swift
SubmitInvoice: action(order: OrderInvoice) -> OrderConfirmation
  bind Erp.SubmitInvoice to order {
    map invoiceNumber   to invoiceNumber      // payload → order (identity)
    map total           to totalAmount        // payload.total → order.totalAmount (rename)
  }
{
  transition to Submitted
}
```

**Outbound** (parameter → endpoint):

```swift
NotifyCustomer: action(order: OrderInvoice)
  bind order to Erp.CustomerNotification {
    map customerEmail   to email              // order.customerEmail → payload.email
    map customerName    to name               // identity
    map totalAmount     to amountDue          // rename
  }
{
  publish OrderNotified
}
```

**Key rule:**
- `bind Left to Right` — Left is always the data source, Right is the data target.
- Inside `{ }`, `map a to b` means "map Left.a to Right.b" — no qualified prefixes.
- When field names match, the line declares identity mapping. Renames are explicit.
- If Erp renames `invoiceNumber` to `invoiceId`, the stale `map` line fails at parse time — the domain action body never changes. The ACL absorbs the drift.

### Open Questions

1. **Where does import resolution run?** MCP session could fetch and cache specs; CLI could need a registry or file path. Implementation concern.

2. **Multiple bindings per action.** An action could bind to multiple endpoints (e.g. listen on two contracts). The `bind` keyword should repeat.

3. **Complex field paths.** Nested maps like `shipTo.address.city to shippingAddress.city` — dot notation on both sides for nested access. `ContractFieldMap.RemoteFieldName` is a string; the engine resolves the path.

4. **Version pinning.** An import tied to version 2 fails stale maps against a v3 spec at parse time. This is the ACL's primary leverage — external changes become compile-time errors, not runtime surprises.

This section is exploratory — the bind syntax above is a first draft. The key constraint is that the ACL is non-negotiable: imported types never appear in domain declarations. The engine's `ContractBinding` + `ContractFieldMap` primitives already enforce this at the data level; the DSL's job is to surface it in authoring format.

## Implementation Strategy

### Phase 1

1. Define the minimal grammar for:
   - `domain Name[: kind]` header (kinds: `service`, `cli`, `library`; default `service`)
   - `Name: entity { ... }` — entity type
   - Property: `Name: Type` (bare type) — when the type is another entity, it is a relationship
   - Relationship modifiers inline on properties: `many`, `owned`
   - Constraint modifiers on properties: `range`, `length`, `pattern`, `required`, `unique`
   - Stage: `Name: stage { ... }`; optional entry/exit blocks with `require` shorthand
   - Action: `transition to` syntax; zero-ceremony `{}` when action name matches stage name by convention
   - Multi-stage actions with `when` clause (stage gate — stage names only, OR semantics)
   - `require` (policy guard) — comma-separated policies AND within line, OR across lines; accepts `not` prefix
   - Policy: `Name: policy { expression }`; optionally `Name: policy external`
2. Build parser emitting `DomainMutationIntent[]` → committed domain
3. Build canonical printer from committed domain
4. Round-trip supported constructs
5. Expose `ExportDomainDsl(sessionId)` and `ImportDomainDsl(text)` as MCP tools

### Phase 2

Add:

- Value types: `Name: value { ... }` with pure functions and `require` guards
- Actors: `Name: actor { ... }` with authorization policies; entity extension via `Name: Parent { ... }`
- Action parameters and return types: `action(Param: Type) -> ReturnType`; implicit return from last `create` statement
- Effect blocks: `assign`, `create`, `create in`, `invoke`, `schedule`, `for`, `parallel`
- `parallel { step require deps { effects } }` — parallel fork/join with constraint-solved dependency graph, acyclic validation, unique output enforcement
- Entity functions: `Name() -> Type { expression }` — pure, read-only, no effects
- `when property StageName { effects }` — stage subscriptions scoped to subscriber's current stage; automatic correlation via relationship graph
- Collection-aware subscriptions with quantifiers: `when all path Stage, Stage { ... }`, `when any path Stage { ... }`
- Compound `when` conditions: `when all reservations Reserved and payment Captured { transition to Target }`
- `schedule at expr { effects }` — time-based effect execution; auto-cancelled on stage exit
- `for var in coll where cond { effects }` — collection iteration with filter
- Collection query operations: `all`, `any`, `sum`, `count`, `first`, `filter`
- `match` expressions for pattern matching: `value: match { cond -> result, else -> result }`
- `domain Name[: kind]` header with `kind` validation (`service`, `cli`, `library`)
- Three deployment modes: in-memory, queue-backed, database outbox
- Cross-entity mutation rule enforcement
- Actor policy resolution in `require` on actions
- Implicit `event` variable in `when property Stage` subscription bodies
- MCP tools for all constructs

### Phase 3

Add:

- Library imports: `import "package-name" version N` — Poly-to-Poly package resolution, version compatibility validation, type extension checks
- Comments/annotations round-trip
- Compatibility aliases
- Migration/versioning support
- ACL / external API import syntax (exploratory — see Third-Party Contracts section)
- Lowering passes (REST, OpenAPI, schema generation)
- Scoped export (windowed DSL for agents)
- Agent-driven decomposition workflows


