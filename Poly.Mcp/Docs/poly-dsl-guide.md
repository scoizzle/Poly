# Poly DSL — Product Guide (Phase 1a/1b Surface)

> **Maintainer note:** This guide is the **single product-true reference** for the shipped DSL surface.
> It must be updated whenever the parser, printer, or `apply_dsl` changes.
> See the "DSL Guide Maintenance" section in `.github/copilot-instructions.md`.

> This is the **product-true** DSL guide verified against the shipped `apply_dsl` parser.
> Do **not** use constructs from experiment docs (`POLY-DSL-MINIMAL.md`, `DOMAIN-DSL-SPEC.md`) —
> they include lab syntax not accepted by the MCP tools.

---

## 0. Modeling Principles (Read This First)

These principles are enforced by analysis diagnostics and/or verified by smoke tests.
Violating them produces structural errors or warnings.

### 0.1 Entities Connect Through Relationships, Not Strings

A navigation property (`book: Book`) is a first-class link. A string field that stores an
identifier (`BookIsbn: Text`) is a code smell — it creates a manual join that analysis
cannot track, validate, or use for subscription routing.

```poly
// ✅ Correct — Loan references Book through a navigation property
Loan: entity {
  book: Book
}

// ❌ Wrong — string-based reference is invisible to analysis
Loan: entity {
  BookIsbn: Text
}
```

**Analysis check:** A property typed as `Text` with a name matching `*Id`, `*Code`, or `*Isbn`
on an entity that already has a relationship to that type triggers a diagnostic.

### 0.2 Transactional Records Own the Lifecycle

A `Loan` has stages (Active → Overdue → Returned). A `Book` is a catalog entry —
static data with no lifecycle. Business state lives on the record that connects
two entities, not on either endpoint.

| Pattern | Example | Lifecycle owner |
|---------|---------|----------------|
| Borrowing | Patron ↔ Loan ↔ Book | Loan |
| Order | Customer ↔ Order ↔ LineItem | Order |
| Reservation | Patron ↔ Reservation ↔ Book | Reservation |

```poly
// ✅ Correct — Loan carries the lifecycle
Loan: entity {
  book: Book
  borrower: Patron
  Active: stage { ... }
  Overdue: stage { ... }
  Returned: stage { }
}

// ❌ Wrong — Book shouldn't track borrowing stages
Book: entity {
  CheckedOut: stage { ... }   // Book is a catalog entry
}
```

### 0.3 `create in Relationship` Auto-Wires the Source

When you create an entity in a navigation property, the source entity reference
is automatically set. You only specify the *other* property initializers.

```poly
Patron: entity {
  loans: many Loan

  CheckOut: action (book: Book) {
    create in loans { book: book }   // borrower: this is implicit
  }
}
```

The generated C# sets `borrower` to `this` automatically. Do not set it explicitly
in the initializer — it would conflict with the auto-wire.

### 0.4 Cross-Entity Side Effects Use Subscriptions, Not Action Coupling

If entity A changing stage should produce an effect on entity B, use a stage
subscription (`when`). Don't put `create Fine` inside a Loan action — put it
where the effect belongs, in the entity that owns the fine.

```poly
// ✅ Correct — Patron subscribes to its own loans
Patron: entity {
  fines: many Fine

  when loans Overdue {
    create Fine { Amount: 5; Reason: "Overdue" }
  }
}

// ❌ Wrong — Loan shouldn't know about Fines
Loan: entity {
  Active: stage {
    transition to Overdue     // Loan just transitions
  }                           // Fine creation happens at the Patron level
}
```

### 0.5 Actions Guard With Require, Not Nested If

Business rules that gate an action should be named policies. Compound conditions
compose naturally.

```poly
// ✅ Correct — clear, testable guards
CheckOut: action (book: Book)
  require GoodStanding
  require not AtLimit
{
  create in loans { book: book }
}

// ❌ Avoid — inline conditions hide business rules
CheckOut: action (book: Book) {
  if (Status is "Active" and CurrentBorrowCount < MaxItems) {
    create in loans { book: book }
  }
}
```

### 0.6 Prefer `before` / `after` / Invariant Comments Over Effect Ordering Hacks

When entry and exit effects must happen in a specific order, use comments
to document why. Do not rely on effect-list ordering alone.

```poly
Suspended: stage {
  entry { assign MaxItems to 0 }    // freeze borrowing capacity first
  exit  { assign MaxItems to 5 }    // restore on reactivation
  Reinstate: action { ... }
}
```

---

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
| Soft-delete self | `delete` |
| Self-invoke | `invoke ActionName` / `invoke ActionName(param: expr, ...)` |
| Cross-entity invoke | `invoke [any\|all] RelName.ActionName` / `invoke [any\|all] RelName.ActionName(param: expr, ...) [where expr]` |
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
| `invoke any Rel.Action` / `invoke all Rel.Action` | Source of **OneToMany** `Rel`; **zero matches fail** (no vacuous `all`) |
| `… where expr` | Only with `any`/`all` on OneToMany; `expr` is **target-local** only |

