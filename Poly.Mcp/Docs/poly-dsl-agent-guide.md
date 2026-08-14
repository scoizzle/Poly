# Poly DSL — Agent Guide (Phase 1a/1b Product Surface)

> **Maintainer note:** This guide is the **single product-true reference** for the shipped DSL surface.
> It must be updated whenever the parser, printer, or `apply_dsl` changes.
> See the "DSL Guide Maintenance" section in `.github/copilot-instructions.md`.

> This is the **product-true** DSL guide verified against the shipped `apply_dsl` parser.
> Do **not** use constructs from experiment docs (`POLY-DSL-MINIMAL.md`, `DOMAIN-DSL-SPEC.md`) —
> they include lab syntax not accepted by the MCP tools.

---

## 1. Domain Header

Every valid `.poly` document starts with a domain name:

```poly
domain MyDomain
```

## 2. Entities and Properties

```
entity-name ":" "entity" "{" property* stage* action* policy* "}"
```

| Constraint | Syntax | Example |
|-----------|--------|---------|
| Required | `required` | `Name: Text required` |
| Unique | `unique` | `Email: Text unique` |
| Range | `range(min, max)` | `Age: Number range(0, 150)` |
| Length | `length(min, max)` | `Code: Text length(2, 10)` |
| Pattern | `pattern(regex)` | `Zip: Text pattern("^\\d{5}$")` |

```poly
Customer: entity {
  Name: Text required        //required text
  Age: Number range(0, 150)   //number with range
  Email: Text unique          //unique constraint
  IsActive: Boolean           //optional boolean
}
```

### Value types and Contracts (used sub-domains)

Named records without identity or lifecycle. Declare before entities that use them.
A property typed as the value type is a nested document, not a relationship.

```poly
Money: value {
  Amount: Number
  Currency: Text
}

Order: entity {
  Price: Money
}
```

A contract is a **used sub-domain**: source + version + **value types** (the ACL you own)
+ endpoints. Bind attaches a parent action to an endpoint. No `import` keyword. No
generated client. Value types declared **inside** the contract belong to that sub-domain;
action parameters at the bind seam may use them, **stored entity properties may not**.

```poly
Stripe: contract external stripe v1 {
  ChargeRequest: value {
    Amount: Number
    Currency: Text
  }
  Charge: outbound operation ChargeRequest
}

Order: entity {
  Pay: action (request: ChargeRequest) {
    assign Total to Total
  }
}

ChargeOrder: bind Stripe Charge to Pay request
```

`external` / `internal`, `inbound` / `outbound`, `operation` / `event`. Analysis fails
closed if the contract, endpoint, action, or parameter is missing, if the parameter type
does not match the payload, if a payload type is unknown, or if two contracts (or the
parent domain) share a value-type name.

A `bind` is a **call in export**: the bound action's generated method invokes a
`{Contract}Adapters` adapter for the endpoint. Until an in-process adapter is registered,
the emitted adapter **throws** `NotImplementedException` — an unimplemented binding fails
closed at runtime, never a silent no-op. The binding is never dropped by export. Export
surfaces the **composition root only** — a produced `contract internal` contributes value
types and endpoints, never child-entity routes.

## 3. Navigation Properties (N1 Relationships Only)

Relationships are declared as **inline navigation properties** on the source entity.
The legacy `relationship { }` top-level form is **not supported**.

```poly
Orders: entity {
  //One-to-many navigation to Customer
  customer: Customer
  //One-to-one navigation (default)
  invoice: Invoice
}

Customer: entity {
  orders: many Order            //one-to-many
  profile: one Profile          //explicit one-to-one
  passport: owned Passport      //source owns target
  lineItems: many owned LineItem // many + owned
}
```

Cardinality: `one` (default, can be omitted), `many`.
Ownership: `owned` marks `SourceOwnsTarget = true`.

## 4. Lifecycle Stages

```
stage-name ":" "stage" "{" stage-member-list "}"
```

Stages contain actions and subscriptions. Stages are **flat** — no parent/child hierarchy.

```poly
Draft: stage {
  Submit: action { transition to Active }
}
Active: stage { }
Done: stage { }
```

### Entry/Exit Effects

