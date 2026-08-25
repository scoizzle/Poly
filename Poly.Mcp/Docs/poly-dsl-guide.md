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

### 0.3 `create in Relationship` Links the Source at Runtime

When you create an entity in a navigation property, the runtime store links the new
instance to the source — the child becomes reachable from the source through the
relationship. You only specify the *other* property initializers.

```poly
Patron: entity {
  loans: many Loan

  CheckOut: action (book: Book) {
    create in loans { book: book }   // the store link to this Patron is implicit
  }
}
```

**Note (back-reference materialization):** the runtime link is a store edge — it
drives path-prefix reads, quantifiers, and subscriptions from the source. The
**C# export auto-wires the child's back-reference** when the target entity has
exactly one singular navigation pointing back to the source (e.g. `borrower`
on `Loan` → `Patron`): the generated `Create{Nav}` factory passes `this` and the
back-ref is not a constructor parameter. When the back-ref is ambiguous (multiple
singular navs to the source) or a collection, it stays an unset constructor
parameter. To-one navigation bindings in `create in` initializers are **legal**
(e.g. `create in loans { book: book }`) — the binding flows into the child's
value bag / constructor parameter like a scalar initializer.

**Required coverage (DMEFF011):** every `required` **scalar** property of the
created entity must be provided in the `create` / `create in` initializers,
unless it has a `default`. Only scalar/enum-typed properties carry constraints —
navigation properties cannot be `required` (the DSL rejects `nav: Type required`
as a parse error), so the back-reference exemption simply means the auto-wired
nav needs no initializer value. Analysis rejects a create that omits a required
property — the generated `Create` factory would otherwise throw at runtime.

```poly
// ✅ Correct — both required props provided
Token: entity { Lexeme: Text required Kind: Text required }
Parser: entity {
  tokens: many Token
  Lex: action {
    create in tokens { Lexeme: "let" Kind: "Keyword" }
  }
}

// ❌ Wrong — DMEFF011: 'Lexeme' is required but not provided
// Parser2: entity {
//   tokens: many Token
//   Lex: action {
//     create in tokens { Kind: "Keyword" }
//   }
// }
```

### 0.4 Cross-Entity Side Effects Use Subscriptions, Not Action Coupling

If entity A changing stage should produce an effect on entity B, use a stage
subscription (`when`). Don't put `create Fine` inside a Loan action — put it
where the effect belongs, in the entity that owns the fine.

```poly
// ✅ Correct — Patron subscribes to its own loans
Patron: entity {
  fines: many Fine

  when loans Overdue {
    create Fine { Amount: 5 Reason: "Overdue" }
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

Every valid `.poly` document starts with a domain name. Optional `uses` lines list
**extensions** this compilation unit depends on (additive facts). Product `apply_dsl`
seeds `temporal` and `storage` when none are listed. Unknown or duplicate ids
fail closed. Another Poly domain is a `contract`, not a `uses` line.

```poly
domain MyDomain
uses temporal
uses storage
```

## 2. Enum Types

Enum types are declared at the top level, before entities that reference them.
Members are bare identifiers separated by optional commas (trailing comma allowed).

```poly
Color: enum {
  Red,
  Green,
  Blue,
}

MemberStatus: enum {
  Active
  Suspended
  Closed
}
```

### Value types

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

### Contracts (used sub-domains)

A contract is a **used sub-domain**: source + version + **value types** (the ACL you own)
+ endpoints. Bind attaches a parent action to an endpoint. No `import` keyword — an
OpenAPI pack will emit this same IR later. No generated client.

Value types declared **inside** the contract belong to that sub-domain. Action
parameters at the bind seam may use those types. **Stored entity properties may not.**

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
closed if the contract, endpoint, action, or parameter is missing, if the parameter
type does not match the payload, if a payload type is unknown, or if two contracts
(or the parent domain) share a value-type name.

The `InternalDomain` producer may fill the body of a `contract internal` from another
loaded domain; a hand-authored body is still legal and unchanged.

A `bind` is a **call in export**: the bound action's generated method invokes a
`{Contract}Adapters` adapter for the endpoint. Until an in-process adapter is registered,
the emitted adapter **throws** `NotImplementedException` — an unimplemented binding fails
closed at runtime, never a silent no-op. The binding is never dropped by export.

## 3. Entities and Properties

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
| Default | `default(value)` | `Status: MemberStatus default(Active)` |

Properties can reference enum types by name as their type, and use `default(MemberName)`
to set a default value:

```poly
Color: enum { Red, Green, Blue }