**Rejected (`DMEFF007` / parse / runtime):**
- `any`/`all` without `Rel.`; `where` without `any`/`all`; `where` on self/singular
- bare `Rel.Action` on OneToMany; `any`/`all` on OneToOne
- reverse-side invoke (caller is relationship target, not source)
- ManyToOne / ManyToMany; self-relationship (same type both ends)
- filter with params, path-prefix, owned, exists, dates (local props/literals/comparisons/bool/arithmetic only)
- missing/duplicate action parameter bindings

```poly
invoke Validate                              # self-only
invoke Validate(status: "ready")             # self-only with args (all params required)
invoke service.Process                       # OneToOne source → target
invoke any services.Process                  # OneToMany first success
invoke all items.Process                     # OneToMany every target (fails if none)
invoke all items.Tag() where Size > 10       # filtered (target-local Size)
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

Expression JSON format (for `add_policy` / `simulate_policy`):

| Shape | JSON |
|-------|------|
| Comparison | `{"property":"Age","op":">=","value":18}` |
| AND | `{"and":[{...},{...}]}` |
| OR | `{"or":[...]}` |
| NOT | `{"not":{...}}` |
| Literal | `{"literal":true}` |

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
- **Related policies are authoring-complete** — they parse, apply, and export correctly. Full runtime evaluation (true/false via `evaluate_policy`/`simulate_policy` against linked instances) is a future enhancement. Today the `evaluate_policy` and `simulate_policy` tools evaluate local entity properties only — cross-entity expression evaluation through the VM graph traversal pipeline is not yet connected.

**Shipped in the current product surface:**
- Arithmetic (`+`, `-`, `*`, `/`) in expressions
- Conditional effects (`if (expr) { effects } else { effects }`)
- Invoke effect (`invoke ActionName` with optional arguments; cross-entity via `invoke RelName.ActionName`; quantifiers `any`/`all`; filter `where`)
- Action parameters (`actionName: action (param: Type, ...)`)
- `default` and `enum` constraints
- Owned navigation (`rel: owned Entity`)

**Not yet shipped** (planned for future phases):
- `any`/`all`/`none`/`count` over collections (Q3′ — **shipped**)
- Date operations
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
| Arithmetic (`+`, `-`, `*`, `/`) | ✅ | ✅ **shipped** | `Total + 5 > 10`, `Total * 0.9` |
| Action parameters | ✅ | ✅ **shipped** | `actionName: action (param: Text) { ... }` |

**JSON policies** (`add_policy` / `simulate_policy`) support comparison + and/or/not + literal only — **not** path-prefix, `Rel exists`, or `where`. Use DSL for related reads; JSON remains limited to local property comparisons and logical composition.

## 8. Supported Effect Summary

| Effect | Can appear in |
|--------|---------------|
| `transition to Stage` | action, entry, exit |
| `assign Prop to expr` | action, entry, exit |
| `create Type { ... }` | action |
| `create in Rel { ... }` | action |
| `delete` | action, entry, exit (soft-deletes the current instance) |
| `invoke Action` / `invoke [any\|all] Rel.Action [where …]` | action (self; OneToOne / OneToMany source-only; fail-closed DMEFF007; depth-limited) |
| `if (expr) { … } else if … else { … }` | action, entry, exit |

The following effects exist in the runtime library but have **no DSL syntax** yet:
- **link / unlink**: Connect existing instances. **Product path uses `create in Rel { ... }`** for graph writes instead (or `create` with `RelationshipName`). Link/Unlink remain available through the `DomainInstanceStore` library API for test code.
- **TransitionRelationship**: IR exists but **not executed at runtime** — do not use.

> **Note:** `delete` performs a **soft-delete** — it sets the `IsDeleted` flag on the current instance. Any subsequent `invoke_action` on a deleted instance is refused. This is not a typed mass-delete.

## 9. Do NOT Use (Unsupported in Phase 1a/1b)

| Construct | Why |
|-----------|-----|
| `actor` | Use `entity` instead |
| `value { }` | Value types not supported |
| `schedule`, `parallel`, `for` | Control flow not supported |

| `relationship Name from A to B` | Use N1 nav properties instead |
| `function` | Functions not supported |
| Event/publish/subscribe | Event model retired |

## 10. Additional Features

### Action Parameters

Actions can declare typed parameters. Bare identifiers in effect expressions resolve to the parameter when the name matches a declared parameter (same surface as property access). Parameters are injected for the duration of the action call and do not persist on the instance.

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
| Default | `default(value)` | `Status: Text default("Active")` |
| Enum | `enum(v1, v2, ...)` | `Color: Text enum(Red, Green, Blue)` |

## 11. Dual Authoring Path

**Batch** (`apply_dsl`): Write the full domain in `.poly` and apply in one shot.
**Replaces** the entire session domain — not merged incrementally.

**Incremental** (micro-tools): Use `add_entity`, `add_property`, `add_stage`,
`add_action`, `add_policy`, etc. for step-by-step construction.

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