```poly
Active: stage {
  entry {
    assign Status to "entered_active"
  }
  exit {
    assign Status to "exited_active"
  }
  DoStuff: action { transition to Done }
}
```

## 5. Actions

```
action-name ":" "action" ["(" param-name ":" Type ("," ...)? ")"] ["require" ...] "{" effect* "}"
```

Parameters (optional) appear **after** `: action`, keeping the uniform `Name: kind` member form (matches `export_dsl`):

```poly
Tag: action (value: Text) {
  assign Label to value
}
```

Effects in an action body:

| Effect | Syntax |
|--------|--------|
| Stage transition | `transition to StageName` |
| Property assignment | `assign PropertyName to expression` |
| Create entity | `create EntityType { prop: value }` |
| Create in relationship | `create in RelationshipName { prop: value }` |
| Self-invoke | `invoke ActionName` / `invoke ActionName(param: expr, ...)` |
| Cross-entity invoke | `invoke RelName.ActionName` / `invoke RelName.ActionName(param: expr, ...)` — OneToOne source only |
| Conditional | `if (expr) { effects } [else if (expr) { effects }]* [else { effects }]` |

```poly
PlaceOrder: action {
  assign Status to "processing"
  create in orders { Total: 100 }
  transition to Active
}

Grade: action {
  if (Score >= 90) {
    assign Status to "A"
  } else if (Score >= 70) {
    assign Status to "B"
  } else {
    assign Status to "C"
  }
}

Submit: action {
  invoke Validate
  transition to Active
}
```

`else if` is sugar for a nested `if` in the `else` branch (round-trips as `else if`).

### Invoke

`invoke` chains another action on the same instance by default.

For cross-entity invoke (E3b), use relationship-dotted syntax from the **source** side only:
`invoke RelName.ActionName` — outbound links on `RelName` (caller must be the relationship source).

**Fail-closed policy:** reject ambiguous shapes now; relax only when analysis can prove the edge case.
Parser + analyzer (`DMEFF007`) + runtime all enforce the same contract.

**Shape rules:**
| Form | Allowed when |
|------|----------------|
| `invoke Action` | Self only |
| `invoke Rel.Action` | Source of **OneToOne** `Rel` — exactly one outbound link |
| `for Rel as name [where …] invoke name.Action` | Source of **OneToMany** `Rel` — fan-out over every matching record; **zero matches fail** |

**Rejected (`DMEFF007` / parse / runtime):**
- `invoke Rel.Action` on OneToMany (fan-out requires `for`); `for` on OneToOne
- `any`/`all` invoke quantifiers — removed; the `for` fan-out is the only mode
- reverse-side invoke (caller is relationship target, not source)
- ManyToOne / ManyToMany; self-relationship (same type both ends)
- `for` predicate that is not a **named policy or stage membership** on the target entity
- missing/duplicate action parameter bindings

```poly
invoke Validate                              # self-only
invoke Validate(status: "ready")             # self-only with args (all params required)
invoke service.Process                       # OneToOne source → target
for items as item where item IsEligible
    invoke item.Mark(amount: item Qty)       # fan-out, policy-filtered
for items as item where item in Active
    invoke item.Mark()                       # fan-out, stage-filtered
```

Nested invoke depth is limited (max 16); recursive cycles fail loud.

### Require Gates

Require gates reference **named policies** defined on the entity.
`require not PolicyName` negates the policy.

```poly
PositiveTotal: policy { Total > 0 }

Submit: action
  require PositiveTotal
{
  transition to Active
}
```

## 6. Stage Subscriptions

```
"when" relationship-name stage-name ("," stage-name)* "{" effect* "}"
```

Subscriptions trigger when a related entity enters one of the named stages.

```poly
Pending: stage {
  when orders Active, Completed {
    assign Status to "fulfilled"
  }
}
```

The relationship name refers to a navigation property on the same entity.

## 7. Policies

Policies are named boolean guard expressions attached to an entity.

```poly
IsAdult: policy { Age >= 18 }
CanProcess: policy { isActive is true and role is "admin" }
```

**Expressions are product DSL text only** — there is no JSON expression format. `add(kind: policy)`
and `simulate_policy` take the same DSL fragment syntax as policy bodies here.

### Expression Grammar (Shipped in Phase 1a/1b)

