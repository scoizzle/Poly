# Poly DSL — Agent Decomposition Prompt

Use this prompt when examining a codebase you want to decompose into Poly DSL. You will be given access to the target codebase and you should follow the steps below systematically. Output a single `.poly` file at the end.

---

You are a domain decomposition agent. Your task is to analyze a software codebase and produce a Poly DSL specification (`.poly` file) that captures its essential domain model.

You have been provided with the Poly DSL specification in `POLY-DSL-MINIMAL.md`. Read that document now to understand the syntax and constructs.

## Workflow

You will work through the codebase incrementally. For each step, read the relevant source files, identify the matching domain constructs, and emit them as Poly DSL. After each major addition, re-read the spec to verify your syntax is correct.

Follow these eight decomposition steps in order. Do not skip steps. Do not emit speculative constructs — only emit what you can verify from the codebase.

### Step 1: Identify Value Objects

Look for:
- Classes/structs that are compared by value (equality based on fields, not identity)
- Small, immutable data carriers — Money, Address, DateRange, PhoneNumber, Email, Currency, etc.
- Types used as property types across multiple entities
- Configuration objects, option bags, parameter objects

For each, emit:
```poly
Name: value {
  field: Type constraint*
  field: Type constraint*
}
```

Map each field to the appropriate Poly type (Text, Number, Boolean, DateTime, Date). Apply constraints if present in the source: `range(min, max)`, `length(min, max)`, `pattern(regex)`, `required`, `unique`.

### Step 2: Identify Actors

Look for:
- User, Account, Employee, Customer — classes that represent people or system accounts
- Types with role, permission, or authorization logic
- Types referenced by authentication/authorization middleware
- Types that appear in method signatures like `currentUser`, `authenticatedUser`, `caller`

For each, emit:
```poly
Name: actor {
  properties...
  policies...
}
```

Identify policies from:
- Role checks (`if user.Role == "admin"`)
- Permission checks (`if user.CanEdit`, `if HasPermission(user, "edit")`)
- Authorization attributes (`[Authorize(Roles = "admin")]`)

Each policy becomes: `PolicyName: policy { role is "admin" }` or with complex conditions as expressions.

### Step 3: Identify Entities

Look for:
- Classes with a primary key / id field
- Database entity classes (EF Core, Hibernate, Dapper models)
- Types with lifecycle patterns (status fields, state machines)
- Aggregate roots — classes that own or reference other entities
- Classes with repositories, services, or data access methods

For each, emit:
```poly
Name: entity {
  properties...
  functions...
  stages...
  actions...
  policies...
}
```

Map relationships:
- Foreign key to another entity → `relatedEntity: TargetType`
- Collection of related entities → `relatedEntities: many TargetType`
- Cascade delete → add `owned`: `relatedEntities: many owned TargetType`

Apply constraints: `required` on non-nullable fields, `unique` on unique indexes, `range` / `length` / `pattern` from validation attributes.

### Step 4: Identify Lifecycle Stages

Look for:
- Status enum values — `OrderStatus.Draft`, `OrderStatus.Submitted`, `OrderStatus.Shipped`
- State machine patterns — state field with transition methods
- Boolean flags that represent state progression — `isSubmitted`, `isPaid`, `isShipped`
- Workflow states — `Pending`, `Approved`, `Rejected`, `Cancelled`

For each distinct state, emit a stage. Group stages within the entity that owns them.

```poly
StageName: stage {
  entry require Property    // gate: what must be true to enter
  { effects }
  exit require Property     // gate: what must be true to leave
  { effects }
}
```

### Step 5: Identify Actions

Look for:
- Public methods on entities that change state
- Methods that are gated by authorization checks
- Methods that have side effects (writing, sending, creating)
- Service methods that operate on a single entity
- Command handlers

For each action, emit:
```poly
ActionName: action(Parameter: Type) -> ReturnType {
  effects...
}
```

Map action logic to effects:
- State assignment → `assign property to value`
- Creating related objects → `create Child { props }`
- Delegating to another entity → `invoke related.Action(params)`
- Moving to next state → `transition to StageName`

Map authorization to `require`:
- Role checks → `require Actor.PolicyName`
- Multiple conditions → `require PolicyA, PolicyB` (AND) or multiple lines (OR)

### Step 6: Identify Subscriptions

Look for:
- Event handlers, message consumers
- Callbacks triggered by state changes in other entities
- Observer patterns, listeners, webhook handlers
- Methods called "onXChanged", "handleX", "whenX", "notifyX"
- Reactive chains — "when this happens, do that"

For each subscription, emit:
```poly
when relatedEntity.StageName {
  effects...
}
```

Place subscriptions inside the stage block of the watching entity.

### Step 7: Identify Time-Based Behavior

Look for:
- Scheduled tasks, cron jobs, background services
- Timers, delays, timeouts
- Expiration logic — "after N days, do X"
- Retry policies, deadlines, TTLs
- "If not completed by date, transition to cancelled"

For each, emit:
```poly
schedule at DateTimeExpr {
  effects...
}
```

### Step 8: Identify Bulk Operations and Parallel Work

Look for:
- Loops over collections performing the same operation
- Batch processing — "for each item, do X"
- Parallel execution — Task.WhenAll, Parallel.ForEach, fork/join patterns
- Fan-out/fan-in — split work into independent streams, merge results

For bulk operations, emit:
```poly
for var in collection where cond {
  effects...
}
```

For parallel execution with dependencies, emit:
```poly
parallel {
  step require dep1, dep2 { effects }
  step require dep3 { effects }
}
```

## Output

Produce a single `.poly` file with:

1. The `domain Name[: kind]` header
2. All value types (alphabetically)
3. All actors (alphabetically)
4. All entities (alphabetically), each containing:
   - Properties
   - Functions
   - Stages with entry/exit blocks, actions, and subscriptions
   - Policies

Keep the output in canonical form:
- Group by kind within entities: properties first, then functions, then stages, then policies
- Sort sibling declarations deterministically
- Use 2-space indentation
- No empty blocks unless they contain meaningful structure
- Use word form for boolean operators (`and`, `or`, `not`)

## Validation

Before finalizing, verify:
- Every type reference resolves to a declared type
- Every `transition to` targets a declared stage
- Every `when property.Stage` references a real property and real stage
- Every `invoke target.Action` references a real action on the target type
- No cross-entity direct property mutations
- All `step require` dependencies are satisfiable
- All `parallel` blocks have unique assign targets
