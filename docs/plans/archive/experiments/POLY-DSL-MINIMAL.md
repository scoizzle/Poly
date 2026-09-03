# Poly DSL — Minimal Spec (v0.3)

Use this spec to decompose a software system into Poly DSL entities, values, actors, stages, subscriptions, and effects. Emit valid `.poly` files.

## Domain header

```
domain Name[: kind]     // kind: service (default), cli, library
```

## Top-level types

### `Name: value { props functions }`
Data shape. No identity, compared by content. Pure functions only — no effects, no stages.

```poly
Money: value {
  amount: Number range(0, ) required
  currency: Text length(3, 3) required
  add(other: Money) -> Money {
    require { this.currency is other.currency }
    Money { amount: this.amount + other.amount, currency: this.currency }
  }
}
```

### `Name: entity { props functions stages actions policies }`
Has identity, lifecycle stages, relationships. Root unit of decomposition.

### `Name: actor { same as entity + policies }`
Entity that participates in authorization. Policies evaluate against the actor instance.

### `Name: Parent { ... }`
Extends Parent. Inherits properties, stages, policies. If Parent is an actor, child is too.

## Properties & Relationships

```
name: Type                      // one-to-one, not owned
name: many Type                 // one-to-many, not owned
name: owned Type                // one-to-one, owner cascades
name: many owned Type           // one-to-many, owner cascades
```

### Constraints
```
range(min, max)   length(min, max)   pattern(regex)   required   unique
```

## Functions
Pure, read-only, no effects. Last expression is implicit return:
```poly
total() -> Money { lineItems.sum(i => i.subtotal()) }
```

## Stages (Entity Lifecycle)

```poly
StageName: stage {
  entry require Policy, Property     // gate: blocks entry
  { effects }                        // auto-effects on entering

  exit require { condition }         // gate: blocks exit
  { effects }                        // auto-effects on leaving

  ActionName: action { effects }     // action available in this stage
}
```

Default initial stage = first stage declared.

## Actions

```poly
ActionName: action(Param: Type) -> ReturnType {
  effects
}
// Zero-ceremony (infers transition from name):
Draft: stage { Submit: {} }   // Submit → Submitted
```

### Gates
```
Action: action
  when StageA, StageB          // OR — available in any listed stage
  require PolicyA, PolicyB     // AND within line
  require PolicyC              // OR across lines
{ effects }
```
- `when`: stage names only, OR semantics.
- `require`: policy names, qualified names (`Entity.Policy`), `not Policy` prefix. AND within line, OR across lines. Empty = no auth.

### Effects
```
transition to Stage           // move to lifecycle stage. Inherently observable.
assign property to expr       // set property
create Entity { prop: val }   // create new entity
create in relation { prop }   // create child, add to owned collection
create name in relation { }   // create child with local binding
invoke target.Action(params)  // call action on reachable entity (relationship path)
start EntityName(params)      // initialization pattern
```

### Iteration
```
for var in collection where cond { effects }
```

### Parallel fork/join
```
parallel {
  step require dep1, dep2 { effects }    // runs once deps available
  step require dep3       { effects }    // runs in parallel when deps ready
}
// Blocks until all steps complete. Deps resolved from entity properties
// and sibling step outputs. No cycles. Assign targets unique across steps.
```

### Schedule
```
schedule at DateTimeExpr { effects }    // fires once at time. Cancelled on stage exit.
```

## Subscriptions — `when property Stage`

Reacts to a related entity's stage transition. Scoped to subscriber's current stage — leaves stage = unsubscribes.

```poly
Order: entity {
  payment: Payment

  Awaiting: stage {
    when payment Captured {       // fires when this.payment enters Captured
      transition to ReadyToShip   // can drive auto-advancement
    }
    when payment Failed {
      transition to PaymentFailed
    }
  }
}
```

### Quantifiers for `many` relationships
```
when items Stage                 // fires per element
when any items Stage             // fires once when any is in Stage
when all items Stage             // fires once when all are in Stage
when all items not Stage         // fires once when none are in Stage
```

Multiple stages: `when all targets Scanned, Errored, Skipped`
Compound conditions: `when payment Captured and all items Reserved`

### `event` variable
Inside a `when` body, `event` is the transitioning entity instance. `this` is the subscriber.

## Policies

```poly
PolicyName: policy { boolExpression }
PolicyName: policy external
```

### Expression grammar
```
! / not (highest precedence)
is / is not / == / != / > / >= / < / <=
&& / and
|| / or (lowest)
( ) grouping
```
Literals: numbers, `true`, `false`, `"strings"`, `null`
Identifiers: `actor` (caller), `this` (current entity/value)
Members: `DateTime.Now`, `Text.Empty`

### Collection operations (on `many` properties)
```
items.all(i => i.stage is Completed)    // Boolean
items.any(i => i.priority is "high")    // Boolean
items.count                              // Number
items.sum(i => i.total)                 // value type
items.first(i => i.isUrgent)            // entity or null
items.filter(i => i.priority is "high") // collection
```

### Match expressions
```
value: match {
  this.hasMatches()  -> 0
  this.code is 2     -> 2
  else               -> 1
}
```

## Name Resolution
1. Current entity (stage/policy name)
2. Parent entity
3. Domain scope (`actor`)

Qualified names (`Employee.Warehouse`) bypass hierarchy. Single namespace per entity — no duplicate names. Entity names globally unique.

## Cross-Entity Mutation Rule
An entity cannot directly mutate another entity's properties. Use `invoke target.Action(params)` or `when property Stage` subscriptions.

## Decomposition Pattern
1. Identify nouns → Entities. Properties → columns/fields.
2. Identify value objects → Value types (Money, Address, DateRange).
3. Identify lifecycle phases → Stages. Transitions between stages → Actions.
4. Identify authorization actors → Actors with policies.
5. Identify cross-entity reactions → `when property Stage` subscriptions.
6. Identify time-based behavior → `schedule at { effects }`.
7. Identify bulk operations → `for var in coll where cond { effects }`.
8. Identify independent computations → `parallel { step ... }`.

## Example output structure
```poly
domain MySystem: service

// Value types
SomeValue: value { ... }

// Actors
User: actor { ... }

// Entities
CoreEntity: entity {
  // Properties
  // Functions
  // Stages with entry/exit, actions, when subscriptions
  // Policies
}
```