The DSL accepts boolean and scalar expressions through the following precedence levels
(highest to lowest):

| Level | Operators | Example |
|-------|-----------|---------|
| Primary | `Number`, `"string"`, `true`, `false`, `null`, `(expr)`, property name | `42`, `"hello"`, `true`, `Name` |
| Comparison | `==`, `!=`, `>`, `>=`, `<`, `<=`, `is`, `is not` | `Age >= 18`, `Status is "active"` |
| `not` | Prefix unary | `not Suspended` |
| `and` | Binary, left-assoc | `Age >= 18 and Status is "active"` |
| `or` | Binary, left-assoc | `Total > 0 or Rush is true` |

Valid expressions:
```poly
Age >= 18
isActive is true and role is "admin"
not Suspended
(Total > 0) or Rush is true
Status is "active"
```

#### Subject-First Related Reads (Q1′)

Policies and assign RHS can **read** data from related entities using subject-first syntax.
Cross-entity **writes** via assign are **banned** — only local entity properties can be written.

| Form | Example | Maps to |
|------|---------|---------|
| Path-prefix (bool prop) | `assignee Active` | `RelationshipNavigation` + `PropertyAccess` |
| Path-prefix (compare) | `customer Tier is "VIP"` | `RelationshipNavigation` + `Comparison` |
| Presence | `assignee exists` | `Exists` |
| Absence | `not certificate exists` | `Not(Exists(...))` — wraps `Exists` in `Not` |
| Multi-predicate | `customer where Status is "Active" and CreditLimit >= 1000` | `RelationshipNavigation` with `And` body |

```poly
assignee Active
customer Tier is "VIP"
assignee exists
not certificate exists
customer where Status is "Active" and CreditLimit >= 1000
```

#### Collection Quantifiers (Q3′)

Policies can **observe** and **evaluate** collection relationships with `any`, `all`, `none`, and `count`.
Quantifiers are evaluated at runtime against the instance store's linked targets.
These require a **OneToMany** relationship from the source entity. The body is an `and`-chain
(use parentheses for `or` inside the body).

| Form | Meaning | Example |
|------|---------|---------|
| `any Rel where body` | ∃ related matching body | `any orders where Priority > 5` |
| `all Rel where body` | ∀ related match body | `all items where Reserved is true` |
| `none Rel where body` | ¬∃ related matching body | `none notes where NeedsFollowUp is true` |
| `count Rel` / `count Rel where body` | Number of related (optionally filtered) | `count orders > 5` / `count orders where Status is "Open" > 0` |

`count` produces a numeric value for use in comparisons. `any`/`all`/`none` produce booleans.

**Empty collection semantics:**

| Form | When related set is empty |
|------|--------------------------|
| `any Rel where ...` | `false` |
| `all Rel where ...` | `false` (no vacuous true) |
| `none Rel where ...` | `true` (¬any) |
| `count Rel` | `0` |

```poly
HasPriorityOrder: policy { any orders where Priority > 5 }
AllHighValue: policy { all orders where Total > 100 }
NoRush: policy { none notes where NeedsFollowUp is true }
OpenOrderCount: policy { count orders where Status is "Open" > 0 }
TotalOrderCount: policy { count orders > 5 }
```

**Analysis rules:** relationship must be OneToMany from the source; body properties validated
against the target entity; reverse-side / self-rel / ManyToMany / OneToOne rejected (DMEFF007).


**Rules:**
- `Rel Prop` on `many` relationships is invalid (use `any Rel where …` — Q3′ shipped). Cardinality validation is enforced at domain analysis time; the parser accepts the syntax but the analysis pipeline will reject it when relationship metadata is available.
- `Rel exists` on `many` is allowed (non-empty check).
- Cross-entity reads (path-prefix, exists, where) are legal in policies, require, and assign RHS.
- Cross-entity writes (nav path as assign target) are banned.
- **Related policies are authoring-complete and runtime-evaluable** — they parse, apply, and export correctly. To-one path-prefix, `Rel exists`, `Rel where`, and Q3′ quantifiers (`any`/`all`/`none`/`count`) are all **runtime-evaluable** via `evaluate_policy` when the instance has been added to a store with linked targets.

  **Dual evaluation path:**
  - `evaluate_policy(age=…)` or `evaluate_policy(properties=…)` → standalone, no store, evaluates local expressions only.
  - `create_instance` → `link_instances(relationshipName=…)` → `evaluate_policy(entityName, policyName, instanceId=sourceId)` → **store-attached**, resolves cross-entity expressions (path-prefix, exists, where, Q3′ quantifiers) against linked targets.

  For agent workflows: use the `instanceId` path when the policy reads related data.