Item: entity {
  Name: Text required
  Color: Color default(Red)     // typed by enum, default by member name
  Status: MemberStatus default(Active)
}
```

### Annotations (Portable Storage Hints)

Annotations are metadata attached to entities (`table`) or properties (`column`)
that inform code generation without changing domain semantics. They require the
Sql annotation pack (enabled by default in MCP `apply_dsl` and the DslCompiler).

| Annotation | Scope | Syntax | Example |
|-----------|-------|--------|---------|
| `table` | Entity header | `table("NAME")` | `Patron: entity table("PATRON_MASTER") { … }` |
| `column` | Property tail | `column("NAME")` or `column("NAME","TYPE")` | `Name: Text column("PRODUCT_NAME")` or `Code: Text column("CODE", "VARCHAR2(20)")` |

- **`column("name")`** overrides the physical column name (default: camelCase property name).
- **`column("name", "type")`** additionally overrides the SQL column type (default: vendor pack type map, or core generic SQL).
- **`table("name")`** overrides the table name (default: pluralized entity name, e.g. `Item` → `Items`).
- Multiple annotations of the same keyword on the same target: the **last one wins** (no parse error).
- Unknown/unregistered annotations produce a parse error (fail-closed).

Annotations interleave with built-in constraints in the property tail. Order does
not matter:

```poly
Item: entity table("INVENTORY") {
  Code: Text unique column("CODE", "VARCHAR2(20)")
  Name: Text column("NAME") required
  Qty: Number range(0, ) column("QUANTITY")
}
```

Without the annotation library enabled, `column(...)` and `table(...)` produce a parse error.
This is deliberate — portable domain text stays DBMS-agnostic; storage hints are
an opt-in projection layer.
```

## 4. Navigation Properties (N1 Relationships Only)

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

**Relationship names are scoped to their source entity.** The same navigation name may
be declared on different source entities — e.g. two children each back-reference their
parent with a nav named `order` — but never twice on the same entity, and never
colliding with a property of the same name on that entity. Relationship references in
policies, subscriptions, `create in`, and `invoke` always resolve relative to the
entity they are authored on.

```poly
OrderLine: entity {
  order: Order
}

Note: entity {
  order: Order        // allowed — different source entity
}
```

## 5. Lifecycle Stages

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

## 6. Actions

```
action-name ":" "action" ["(" param-name ":" Type ("," ...)? ")"] ["->" ret-type] ["require" ...] "{" effect* "}"
```

Parameters (optional) appear **after** `: action`, keeping the uniform `Name: kind` member form (matches `export_dsl`):

```poly
Tag: action (value: Text) {
  assign Label to value
}
```

Return type (optional) appears after parameters using `-> TypeName`.

**Product path (entity return):** `-> EntityType` is supported when the action body
**creates** that type (`create Type {…}` or `create in Rel {…}`). Runtime
`InvokeAction` returns the created instance; MCP `invoke_action` exposes
`returnTypeName` and `returnInstanceId` for the registered child.

```poly
PlaceOrder: action -> Order {
  create in orders { Total: 100 }
}
```

Analysis **rejects** (DMEFF009) a declared `-> EntityType` when no create/create-in
produces that entity. Analysis also rejects (**DMEFF010**) when the create is **not the
final statement** — the create/create-in yielding the return value must be the last
statement of the action body (or every branch of a final `if … else` must produce it).
This pins the contract the C# export relies on (return value = last statement's created
instance) and the runtime return (`InvokeAction` returns the created instance):

```poly
// ✅ Correct — create is the final statement
PlaceOrder: action -> Order {
  assign Status to "processing"
  create in orders { Total: 100 }
}

// ✅ Correct — final conditional, every branch produces
Place: action -> Order {
  if (Rush is true) {
    create in orders { Code: "rush" }
  } else {
    create in orders { Code: "normal" }
  }
}

// ❌ Wrong — DMEFF010: create is not the last statement (transition after it)
// Lex: action -> Token {
//   create in tokens { Kind: Keyword }
//   transition to Parsing
// }
```

Primitive returns (`-> Number`) and “last assign is return” are **not** product — prefer entity create return or void.