**Shipped in the current product surface:**
- Arithmetic (`+`, `-`, `*`, `/`) in expressions
- Conditional effects (`if (expr) { effects } else { effects }`)
- Invoke effect (`invoke ActionName` with optional arguments; cross-entity via `invoke RelName.ActionName`; fan-out via the `for` form — one mode, no `any`/`all`/`each` quantifier)
- Action parameters (`actionName: action (param: Type, ...)`)
- `default` constraints and enum-typed properties
- Owned navigation (`rel: owned Entity`)
- Temporal clock dates (`Now`/`Today`, `N days`/`N months`, `DateExpr ± duration`) — authoring, analysis, and `export_dsl` round-trip shipped

#### Temporal Clock Dates (`Now`, `Today`, durations)

With the temporal library (default in MCP `apply_dsl`), clock date values and relative date
arithmetic are authorable in assign RHS and policy comparisons:

```poly
Renew: action {
  assign DueDate to Now - 12 Days
  assign RenewedAt to Today - 3 Months
}

IsExpired: policy { ExpiryDate < Now }
Replenished: policy { DueDate + 14 Days > ExpiryDate }
```

| Form | Meaning |
|------|---------|
| `Now` | Current UTC timestamp (exact spelling) |
| `Today` | Current calendar date (exact spelling) |
| `N Days` / `N Months` | Relative duration (singular `Day`/`Month` also accepted; exact PascalCase) |
| `DateExpr + N Days` / `DateExpr - N Months` | Offset `Now`/`Today`/a `Date` property by a duration |

**Fail-closed:** unknown units (`12 Fortnights`) are a parse error; a bare `Number + Days`
with no temporal left operand is rejected at analysis; without the library `Now` stays a plain
`PropertyAccess` (never a clock read) and temporal authoring fails at parse.

**Create-time defaults and assign-to-clock are shipped.** `default(Today)` / `default(Now)`
and `assign Prop to Today` / `assign Prop to Now` evaluate at create/invoke. Offsets
(`Now - 12 Days`) and **policy/VM** clock reads are **not** shipped: the fixed-clock
`TimeProvider` seam is a production blocker (`simulate_policy` / the VM fail on
`NamedTypeReference`). Author and round-trip those spellings; do not rely on policy
clock values.

**Explicitly NOT shipped:** `schedule at`/`at <time>`, business days, and timezone (TZ)
handling are out of scope.

**Not yet shipped** (planned for future phases):
- Date **runtime evaluation** — `Now`/`Today` clock reads parse, analyze, and round-trip
  (shipped), but executing them at runtime is blocked on the fixed-clock `TimeProvider` seam
- Owned/nested access in expressions

### Expression Gaps — IR vs DSL

The following expression capabilities exist in the runtime expression IR (`DomainExpression`)
and lowering pipeline but are **not yet authorable in product DSL**:

| Capability | IR exists | DSL status | Notes |
|------------|-----------|------------|-------|
| Relationship navigation | ✅ | ✅ **shipped** (path-prefix) | `customer Tier`, not `customer.Tier` |
| Existence check | ✅ | ✅ **shipped** (postfix `Rel exists`) | `assignee exists` / `not assignee exists` |
| Scoped filter (`where`) | ✅ | ✅ **shipped** (`rel where and-chain`) | `customer where Status is "Active"` |
| Owned/nested access | ✅ | Pull (same path-prefix approach) | `profile Field is "x"` — not `profile.Field` |
| Collection quantifiers (`any`/`all`/`none`/`count`) | ✅ | ✅ **Q3′ shipped** | `any items where Status is "Open"`; store-aware runtime eval before VM lowering. |
| Arithmetic (`+`, `-`, `*`, `/`) | ✅ | ✅ **shipped** | `Total + 5 > 10`, `Total * 2 > 10` |
| Action parameters | ✅ | ✅ **shipped** | `actionName: action (param: Text) { ... }` |

**Expression bodies are DSL text only** — JSON expression bags were retired with the catalog minify.
`simulate_policy` is bag-only: relationship/owned path-prefix and relationship `exists` fail closed
without a store (use create + link + `evaluate_policy`).

## 8. Supported Effect Summary

| Effect | Can appear in |
|--------|---------------|
| `transition to Stage` | action, entry, exit |
| `assign Prop to expr` | action, entry, exit |
| `create Type { ... }` | action |
| `create in Rel { ... }` | action |
| `invoke Action` / `invoke Rel.Action` | action (self; OneToOne source-only; fail-closed DMEFF007; depth-limited) |
| `for Rel as name [where policy \| where in stage] invoke name.Action` | action (OneToMany source-only fan-out; fail-fast; zero matches fail) |
| `if (expr) { … } else if … else { … }` | action, entry, exit |

The following effects exist in the runtime library but have **no DSL syntax** yet:
- **link / unlink**: Connect existing instances. The MCP `link_instances` and `unlink_instances` tools expose `DomainInstanceStore.Link` / `DomainInstanceStore.Unlink` with relationship + entity-type validation at the tool boundary. **DSL has no `link` keyword** — the product graph-write path for spawn-and-wire remains `create in Rel { … }`. There is no Link/Unlink **Effect IR**.

> **Note:** the `delete` soft-delete effect was removed (2026-08-10). There is no delete effect; do not author `delete`.

## 9. Do NOT Use (Unsupported in Phase 1a/1b)

| Construct | Why |
|-----------|-----|
| `actor` | Use `entity` instead |
| `schedule`, `parallel` | Not product constructs |
| `schedule`, `parallel`, `for` | Control flow not supported |

| `relationship Name from A to B` | Use N1 nav properties instead |
| `function` | Functions not supported |
| Event/publish/subscribe | Event model retired |

## 10. Additional Features

### Action Parameters

Actions can declare typed parameters. There is no `param` keyword: a parameter is a **bare identifier** — the product parser emits it as a property access, and analysis/lowering/invoke-bindings/runtime treat an identifier matching an in-scope action parameter as a **parameter**, not a property. `ParameterAccess` is internal IR, not an authoring form. Parameters are injected for the duration of the action call and do not persist on the instance.

Canonical form puts parameters after `: action` so members stay `Name: kind` (matches `export_dsl`):

```poly
SetName: action (newName: Text) {
  assign Name to newName
}
```

### Constraint Reference

| Constraint | Syntax | Example |
|-----------|--------|---------|
| Required | `required` | `Name: Text required` |
| Unique | `unique` | `Email: Text unique` |
| Range | `range(min, max)` | `Age: Number range(0, 150)` |
| Length | `length(min, max)` | `Code: Text length(2, 10)` |
| Pattern | `pattern(regex)` | `Zip: Text pattern("^\\d{5}$")` |
| Default | `default(value)` | `Status: MemberStatus default(Active)` |
| Enum-typed property | `Prop: EnumType` | `Color: Color` (top-level enum type; inline `enum(...)` constraints are not supported) |

## 11. Dual Authoring Path

**Batch** (`apply_dsl`): Write the full domain in `.poly` and apply in one shot.
**Replaces** the entire session domain — not merged incrementally.

**Incremental** (unified tools): Use `add(kind, payload)` to create one element
(entity, property, stage, action, stage_action, relationship, constraint, policy) and
`remove(kind, payload)` to delete one by identity.

**Golden workflow:** `get_dsl_guide` → write `.poly` → `apply_dsl` → `get_domain_analysis` →
oracle tools → iterate.

## 11. Example (Round-Trip Safe)

```poly
domain Orders

Customer: entity {
  Name: Text required
  Email: Text unique
  orders: many Order
}

Order: entity {
  Total: Number range(0, )
  Status: Text
  PositiveTotal: policy { Total > 0 }

  Draft: stage {
    Submit: action
      require PositiveTotal
    {
      transition to Active
    }
  }
  Active: stage {
    entry { assign Status to "active" }
    exit  { assign Status to "archived" }
  }
  Done: stage { }
}
```

This domain parses, analyzes clean, and round-trips through `apply_dsl` → `export_dsl`.