```poly
// Not product: -> Number from assign alone (analysis error)
// ComputeDiscount: action (total: Number) -> Number { assign Result to total * 0.1 }
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
| Fan-out invoke | `for Rel as name [where name.PolicyName \| where name in StageName] invoke name.ActionName(param: expr, ...)` — OneToMany source only |
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
| `for Rel as name [where …] invoke name.Action` | Source of **OneToMany** `Rel` — fan-out over every matching record |

**Rejected (`DMEFF007` / parse / runtime):**
- `invoke Rel.Action` on OneToMany (fan-out requires `for`); `for` on OneToOne (iterating a known singular makes no sense)
- reverse-side invoke (caller is relationship target, not source)
- ManyToOne / ManyToMany; self-relationship (same type both ends)
- `for` predicate that is not a **named policy or stage membership** on the target entity
- `for` binder colliding with a caller member; missing/duplicate action parameter bindings

### Fan-out invoke (`for`)

`for Rel as name [where name.PolicyName | where name in StageName] invoke name.ActionName(args)`
iterates **every record** reachable via a OneToMany relationship (fetch-all from storage)
and invokes the action on each. One fan-out mode, no `any`/`all`/`each` quantifier.

- **Binder (`as name`)** names the current record; it is in scope for the predicate and the
  invoke arguments (`invoke line.Mark(amount: line Qty)`).
- **Predicate** must be a **named policy** (`where line IsPaid`) or a **stage membership**
  (`where line in Active`) on the **target entity** (the iterated record) — never an inline
  expression. The invariant analysis reasons about the policy like a `require` gate.
- **Fail-fast:** the first record whose invoke fails fails the whole `for` (the action
  returns `Failure`) — no silent swallow.
- **Zero matches fail** (no vacuous success).
- Rollback of already-invoked records is a documented gap (fail-fast guarantees the caller
  always sees the failure; atomic undo is not shipped).
- **Store-dependent predicates are runtime-only:** a predicate policy that needs the store
  (collection quantifiers, path-prefix reads, `Rel exists`) is **rejected at analysis** on
  the C# export path ("is store-dependent … cannot be compiled to standalone C#"). The
  runtime store path (MCP `create_instance` + `link_instances` + `invoke_action`) supports
  them. Use a **local policy over the record's own properties** for the standalone export.

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

## 7. Stage Subscriptions

```
"when" ["any"|"all"] relationship-name stage-name ("," stage-name)* ["as" binder-name] "{" effect* "}"
```

Subscriptions trigger when a related entity (reachable via the named relationship /
navigation property on the **subscriber**) enters one of the listed stages.

### Quantifier (optional: `any` / `all`)

The quantifier controls how the related-entity **set** is evaluated when a linked
target enters a matching stage. Omit it for the default **`Each`**:

| Keyword | Semantics |
|---------|-----------|
| *(omitted)* = **`Each`** | Fires effects for **every** matching transition (per-element; the default and the pre-p4 behavior) |
| `any` | Fires **once** when **at least one** linked related entity is in a matching stage |
| `all` | Fires **once** when **every** linked related entity is in a matching stage |

- `any`/`all` (and `Each`) all evaluate the **set state after the transition** —
  they do not fire "every peer bag at once". `all` only fires once the whole linked
  set is in a matching stage; until then transitions into the stage are ignored.
- `any`/`all` on a **singular** relationship (OneToOne / ManyToOne) are
  **rejected at analysis time** (error `DMSS003` — `SubscriptionContractMismatch`).
  The quantifier is meaningless there; omit it (`Each`) or change cardinality.
- **Reserved keywords:** `any`, `all`, `none`, and `count` are parsed as quantifier
  keywords in expression reads (`any Rel where …`, `count Rel`, …) and `any`/`all`
  as subscription quantifiers after `when`. A relationship literally named `any`,
  `all`, `none`, or `count` is **rejected at analysis** (structural failure) because
  it would be silently consumed as the quantifier in every read. Rename such
  relationships (e.g. `anyOrders`).
- Peer binder (`as name`) remains valid with `any`/`all`; the peer is the
  **transitioned** instance for that firing (same as `Each` today).

```poly
Pending: stage {
  when any orders Active {      // once any linked order is Active
    assign Status to "hasOrder"
  }
  when all orders Completed {   // once every linked order is Completed
    assign Status to "allDone"
  }
  when orders Active {          // default Each — per matching transition
    assign Status to "triggered"
  }
}
```

### Placement (stage vs entity-level)

| Placement | Active when | Optional `as name` |
|-----------|-------------|--------------------|
| **Stage-scoped** (`when` inside a stage body) | Only while the subscriber is **in that stage** | Yes — same peer rules |
| **Entity-level** (`when` on the entity, outside stages) | **Always-active** (any subscriber stage) | Yes — same peer rules |

Store notify runs stage-scoped handlers first, then entity-level. Both placements use the
same effect-binding fail-closed checks.

### Two shapes

| Shape | Form | Body may use peer fields? |
|-------|------|---------------------------|
| **Notification-only** | `when Rel Stage { … }` | No — subscriber props, literals, local effects only |
| **Peer-dependent** | `when Rel Stage as name { … }` | Yes — **scalar** path-prefix `name Prop` on the transitioned peer |

The optional **`as name`** binder names the **instance that just entered** one of
the listed stages for this firing (the related record). It is **not** an event
object and not the relationship collection. Allowed on **both** stage-scoped and
entity-level `when`.

- Bare property names in the body always mean the **subscriber**.
- Peer fields are available **only** through the binder (`order Code`, not a
  magic `event.*` root). Nested path-prefix under the binder (e.g. `order item Price`)
  is **not** supported — analysis rejects it.
- Notification-only subscriptions are intentional: many reactions need only the
  stage signal, not values from the related record.
- Path-prefix roots that are **not** a subscriber relationship require `as name`
  (analysis fail-closed — do not invent a peer root without a binder). Declaring
  `as name` without using it is allowed.
- Peer path-prefix is value-side only (RHS / conditions / initializers) — not an
  assign target.
- Multi-stage lists use the same binder for every listed stage:
  `when Tracks Active, Completed as order { … }`.
- **C# export:** the export consumes the analysis-published subscription dispatch plan
  (the same metadata the runtime uses) and emits a handler per subscription, named by
  quantifier: `WhenAny`/`WhenAll`/`WhenEach{Target}{Stage}`. Peer-dependent
  `when … as name` adds a parameter typed as the target entity and named as the binder;
  notify calls `handler(this)`. Binder path-prefix (e.g. `order Code`) lowers to that
  parameter (`order.Code`). Notification-only `when` stays zero-arg. Nested path-prefix
  under the binder is rejected (analysis + export).

```poly
// Notification-only — peer values not needed
Pending: stage {
  when orders Active, Completed {
    assign Status to "fulfilled"
  }
}

// Peer-dependent — copy a field from the order that just became Active
Pending: stage {
  when orders Active as order {
    assign LastCode to order Code
  }
}

// Entity-level (always-active) — same shapes, including optional peer binder
when orders Active as order {
  assign LastCode to order Code
}
```

The relationship name (`orders` above) is the correlation edge. The binder
(`order`) is a local name for the transitioned peer and need not match the
relationship name.

## 8. Policies

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
| Presence | `assignee exists` | `Exists(PropertyAccess)` — **store outbound-link presence** for relationship names |
| Absence | `not certificate exists` | `Not(Exists(...))` / `NotExists` — store-aware when target is a relationship |
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

#### Temporal Clock Dates (`Now`, `Today`, durations)

The **temporal library** (loaded by the product language default — MCP sessions and `ExtensionCatalog.Core.Language`)
ships clock date values and relative date arithmetic in expressions. These spellings parse to
`Now` / `Today` / `DateOperation` IR, pass analysis, round-trip through `export_dsl`, and are
authorable in **assign RHS and policy comparisons**:

```poly
Renew: action {
  assign DueDate to Now - 12 Days
  assign RenewedAt to Today - 3 Months
}

IsExpired: policy { ExpiryDate < Now }
Replenished: policy { DueDate + 14 Days > ExpiryDate }
```

| Form | Meaning | Notes |
|------|---------|-------|
| `Now` | Current UTC timestamp (clock read) | Exact spelling `Now`; folds to clock IR |
| `Today` | Current calendar date (clock read) | Exact spelling `Today`; folds to clock IR |
| `N Days` / `N Months` | Relative duration | Singular and plural accepted (`Day`, `Days`, `Month`, `Months`); exact PascalCase |
| `DateExpr + N Days` / `DateExpr - N Months` | Offset a date by a duration | `DateOperation` — `Now`, `Today`, or a `Date` property can be the left operand |

**Fail-closed (parse):** unknown units (`12 fortnights`) are a **parse error** — never a
vacuous `DateOperation` and never a dropped unit. A bare `Number + Days` with no temporal left
operand is rejected at analysis, never a silent numeric constant. `Date + Date` (clock nodes
or two `Date` properties) is rejected at analysis.

**Fail-closed (library not loaded):** without the temporal library, `Now` stays a plain `PropertyAccess`
(never a clock read), temporal authoring (`Now - 12 Days`, `5 Days`) fails at parse, and
`DateOperation` printing throws. Product sessions load Temporal via `uses temporal`
(or the SDK/MCP seed). A unit that does not list Temporal does not get clock meaning.

**Create-time defaults and assign-to-clock are shipped.** `default(Today)` / `default(Now)`
and `assign Prop to Today` / `assign Prop to Now` evaluate at instance create / invoke
(and the C# export emits a `??` coalesce). Offsets (`Now - 12 Days`) and **policy/VM**
reads of `Now`/`Today` are **not** shipped: the fixed-clock `TimeProvider` seam is a
blocked production gap (`simulate_policy` / the VM fail on static clock members,
`DirectVmAbiEmitter: unsupported node type NamedTypeReference`). Author and round-trip
those spellings; do not rely on policy evaluation of clock values.

**Explicitly NOT shipped:** `schedule at` / `at <time>`, business days, and timezone (TZ)
handling are out of scope — no `at 9am`, no business-day arithmetic, no TZ conversion.

**Rules:**
- `Rel Prop` on `many` relationships is invalid (use `any Rel where …` — Q3′ shipped). Cardinality validation is enforced at domain analysis time; the parser accepts the syntax but the analysis pipeline will reject it when relationship metadata is available.
- `Rel exists` on `many` is allowed (non-empty outbound-link intent). **Runtime is store-link presence** when domain-bound with a store — see dual paths below.
- Cross-entity reads (path-prefix, exists, where) are legal in policies, require, assign RHS, and **`if` conditions** (same preprocess as policy eval).
- Cross-entity writes (nav path as assign target) are banned.
- **Path-prefix / owned / quantifier / Rel exists** require a store + links when the name is an outbound relationship. Ownership (`SourceOwnsTarget`) is a modeling flag; evaluation uses the same outbound links as path-prefix / many.

  **Dual evaluation path (do not conflate):**
  - **Store + link (product path):** `create_instance` → `link_instances` → `evaluate_policy(…, instanceId=…)` **or** action/entry/exit/`if` bodies on store-attached instances. Resolves **singular path-prefix** (including **to-one multi-hop** e.g. `loan book Title is "Classic"`), **Q3′ quantifiers**, and **`Rel exists` / `not Rel exists`** against store outbound links. **Fail closed:** missing store/domain metadata throws. Empty links: path-prefix throws; `Rel exists` → **false**; `not Rel exists` → **true**. Bare path-prefix on `many` is analysis-rejected (use `any`/`all`); multi-link at any hop throws at eval.
  - **Standalone bag:** `evaluate_policy(age=…)` / `properties=…` — **local expressions only**. Non-relationship `Exists(PropertyAccess)` still bag-null-lowers; relationship-named `Rel exists` requires store (throws without it).

  For agent workflows: use `instanceId` + store + link for path-prefix, owned, quantifiers, and relationship `exists`.

**Shipped in the current product surface:**
- Arithmetic (`+`, `-`, `*`, `/`) in expressions
- Conditional effects (`if (expr) { effects } else { effects }`) — store-aware conditions (exists / path-prefix / quantifiers) preprocess like policies
- Invoke effect (`invoke ActionName` with optional arguments; cross-entity via `invoke RelName.ActionName`; fan-out via the `for` form — one mode, no `any`/`all`/`each` quantifier)
- Action parameters (`actionName: action (param: Type, ...)`)
- `default` constraints and enum-typed properties
- Owned navigation declaration (`rel: owned Entity`) + **to-one path-prefix** policy reads (single-hop and multi-hop chains; store + `link_instances` required)
- Temporal clock dates (`Now`/`Today`, `N days`/`N months`, `DateExpr ± duration`) — authoring, analysis, and `export_dsl` round-trip shipped; runtime clock eval is the only residual gap (see the temporal section)

**Not yet shipped** (planned for future phases / residual gaps):
- Date **runtime evaluation** — `Now`/`Today` clock reads parse, analyze, and round-trip (shipped), but executing them at runtime is blocked on the fixed-clock `TimeProvider` seam (`DirectVmAbiEmitter: unsupported node type NamedTypeReference`); authoring is not a gap, runtime evaluation is
- **Product DSL for IR-only `OwnedAccess`** (nested value-doc shape) — path-prefix → `RelationshipNavigation` is the product authoring surface; do not treat bag `OwnedAccess` as a second policy product path
- Dedicated many-owned policy demos beyond Q3′ quantifiers on plain `many` (ownership flag is unused at eval; use `any`/`all`/`none`/`count` on `many owned` the same way)

### Shipped-surface boundaries

Decisions that narrow the shipped claim so authoring fails loud instead of silently
diverging:

- **`unique` is enforced at the runtime instance store** (`DomainInstanceStore.Add` /
  property write) and projected to storage. The C# export's `Create` factory does not
  emit a uniqueness check — export uniqueness is the generated unique index, not a
  constructor guard. Duplicate values fail loud at the in-memory store.
- **`Now`/`Today`/`Guid` are authorable in `default(...)` and in assign RHS.** With the
  temporal library (default in MCP `apply_dsl`), `default(Today)` / `default(Now)` parse as
  clock IR and still **evaluate at create time** and in the C# export coalesce (same as
  the lowercase keyword forms). In **policy bodies**, `Now`/`Today` are authorable only
  with the pack — see the temporal section. A bare `now`/`guid` (lowercase runtime
  keyword) in a policy is still rejected at analysis ("property does not exist");
  `Now`/`Today` in a policy round-trip but their **policy/VM evaluation is not shipped**
  (fixed-clock `TimeProvider` seam is a production blocker). Date-comparing policies
  comparing two real properties (e.g. `DueDate < ReferenceDate`) remain the
  runtime-evaluable form.
- **`pattern(regex)` validates stored values at write time** (create/assign) — it is a
  constraint, not a query/read filter. Grep-style read-time matching against stored text
  is not expressible.
- **Store-dependent expressions are runtime-only on the standalone C# export.** The C#
  export lowers path-prefix reads and `Rel exists` / `not Rel exists` to standalone member
  access (to-one hops, count-vs-null checks for collections) — those compile. **Only**
  Q3′ collection quantifiers (`any`/`all`/`none`/`count`) cannot be lowered: the export
  emits a method that **throws `NotSupportedException` at call time** ("requires store-aware
  evaluation"); a `for` predicate using such a policy is **rejected at analysis**. The
  runtime store path (MCP `create_instance` + `link_instances` + `evaluate_policy` /
  `invoke_action`) is the supported evaluation surface for Q3′ forms. Author store-
  dependent expressions only when you run through the store, or keep policies local to the
  record's own properties for the export.
- **Relative date ordering is authorable but not runtime-evaluable.** Comparing a date
  property to `Now`/`Today` (e.g. `ExpiryDate < Now`, `DueDate + 14 Days > ExpiryDate`) is
  **shipped** for authoring — it parses, analyzes, and round-trips through `export_dsl` with
  the temporal library (default). **Runtime evaluation of `Now`/`Today` is not shipped**: the
  VM fails on static clock members (`NamedTypeReference`) until the fixed-clock `TimeProvider`
  seam lands, so such policies cannot yet be exercised via `simulate_policy` / `invoke_action`.
- **Expressions are type-checked at analysis.** Wrong-typed comparisons, assigns,
  arithmetic, and defaults (e.g. `Name >= 18` on a `Text` property, `default(Today)` on
  a `Number` property) are rejected at authoring time — the export and runtime no longer
  receive type-confused expressions.
- **Property constraints propagate onto effects.** An effect that assigns a value to a
  constrained property is checked against the property's constraints at analysis:
  a literal value (or a derived expression whose inferred value range is entirely
  outside, e.g. `assign Age to Age + 200` on `Age: Number range(0, 150)`) is an
  **error**; a derived expression that *can* fall outside (e.g. `assign Qty to Qty - 100`
  on `Qty: Number range(0, 100)`) is a **warning**. This covers `assign`, entry/exit
  effects, and `create`/`create in` initializers. The action's guard policies are
  considered **additively**: a `require` gate (or always-on entity-level policy) that
  narrows a property's value range is combined with the property's own constraints before
  judging the downstream violation — e.g. `assign Qty to Qty + 10` under
  `require Qty <= 80` on `Qty: Number range(0, 90)` is provably within range and produces
  no diagnostic.
- **Invariant verification is per-stage-context and combinatory.** An action valid in
  multiple stages is analyzed once per stage: each stage's policies (plus the action's
  require gates and entity policies) combine to a **net constraint per constraint type**
  (range, length, enum, … merged by intersection — the smaller maximum and the larger
  minimum win), and a downstream violation in **any** state the action can run is
  reported. Action **parameters** carry their own constraints into the effects that use
  them: the postcondition for `assign Total to amount` is the intersection of the
  property's and the parameter's constraints. Stage-scoped policies are a model-level
  surface; the DSL does not yet author them, but programmatically-populated stage
  policies participate in the per-stage narrowing.
- **Constraints must be jointly satisfiable.** A property whose constraints contradict
  each other is a structural error at analysis: disjoint ranges or lengths, differing
  patterns, contradictory equality constraints, an equality value or literal default that
  violates a sibling constraint. An action whose guard policies narrow a property to an
  **empty range** (the preconditions + property constraint can never hold) is reported as
  unsatisfiable — the action is un-runnable.
- **`if` conditions are implicit branch preconditions.** A conditional effect's
  then-branch is analyzed with the condition's bounds applied (e.g.
  `if (Qty >= 10) { assign Qty to Qty - 5 }` runs with Qty ∈ [10, …], so the assignment is
  judged against that narrower range); the else-branch uses the negated condition where it
  is a single comparison. This removes false-positive downstream warnings on guarded
  branches and is part of the same additive invariant model as guard policies.
- **Call-chain propagation.** `invoke B` runs B's effects under the caller's context: the
  caller's guard/if-condition narrowing is intersected with B's own preconditions, and the
  invoke argument bindings flow the bound expressions' value ranges into B's parameters.
  B's postconditions are recorded as effects the caller can trigger, with the
  call-chain-narrowed ranges (distinct from B's direct postconditions when B is invoked
  on its own). Those **call-chain postconditions are validated**: a callee assignment that
  can violate its target under the caller's context is reported as a diagnostic naming the
  chain (e.g. `A → B`), including cases the callee's own analysis cannot see (a parameter
  bound by the caller's argument whose range is only known at the call site).
- **Cross-entity fan-out and the predicate policy propagate the same way.** `for Rel as x
  [where x.Policy] invoke x.Action` builds the related entity's abstract environment (its
  declared constraints + its own preconditions) refined by the predicate's **named policy**
  (a require-gate-style refinement) before stepping the callee's effects — so `for lines as
  line where line IsHighQty invoke line.Mark()` with `IsHighQty: policy { Qty <= 40 }`
  analyzes `Mark`'s `assign Qty to Qty + 10` with Qty ∈ [0, 40] (range(0, 100) ∩ policy),
  giving a [10, 50] postcondition instead of the unfiltered [10, 110].

### Expression Gaps — IR vs DSL

The following expression capabilities exist in the runtime expression IR (`DomainExpression`)
and lowering pipeline but are **not yet authorable in product DSL**:

| Capability | IR exists | DSL status | Notes |
|------------|-----------|------------|-------|
| Relationship navigation | ✅ | ✅ **shipped** (path-prefix) | `customer Tier`, multi-hop `loan book Title is "X"` (to-one hops only); **store + link** at `evaluate_policy` |
| Existence check | ✅ | ✅ **shipped** (postfix `Rel exists`) | Store outbound-link presence for relationship names; fail-closed without store; empty → false |
| Scoped filter (`where`) | ✅ | ✅ **shipped** (`rel where and-chain`) | `customer where Status is "Active"` |
| Owned / related single-hop | ✅ | ✅ **shipped** (path-prefix) | `profile City is "Metropolis"` — same space-delimited syntax as to-one nav; **requires store + link** at `evaluate_policy` |
| Nested multi-hop path-prefix | ✅ | ✅ **shipped** (to-one hops) | `loan book Title is "Classic"`; many-middle requires `any`/`all` quantifiers |
| Collection quantifiers (`any`/`all`/`none`/`count`) | ✅ | ✅ **Q3′ shipped** | `any items where Status is "Open"`; store-aware runtime eval before VM lowering. |
| Arithmetic (`+`, `-`, `*`, `/`) | ✅ | ✅ **shipped** | `Total + 5 > 10`, `Total * 2 > 10` |
| Action parameters | ✅ | ✅ **shipped** | `actionName: action (param: Text) { ... }` |

**Expression bodies are DSL text only** — JSON expression bags were retired with the catalog minify.
`simulate_policy` is bag-only: relationship/owned path-prefix and relationship `exists` fail closed
without a store (use create + link + `evaluate_policy`).

## 9. Supported Effect Summary

| Effect | Can appear in |
|--------|---------------|
| `transition to Stage` | action, entry, exit |
| `assign Prop to expr` | action, entry, exit |
| `create Type { ... }` | action |
| `create in Rel { ... }` | action |
| `invoke Action` / `invoke Rel.Action` | action (self; OneToOne source-only; fail-closed DMEFF007; depth-limited) |
| `for Rel as name [where policy \| where in stage] invoke name.Action` | action (OneToMany source-only fan-out; fail-fast; zero matches fail) |
| `if (expr) { … } else if … else { … }` | action, entry, exit |

**Linking existing instances:** graph wiring happens through `create in Rel { … }`, which the runtime auto-links in the store. To connect already-existing instances, the MCP `link_instances` and `unlink_instances` tools expose `DomainInstanceStore.Link` / `DomainInstanceStore.Unlink` with relationship + entity-type validation at the tool boundary. There is no Link/Unlink **Effect IR** — linking existing instances is a store/tool operation only.

## 10. Do NOT Use (Unsupported in Phase 1a/1b)

| Construct | Why |
|-----------|-----|
| `actor` | Use `entity` instead |
| `schedule`, `parallel` | Not product constructs |

| `relationship Name from A to B` | Use N1 nav properties instead |
| `function` | Functions not supported |
| Event/publish/subscribe | Event model retired — stage transitions are the observable |
| `event.Prop` / `event Prop` in `when` bodies | Use optional peer binder: `when Rel Stage as name { … name Prop … }` |

## 11. Additional Features

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
| Default | `default(value)` | `Status: Text default("Active")` |
| Enum-typed property | `Prop: EnumType` | `Color: Color` (see §2 — top-level enum type; inline `enum(...)` constraints are not supported) |

### Annotations (portable metadata, not constraints)

`column(...)` and `table(...)` are **projection annotations**, not validation constraints.
They attach storage hints to properties/entities that codegen consumes (EF, DDL).
See §3 for the full syntax reference.

| Annotation | Scope | Example |
|-----------|-------|---------|
| `column` | Property tail | `Name: Text column("PROD_NAME")` |
| `table` | Entity header | `Item: entity table("INVENTORY") { … }` |

### Constraint propagation into transport contracts

Where the storage pack projects constraints into the DB (`CHECK` constraints), the
Minimal API transport projects them onto DTO validation attributes, so the API boundary
enforces the same envelopes the domain declares:

- `range(min, max)` on a `Number` property → `[Range(min, max)]` on the create DTO and on
  any **action DTO parameter** the value is directly assigned into
  (`assign Stock to amount` ⇒ `[Range(0, 1000)]` on `amount`). The numeric range uses the
  analysis-**verified** envelope when the invariant analysis proved no effect can exceed
  it; otherwise the declared range.
- `length(min, max)` → `[MinLength(min)]` + `[MaxLength(max)]`.
- `pattern(regex)` → `[RegularExpression(regex)]`.
- `required` → `[Required]` (reference-typed properties).
- Enum-typed properties and parameters (`Genre: Genre`) → `[EnumDataType(typeof(Genre))]`
  declares the member union on the contract (the CLR enum type already enforces
  membership at binding).

Action DTO bounds are **implicit**: not declared on the parameter, but derived from the
action's own effects — a parameter that flows into a constrained property inherits that
property's constraints, merged by intersection across all such targets. This covers
`assign Prop to param` on the action's entity and `create`/`create in` initializer
bindings (`Prop: param`) on related entities. Conflicting targets (e.g. different
patterns) merge to nothing and emit no attribute. A pinned literal in the model
(`EqualityConstraint`, evolution-only — not DSL) still projects as `[AllowedValues]`.
Author a closed set as an **enum**, not a property constraint.

**Soundness rules for implicit derivation:**
- Only **unconditional** flows contribute. A parameter that reaches a target only inside
  an `if` branch has no universally-provable envelope — intersecting the branch ranges
  would falsely reject valid inputs, so conditional assigns emit nothing (fail-closed).
- Open range bounds (`range(0, )`) keep their open side; the emitted `[Range]` caps it at
  the CLR type's representable bound rather than collapsing it.
- **Known gap:** a parameter passed through `invoke Rel.Action(param: expr)` does **not**
  yet inherit the callee's transitive envelopes (the callee's own DTO does).

## 12. Dual Authoring Path

**Batch** (`apply_dsl`): Write the full domain in `.poly` and apply in one shot.
**Replaces** the entire session domain — not merged incrementally.

**Incremental** (unified tools): Use `add(kind, payload)` to create one element
(entity, property, stage, action, stage_action, relationship, constraint, policy) and
`remove(kind, payload)` to delete one by identity.

**Golden workflow:** `get_dsl_guide` → write `.poly` → `apply_dsl` → `get_domain_analysis` →
oracle tools → iterate.

## 13. Example (Round-Trip Safe)

```poly
domain Orders
uses temporal
uses storage

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
