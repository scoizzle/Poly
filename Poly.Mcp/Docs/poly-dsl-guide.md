# Poly DSL — Product Guide (Phase 1a/1b Surface)

> **Maintainer note:** This guide is the **single product-true reference** for the shipped DSL surface.
> It must be updated whenever the parser, printer, or `apply_dsl` changes.
> Keep this file in sync with the parser, printer, and MCP authoring tools (`AGENTS.md` Ops).

> This is the **product-true** DSL guide verified against the shipped `apply_dsl` parser.
> Do **not** use constructs from experiment docs (`POLY-DSL-MINIMAL.md`, `DOMAIN-DSL-SPEC.md`) —
> they include lab syntax not accepted by the MCP tools.

> **MCP surface:** `poly_guide`, `poly_workspace`, `poly_define`, `poly_check`,
> `poly_inspect`, `poly_export`, `poly_simulate`. The 30-tool v1 surface was removed;
> names such as `apply_dsl` in older sections refer to it historically.
> `poly_inspect` takes one element path as `select`: `Entity`, `Entity.Member`, `Entity.Stage.Action`, `Enum.Member`, or an enum, value type or function name; an unresolvable path returns `ELEMENT_NOT_FOUND` with candidates. A same-named entity action and stage action make `Entity.Action` ambiguous: use `Entity.Stage.Action`, or the domain-prefixed `Domain/Entity/Action` (which prefers the entity-level member) for the entity-level one.
> `poly_check` with `select: "Entity.Action"` adds `data.witness`: one entry per stage context
> (`stageName`, `preconditions`, `properties[].min/max`, `postconditions`) copied from the
> analyzer's range/precondition facts; absent when the analysis published none. Only an
> `Entity.Action` select carries a witness; a stage-qualified `Entity.Stage.Action` select does not.
> `poly_check` with an `expression` proves one DSL fragment against the workspace's own model: the
> declared `function`s and `enum`s are in scope, and a bound property keeps its declared type, so
> `Level is Senior` reads the enum member while an undeclared name is typed by its literal. Every
> property the fragment reads needs a value in `bindings`; a name it reads with no binding fails
> closed and names it. An expression that reads no property needs no `bindings`
> (`Within(5, 1, 10)`).
> A `select` that names no element is `ok:false` with `ELEMENT_NOT_FOUND`, the same answer
> `poly_inspect` gives, so a typo cannot read as an undecided proof.
> `poly_export` inlines an artifact up to 4 KB; a larger one is written to the OS temp directory as
> `poly-<workspace>-<format>-<sha16>.<ext>` and returned as `{path, bytes, sha256}`. Identical content
> reuses the same file, and `poly_workspace(op:"close")` removes the files the workspace wrote.
> Each result's text block is a prose summary (outcome, `workspace:` handle, findings, `next:`
> calls, flattened data); the full envelope is in `structuredContent`.
> `poly_check` findings carry the source `line`/`column` when the stored document's outline reaches
> the element, the same position `poly_define` reports; `data.hints` lists the hints the summary
> counts (`data.hintCount` is the total, up to 20 are listed).

---

## 0. Modeling Principles (Read This First)

These principles are enforced by analysis diagnostics and/or verified by smoke tests.
Violating them produces structural errors or warnings.

### 0.1 Entities Connect Through Relationships, Not Strings

A navigation property (`book: Book`) is a first-class link. A string field that stores an
identifier (`BookIsbn: Text`) is a code smell — it creates a manual join that analysis
cannot track, validate, or use for subscription routing.

<!-- poly-verify: skip:illustration owner:M-02 -->
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

<!-- poly-verify: skip:illustration owner:M-02 -->
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

<!-- poly-verify: fragment:top wrapper:patron-loans -->
```poly
Patron: entity {
  loans: many Loan

  CheckOut: action (book: Book) {
    create in loans { book: book }   // the store link to this Patron is implicit
  }
}
```

**Note (back-reference materialization):** the runtime link is a store edge — it drives
path-prefix reads, quantifiers, and subscriptions from the source. The **C# export
auto-wires the child's back-reference**: the singular navigation the collection's `via`
names (or whose `via` names it), else the only one on the target that points back
(`borrower` on `Loan` → `Patron`); `Create{Nav}` passes `this`, so it is not a constructor
parameter, and a collection back-ref stays unset. Two or more singular navigations back
with no `via` pairing is **DMREL005**. To-one bindings in `create in` initializers are
**legal** (`create in loans { book: book }`) and flow in like a scalar. **A supplied
collection argument wires the pair the other way (#572):** `Patron.Create(loans: kids)`
sets each child's paired to-one (`kid.Borrower`) and refuses a child already paired with
a different owner (a Link failure) instead of re-pointing it.

**Required coverage (DMEFF011):** every property **without** `optional` on the
created entity must be provided in the `create` / `create in` initializers, unless it has
a `default` or the initial stage's `entry` effects assign it. A to-one navigation (§4) must
be bound (`create in events { hall: hall }`) unless it is `optional` or the back-reference
`create in` fills: "navigation 'patron' is required: bind it, or declare it `optional`".
`required` is not a constraint word. A null one at run time (a binding whose value is
null) fails Required in the generated `Create`, simulated or printed.

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
// ✅ Correct — both props provided (a property is required by default)
Token: entity { Lexeme: Text Kind: Text }
Parser: entity {
  tokens: many Token
  Lex: action {
    create in tokens { Lexeme: "let" Kind: "Keyword" }
  }
}

// ✅ Correct — Note opted out, so it may be omitted
Draft: entity { Lexeme: Text Note: Text optional }
Parser2: entity {
  drafts: many Draft
  Lex: action {
    create in drafts { Lexeme: "let" }
  }
}

// ❌ Wrong — DMEFF011: 'Lexeme' is required but not provided
// Parser3: entity {
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

<!-- poly-verify: skip:illustration owner:M-02 -->
```poly
// ✅ Correct — Patron subscribes to its own loans
Patron: entity {
  fines: many Fine

  when loans Overdue {
    create in fines { Amount: 5 Reason: "Overdue" }
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

<!-- poly-verify: skip:illustration owner:M-02 -->
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

<!-- poly-verify: skip:illustration owner:M-02 -->
```poly
Suspended: stage {
  entry { assign MaxItems to 0 }    // freeze borrowing capacity first
  exit  { assign MaxItems to 5 }    // restore on reactivation
  Reinstate: action { ... }
}
```

---

Every valid `.poly` document starts with a domain name. Optional `uses` lines list
**extension ids** this compilation unit depends on. `polyc build` loads the implicit set
`[storage, persistence]` when none are listed; a non-empty list replaces it, so a `unique`
property needs `persistence` or a vendor library there — nothing adds it for you, and any other list is
`DMLIB001` at define time (`polyc`, `poly_define`, `apply_dsl`). Temporal stays implicit; unknown or duplicate ids fail closed.
`uses <id>` loads the library's export package, never a sibling `.poly`. `sqlite` and `sqlserver` are the vendor libraries the catalog resolves; `uses mysql` is `DSL0002` with the reason, not an unknown id (`Poly.Packs.MySql` ships, but no host provider arm loads it). A package boundary
pins the surface with `uses billing 2024-11-20`; a `contract` block covers what no library contributes.

A package may declare the host targets it is for (target ids such as `posix`); the host passes
its target on the compile (`polyc --target`). `uses posix` names the package, never the OS, and
a package whose targets omit the host target (or a host with no target) is `DMCON004`.


<!-- poly-verify: valid -->
```poly
domain MyDomain
uses storage
```

## 2. Enum Types

Enum types are declared at the top level, before entities that reference them.
Members are bare identifiers separated by optional commas (trailing comma allowed).

<!-- poly-verify: fragment:top wrapper:top-default -->
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

Members may carry core term metadata (see §3, Ontology Term Metadata):
`Active iri("https://example.com/MemberStatus/Active") term("active")`.

A policy compares an enum property to a member name: `Outcome == Pending` and
`Outcome is Pending`. `Outcome == "Pending"` is the same comparison.

### Value types

Named records without identity or lifecycle. A value type is a **contract payload or
action parameter** — it has no storage representation and cannot be a stored entity
property. Analysis fails closed on a property typed as one. `public` is optional and
omitted by default (`public value Money { … }`). Generated C# honours it (see **Visibility in generated C#** under entities): an unmarked value type prints `internal`. A generated value type is a C# record whose properties are `init`-only (`{ get; init; }`): a caller builds one with an object initializer or `with`, and assigning a property after construction does not compile, so a setter can be removed later without breaking a caller. A value-type payload's constraints and a null payload are refused at the action boundary on both hosts.

<!-- poly-verify: invalid:DMSTR006 wrapper:top-default -->
```poly
Money: value {
  Amount: Number
  Currency: Text
}

Order: entity {
  Total: Money   // error: a value type has no storage representation
}
```

For structured state on an entity, declare a **nested entity** (an `owned`/`many`
relationship) instead. To move a value shape into behavior, declare the type on a
contract and take it as a bound **action parameter** (see Contracts below). Policies
and path-prefix expressions cannot read a value type's fields (`bill Amount`), and
the diagnostic names the value type rather than calling that name a relationship.

### Contracts (used sub-domains)

A contract is a **used sub-domain**: source + version + **value types** (the ACL you own)
+ endpoints. Bind attaches a parent action to an endpoint. No `import` keyword — an
OpenAPI pack will emit this same IR later. No generated client.

Value types declared **inside** the contract belong to that sub-domain. Action
parameters at the bind seam may use those types. **Stored entity properties may not.**

<!-- poly-verify: skip:drift:DMEFF001 owner:M-02 -->
```poly
Billing: contract internal billing v1 {
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

ChargeOrder: bind Billing Charge to Pay request
```

This is the surface-only **sibling** case: the bind compiles away and `Pay` still runs.
An external provider is a library: `uses stripe` contributes the surface and the
endpoint body. A hand-authored `external` contract has no body, so a bind to it fails
Lower rather than guessing an implementation.

`external` / `internal`, `inbound` / `outbound`, `operation` / `event`. Analysis fails
closed if the contract, endpoint, action, or parameter is missing, if the parameter
type the payload cannot accept, if a payload type is unknown, or if two contracts
(or the parent domain) share a value-type name. A bind may name the original action
instead of a suffixed exported endpoint; see §11.

A bind name is a global name: a second bind with the same name is `DMCON017` on the
second declaration, naming the first. Lower splices one endpoint body per bound
action, so an action takes one bind: a second bind to the same action is `DMCON018`
on the second declaration, naming both; the other body would never run. Both are
errors, and both fail closed.

The `InternalDomain` producer fills a `contract internal`'s value types and endpoints
from another loaded domain; it contributes no implementation body. A surface-only
in-solution bind therefore compiles away.

A `bind` attaches the parent action to the endpoint. A library loaded by `uses` may
contribute the endpoint's implementation AST; Lower **splices** it at the bind: the
body's leading statements run, its final `return <DomainResult>;` becomes the call
outcome, and `if (!adapterResult.IsSuccess)` returns the failure. The body reads its
payload as the `Parameter("request")` node — there is no `Variable("request")` alias —
so the bound action's parameter must be named `request`. No `{Contract}Adapters` type or
adapter invoke is emitted.

A **surface-only in-solution** contract (`InternalDomain`, no body) compiles the call
away: no adapter, no invoke, and the local action still runs. An **external** contract
(`ExternalProvider`) with no contributed body fails Lower — and MCP simulate — naming
the contract and endpoint; it is never erased.

## 3. Entities and Properties

```
["public"] "entity" entity-name annotation* [initial-clause] [key-clause] "{" property* stage* action* policy* "}"
entity-name ":" "entity" annotation* [initial-clause] [key-clause] "{" property* stage* action* policy* "}"
initial-clause = "initial" "(" stage-name ")"
key-clause = "key" "(" member ("," member)* ")"
```

**Initial stage.** `initial(Active)` designates which stage a new instance starts in. It is a header clause that sits **before** `key(...)` and the opening `{`, after any annotations: `Order: entity initial(Active) key(Code) {`. It is not a body member and adds nothing to the entity: the name must be one of the entity's own stages. With no designation the first declared stage is initial, which is the old rule. The designation is honoured on create by the simulator and by the generated C# `Create` and its constructor, so the designated stage's `entry` effects run on creation. The generated test suite treats that stage as the covered one: its create test asserts the designated stage, and it refuses actions that exist only on other stages. An empty clause, a non-identifier, a second `initial(...)` clause, or a clause placed after `key(...)` is a positioned parse error; a name that matches no stage (or an entity with no stages) is **`DMINI001`**, and the create path falls back to the first declared stage meanwhile. The printer writes the clause in the same position.

**Key (identity).** `key(a, b)` designates existing members as the entity's identity. It is a header clause that sits **immediately before the opening `{`**, after any annotations: `Order: entity table("ORDERS") key(Code, Region) {` or `public entity Order key(Code) {`. It is not a body member and adds nothing to the entity; each name is one of the entity's own properties, and names are scoped to the entity. Analysis rejects an unknown member, a blank one, an `optional` or action-assigned key property, or a navigation (`DMSTR007`: a key over a navigation is not supported yet, since its foreign-key shape is not decided), a repeated member (`DMSTR001`), and a key that cycles through navigations (`DMSTR002`). An empty `key()`, a doubled or trailing comma, an unclosed list, or a second `key(...)` clause is a positioned parse error. The printer writes the clause in the same position. Scoped **value** uniqueness (`unique_by` / `unique(scope, value)`) is **not implemented**, and nothing enforces a key at runtime yet.

`public` is an optional leading modifier before the kind keyword (`public entity Order { … }`). Omitting it is `Visibility.Internal` and keeps `Order: entity`. `public` is a keyword, so it is not a property name. The mark may appear anywhere among the header annotations; the printer writes it first.

**Reserved names.** A property name must be an identifier, and a lowercase DSL keyword is not one: `from`, `to`, `entity`, `stage`, `action`, `policy`, `when`, `one`, `many`, `owned`, `in`, `create`, `link`, `unlink`, `public`, and the other keywords lex as keywords. A keyword in member position is a positioned parse error that names the word and the rename — `'from' is a reserved word and cannot name a property; rename it (e.g. 'From' or a synonym)` — at the word, in an entity body, a value-type body or a `create` initializer. The capitalized spelling (`From`, `To`) is an ordinary identifier, as is `Src`/`Dst` or `Origin`/`Destination`; the keyword rule is case-sensitive. The primitive type keywords are the exception: `Text`, `Number` and `Boolean` are legal property names when the type is primitive (`Number: Text`).

An exported signature must be closed. A `public` action's parameter and result types, and a `public` value type's field types, must themselves be `public`; primitives and enum payloads are always available, and a used contract's value types are already on that contract's surface. Analysis reports **`DMCON002`** and names the internal type. The fix is to mark the type `public`, not to add an exception.

**Visibility in generated C#.** Only a `public` entity, value type or action prints `public`; every unmarked one prints `internal`, so a domain with no `public` mark has no public surface and a consumer in another assembly needs the mark. Enums carry no mark and stay public, and a stage enum follows its entity. A public entity's properties and policies stay public, and a member that would name an internal type is `internal`; so is its `Create` when the entity has a navigation to an internal entity (a later slice, L-22c, adds a public factory). An internal entity's `Create` is `internal` too. `Create{Nav}` is always `internal`, on a public entity as well: a caller in another assembly creates a child by calling the declaring action, never `Create{Nav}` directly; a to-many with no action that creates into it is populated from another assembly by passing already-built children to the static `Create`, which takes the collection. A stage-dispatched action is one method that is `public` if any stage variant is public; a public stage group is rejected (`DMCON002`, on the offending copy) when any of its copies returns an internal entity. The generated DbContext is `internal` when any entity is internal, and a generated DTO is `internal` when its entity or any domain type it names is. A public entity may navigate to an internal entity: the navigation prints `internal` (the public surface stays closed) and the pack maps it explicitly, so the entity's JSON omits it. A nested route reads it only when the target is an aggregate child (owned, or one with a required back-reference); otherwise the target is a root, listed at its own route. Such an entity is built by its host, EF and actions, not by a caller in another assembly. A two-way one-to-one (owned or not) to an internal entity builds and boots (#791, row P-18). A many-to-many navigation to an internal entity is mapped as an EF skip navigation over a join table (`HasMany(...).WithMany(...)`, #807, row L-22d), so it boots and round-trips both sides. **`DMCON019`** covers the one shape still open: a public entity with an entity-typed property of an internal entity (persistence ignores it).

<!-- poly-verify: valid -->
```poly
domain Shop

Line: entity {
  Qty: Number default(1)
}

public entity Order {
  lines: many Line   // legal: prints internal, mapped by the pack
}
```

| Constraint | Syntax | Example |
|-----------|--------|---------|
| Optional | `optional` | `Note: Text optional` |
| Unique | `unique` | `Email: Text unique` |
| Range | `range(min, max)` | `Age: Number range(0, 150)` |
| Length | `length(min, max)` | `Code: Text length(2, 10)` |
| Pattern | `pattern(regex)` | `Zip: Text pattern("^\\d{5}$")` |
| Equality | `equals(value)` | `Status: Text equals("Active")` |
| Default | `default(value)` | `Status: MemberStatus default(Active)` |

`Number` is a 64-bit integer (`long`). A fractional value such as `0.25` is rejected and is not stored as `0`. `1` and an integral JSON number such as `1.0` are accepted. `Decimal` is `decimal` and accepts a fraction. A fractional literal or bound on `Number` is an error that names `Decimal`. A `Decimal` *expression* cannot be stored in `Number` either (`assign N to N + D`): the fraction would be rounded away, so analysis refuses it — see the expression section below.

A number literal is ASCII digits `0-9` with at most one `.` between digits: no sign, exponent or digit separator, and a non-ASCII digit such as `٣` is a positioned error. It is held exactly or it is a positioned error that states the limit: at most 28 digits after the point and no larger than `79228162514264337593543950335` (less with digits after the point). Nothing is rounded. Leading zeros and trailing fraction zeros are free (`007`, `1.0`). `length(...)` bounds and duration amounts are whole numbers written without a decimal point (`length(1.0)` and `1.0 Days` are errors), from 0 to `2147483647` and from 0 to `9223372036854775807`.

`range` bounds are exact at parse and in the assign guard: a `Number` bound is a `long`, a `Decimal` or any other non-Number-family bound is a `decimal`, and no bound is a `double`. A `Number` bound outside the `long` range (for example `9223372036854775808`) is a positioned parse error, not a silent widening, and the exported DSL and the generated validation messages keep the declared bound text. The generated data-annotation attribute keeps the bound exact too: an integral bound within `int` uses `[Range(min, max)]`, and `Decimal` or a `long` bound beyond `int` uses `[Range(typeof(decimal), "min", "max")]` or `[Range(typeof(long), "min", "max")]`. Analysis-time bound arithmetic is still `double` and moves to an exact representation in a later slice.

Properties can reference enum types by name as their type, and use `default(MemberName)`
to set a default value:

<!-- poly-verify: fragment:top wrapper:enum-defaults -->
```poly
Color: enum { Red, Green, Blue }

Item: entity {
  Name: Text
  Color: Color default(Red)     // typed by enum, default by member name
  Status: MemberStatus default(Active)
}
```

### What makes a property optional at create

Properties are **required at create by default**: every property without `optional`
must be supplied by a `create` / `create in` / `create_instance` (and, for a root
entity, is a member of the create DTO). Required means non-null for every type: an
omitted required value fails `Required` on every host, a Number is never filled with
`0`, and an empty string is a value (forbidding it is the job of `length(1, …)`, which
fails `Length`). Every parameter of the generated `Create` is `T? x = default`, in
member declaration order; the lowered body owns the `Required` failure and the defaults,
so a host passes `null` for a value it does not have. An explicit `null` at the create
door is the same as omitting the value. Over HTTP a `POST` that omits a required member,
or sends it as JSON `null`, answers `400` with a `Required` ProblemDetails (`elementPath`
`Entity.Member`), exactly what the simulator answers; the DTO's members are nullable and
an empty string for a required Text is a value, so the create succeeds (`201` for an entity
with a unique key, otherwise `200`) unless `length` or `pattern` refuses it.

`optional` is the explicit opt-out: the property prints as `T?` and stores `null` when
omitted. `default(...)` supplies a value when the property is omitted (or passed `null`);
an `optional default(x)` property therefore cannot be created unset.

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
Token: entity {
  Lexeme: Text                 // required at create
  Note: Text optional          // may be omitted; omitted = null
  Kind: Text default("Word")   // may be omitted; the default supplies "Word"
}
```

**`null` and optional values.** `null` is written only into an `optional` property: `assign Note to null`, a
`create` initializer, `default(null)`, or a `match` arm (`assign Q to match { Big -> null, else -> 7 }` stores
absent or 7). Into a required property, an action or function parameter, or a function's return it is refused at
the `null` (`DMSEM003`; `DMFUN003` for a call argument, `DMFUN006` for a return). An `optional` property's constraints
(an enum's members, `equals`, `range`, `length`, `pattern`) apply to a present value only. `==` and `!=` (`is`, `is not`) compare `null` with an optional property, a related
read (`part Qty`, `line Qty`), arithmetic over an optional property, a `match` with a `null` arm, or a Text, Uuid,
entity or Binary value; a required Number, Boolean, Date, Time, Duration or enum property is never null, so
`Qty == null` is `DMSEM003`, and an ordering against `null` (`Q < null`) is always `DMSEM003`. An optional Boolean
in a condition (`if`, an `and`/`or`/`not` operand, a `for … where`, a `match` arm condition, an
`any`/`all`/`none`/`count … where` body, a policy) means `X is true`: absent is false. So `not Flag` and
`Flag != true` are true when Flag is absent, and `Flag is false` is false when it is absent.

A property without `optional` rejects a missing (null) value when it is written, for every
type. An empty string is a value: refusing it is the job of `length(1, …)` (a `Length` failure).
An unset optional Text reads as `null`, never as `""`: `is null` is true and `exists` is false
on every host, and a copy of it into another optional Text stays absent.
The word `required` is not a constraint. Writing it is a positioned parse error:
a property is required unless it is marked `optional`.

`optional` applies to entity properties only. Value-type (`value { ... }`) and contract
payload members are always required fields; `optional` there is a positioned parse error.

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

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
Item: entity table("INVENTORY") {
  Code: Text unique column("CODE", "VARCHAR2(20)")
  Name: Text column("NAME")
  Qty: Number range(0, ) column("QUANTITY")
}
```

Without the annotation library enabled, `column(...)` and `table(...)` produce a parse error.
This is deliberate — portable domain text stays DBMS-agnostic; storage hints are
an opt-in projection layer.

### Ontology Term Metadata (Core)

Term annotations link a model to the ontology it describes. They are **core** vocabulary —
no `uses` is needed — and have no runtime semantics.

| Annotation | Scope | Meaning |
|-----------|-------|---------|
| `iri("…")` | entity/enum header, property tail, enum member | the absolute `http(s)` or `urn` IRI this element denotes |
| `label("…")` | entity/enum header, property tail, enum member | the human label |
| `description("…")` | entity/enum header, property tail, enum member | the human description |
| `term("…")` | enum member | the term name inside the vocabulary |

Each takes **exactly one non-empty string**. `iri` must be absolute (`http`, `https`, or
`urn`); a CURIE such as `schema:Person` has no prefix declaration here and is rejected.
A number, a boolean, an empty list, an empty string, or a duplicate key is a positioned
parse error; a typo never becomes a silent term.

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
Color: enum iri("https://example.com/Color") {
  Red iri("https://example.com/Color/Red") term("red"),
  Green,
}

Product: entity iri("https://example.com/Product") label("Product") description("A sellable item") {
  Sku: Text iri("https://example.com/Product/sku") label("SKU") length(1, 20)
}
```

Term annotations interleave with constraints and storage annotations in any order.
`export_dsl` round-trips them. `export_domain_to_json_schema` projects an entity's terms to
`title` / `description` / `x-poly-iri`, property terms to `title` / `description` /
`x-poly-iri`, and enum-member terms to `x-poly-enum`. `export_domain_to_json_ld` uses the
same IRIs in an `@context` plus an RDFS `@graph`; `export_domain_to_shacl` publishes the
same classes and paths as SHACL node/property shapes; `export_domain_to_html` renders the
same facts as a self-contained page for a person; an untagged element gets a stable
`urn:poly:` IRI. The IRI is `x-poly-iri`, not a subschema `$id`: in JSON Schema 2020-12 a
`$defs` `$id` starts an embedded resource and would rebase every local `#/$defs/…`
reference onto the element IRI.

### Export order and `uses`

`export_dsl` prints declarations in the order they were authored. Top-level declarations are grouped by kind (enums, value types, entities, functions, contracts, bindings), each kind in model order. Entity members (properties, navigations, stages, actions, policies) print in authored order, with subscriptions last. Members added through incremental MCP tools print after the authored ones. A `uses` line is printed only when the domain's extension list differs from the implicit set (`storage`, `persistence`, in that order) or a `uses <id> <version>` pin is present, so a document that listed no `uses` exports without one. Export drops `uses temporal`; reading re-supplies it.

Two consequences of the `uses` rule. First, a document that spells exactly the implicit set (`uses storage` then `uses persistence`) exports without those lines: the model is equal, the text is not. Second, an export without `uses` is not self-describing: applying it into a session that already holds other libraries keeps them (for example `uses http`), and `polyc --seed language` loads no libraries (`--seed authoring` is an alias for the implicit set). Write the `uses` lines you need into the document. This is an interim rule until the implicit set stops being recorded in the extension list.

## 4. Navigation Properties (N1 Relationships Only)

Relationships are declared as **inline navigation properties** on the source entity.
The legacy `relationship { }` top-level form is **not supported**.

<!-- poly-verify: skip:drift:DSL0001 owner:M-02 -->
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
  passport: owned Passport optional //source owns target
  lineItems: many owned LineItem // many + owned
}
```

Cardinality: `one` (default, can be omitted), `many`.

**A to-one navigation is required** unless it is declared `optional`, as a property is:
`parent: Account optional`. `optional` closes the line, after any `via`
(`holder: Holder via tickets optional`). A required to-one must be bound when the entity is
created (DMEFF011 above), and creating it with a null navigation fails with a Required
failure on every host. A collection is never required, so `optional` on `many` is a
positioned parse error; its *elements* are still checked, and `Create` refuses a null element in
a `many` argument with a Required failure (`'legs' contains a null element.`, `elementPath`
`Shipment.legs`), so a policy that counts the collection (`count legs > 0`) is not satisfied by a
null and no null reaches the stored list. An **owned single part must be written `optional`**
(`hq: owned Address optional`): it is filled by `create in` after its owner exists, so it can
never be required at create; a bare `hq: owned Address` is a positioned parse error. Use a
policy (`policy { hq exists }`) to require it at a later stage. A bare navigation to the
entity itself, or a cycle of required to-ones across entities (`a: B` with `b: A`), is
**DMREL006**: no instance could ever be created first. Mark one side `optional`; an `owned`
navigation breaks a cycle. An entity whose to-one navigations are all `optional` is a root
structurally; storage and HTTP take root-ness from the aggregate, where an entity nothing
owns is a root too. **Over HTTP, the create body carries each required link as the linked
entity's route key** (`"CourseId": "CS101"` for `course: Course`, the target's `unique`
property): the host looks the target up as an action's entity argument is looked up (404 when
no such row), loads what the create reads on it, and passes it to `Create`. A link left out
or sent as `null` answers 400 `Required` at `Section.course`, the simulator's answer. An
**optional** link is not settable in a create body yet: use an MCP create, which takes it,
or, where the document has an action that fills it, create then call that action. An entity whose required link's target has no natural
key (a shadow `id`) has no `POST` route, since no value a client could send names the
target; it is created by a parent action's `create in`, which exists only when the parent has
a collection of it (`CheckOut: action { create in loans { book: book } }` serves `POST
/api/patrons/{email}/checkout`). An entity with a required link is not seeded. The member is named `<Link>Id`, like an
action's entity-reference member; when a required scalar of the entity already has that name
(`CourseId` next to `course: Course`) the entity gets no `POST` route, DTO or sample, with no
diagnostic, so rename one of them. A `create in` into a required
to-one of the source is **DMEFF001**: the link is already set when the action runs; declare
it `optional` to fill it there.

**A required link is permanent in this release.** The host refuses to unlink a required
to-one ("Cannot unlink '<rel>': the navigation is required; delete the instance and create a
new one to move it."), and a set to-one cannot be re-pointed, so move it by deleting and
creating. The generated C# has no unlink operation. An MCP create (`poly_simulate`'s
`create` step) takes a singular navigation as well as properties: its value is the id of an
existing instance of the target, or an earlier step's alias
(`{"Name":"Grace","account":"acme"}`). A required to-one must be given there; an optional one
may be. An unknown id, an instance of another entity, a collection navigation or a value that
is not an id fails on the navigation (`Lead.account`). JSON Schema, SHACL and MCP describe do
not show requiredness of a navigation yet.

**A create wires both sides of a pair.** Every navigation argument of a generated
`Create` is paired with its inverse: a **singular** argument attaches the new instance to
the peer's paired navigation (`Order.Create(customer: ann)` adds the order to `ann.Orders`;
`Profile.Create(owner: ann)` sets `ann.Profile`, not only the collection case), and a
**collection** argument sets each child's paired to-one (`Patron.Create(loans: kids)` sets
`kid.Borrower`) — see §0.3. A collection argument holding a child whose paired to-one
already names another owner fails the create with a Link failure rather than re-pointing
the child. Pairing is ambiguous when two navigations back to the source qualify: then
neither side is wired, the same answer the create-in fill gives.
Ownership: `owned` marks `SourceOwnsTarget = true`. Export writes `one ` explicitly for a one-to-one navigation (`invoice: Invoice` prints as `invoice: one Invoice`), because a bare `x: T` also reads back as one-to-one but fails closed when `T` is a Phase 1a keyword (`actor`, `schedule`, `parallel`, `for`, `function`; matched case-insensitively); `one T` is unambiguous.

**Which entity is the root.** A collection nav (`children: many Child`) means the
declaring entity owns the target. `owned` marks composition explicitly. A plain singular
nav is a *reference*: `Truck.warehouse: Warehouse` is a truck that names its warehouse, not
a warehouse that names one truck. Where two entities reference each other (the classic 1:1
pair, `Doctor.schedule: one Schedule` with `Schedule.doctor: Doctor`) one side resolves as
the owner and the other becomes its child — which side is a resolution detail, not something
the DSL distinguishes.

Ownership never closes a loop, and an entity with no owner is a **root**: it gets its own
key and its own HTTP endpoints. So a domain cannot express "nothing owns me, but I am not a
root" — that state used to be reachable and silently produced an entity with no key and no
endpoints.

**Owned targets keep their owner.** `unlink_instances` refuses to drop the last
inbound link of an `owned` target (minimum one owner): "Cannot unlink '<rel>': the
owned target must keep its owner." Non-owned links stay minimum zero. No shipped
surface removes an instance: the effects are `transition`, `assign`, `create`,
`create in`, `invoke`, `for` and `if`, and the generated HTTP host maps only GET
and POST. The minimum is therefore checked when unlinking, and an owned target
cannot be released from its owner.
An instance cannot own itself: linking an instance to itself over an `owned` navigation is
refused ("Cannot link '<rel>': an instance cannot own itself."). A cycle through other
instances (`a` owns `b`, `b` owns `a`) is not refused, and a non-owning self-link is legal.

**An owned entity has exactly one owner navigation** (`DMSTR010`). Two `owned`
navigations to one entity type are an analysis error, whether they sit on different
entities or on one, and the message names every owner:

<!-- poly-verify: invalid:DMSTR010 wrapper:top-default -->
```poly
Customer: entity {
  Name: Text
  profile: owned Profile optional
}

Supplier: entity {
  Name: Text
  profile: owned Profile optional   // error: Profile already has an owner, Customer.profile
}

Profile: entity {
  Bio: Text
}
```

Keep one navigation `owned` and make the other a plain reference. The rule is by
navigation, so `Order.billing: owned Address` plus `Order.shipping: owned Address`
is rejected too: make one a plain reference, or model the address as two entity types.
A self-owning navigation (`Node.children: many owned Node`) is one owner edge and is
legal, but a container that owns a recursive type (`Folder.items: many owned Item` plus
`Item.children: many owned Item`) gives `Item` two owner navigations and is rejected;
make `Item.children` a plain reference, or declare the tree node as its own type.
This is a type-level check only. A second owner *instance* is refused when the part names
its owner — the part's own navigation back (`Order.customer`) is what the generated C# and
the simulator both read, so putting a part that already names another owner into an owner's
collection answers a `Link` failure: "Cannot add a link on 'orders': the owned 'Order'
already has an owner ('Customer'), so another 'Customer' cannot own it." A part whose type
declares no navigation back records no owner, so nothing refuses a second one yet (see
*Known wrong*).

**Ownership never closes a loop between different entities** (`DMDEP002`). Two or
more different entity types that own each other through `owned` navigations are an
analysis error: one per cycle the analysis walk closes, reported on the owned
navigation that returns to the cycle's earliest-declared entity and naming the whole
path (`Cabin -> Deck -> Cabin`). Fixing the named navigation may reveal another cycle
that the walk reached only through an entity it had already finished.
An entity owning only itself is a tree and stays legal; a plain reference back to the
owner is not ownership:

<!-- poly-verify: invalid:DMDEP002 wrapper:top-default -->
```poly
Cabin: entity {
  Name: Text
  deck: owned Deck optional
}

Deck: entity {
  Name: Text
  cabin: owned Cabin optional   // error: Cabin -> Deck -> Cabin, nothing is the root
}
```

Make the back navigation a plain reference (`cabin: Cabin`), or drop it.

**Relationship names are scoped to their source entity.** The same navigation name may
be declared on different source entities — e.g. two children each back-reference their
parent with a nav named `order` — but never twice on the same entity, and never
colliding with a property of the same name on that entity. Relationship references in
policies, subscriptions, `create in`, and `invoke` always resolve relative to the
entity they are authored on.

<!-- poly-verify: fragment:top wrapper:order-minimal -->
```poly
OrderLine: entity {
  order: Order
}

Note: entity {
  order: Order        // allowed — different source entity
}
```

**Inverse navigation.** `via <name>` names the navigation property on the target entity
that points back, so a pair can name both directions explicitly:

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
Department: entity {
  reports: many Employee via manager
}

Employee: entity {
  manager: Department via reports
}
```

`via` is recorded on the relationship as `Inverse` and is validated by analysis:
`DMREL003` when the name is not a navigation on the target entity, `DMREL004` when the
two sides contradict (the named navigation points elsewhere, declares a different
inverse, is the navigation itself, both ends are owned, an owned navigation is paired with a
collection inverse, or two navigations on one entity claim the same undeclared inverse), and `DMREL005` when two or more navigations of one
cardinality class pair with a side that declares none. `create in` reads the declared
inverse to pick the back-reference it fills, and a create argument reads it to pick the peer
navigation it wires: the one `via` names, or the one that declares `via` back. With neither
declaration the pairing is the only navigation of the target's type — and on a
self-relationship, where every navigation targets that type, only when nothing else points
back: `referrer: Person` beside `friends: many Person` leaves `referrer` paired with nothing
rather than guessed into `friends` (#584). Nothing else dispatches from `via` yet. `via <name>` is legal on an owned
navigation too — that is how the back-reference names its owner, and only one side of a
pair may own it. A member may still be named `via` (`via: Text`, `via(x: Text): action { }`): the
clause starts whenever `via` is followed by anything other than `:` or `(`, so `via 123`
enters the clause and then fails as a missing name. A `via` with no name, or a second
`via` on the same navigation, is a positioned parse error.

## 5. Lifecycle Stages

```
stage-name ":" "stage" "{" stage-member-list "}"
```

Stages contain actions and subscriptions. Stages are **flat** — no parent/child hierarchy.

<!-- poly-verify: fragment:entity wrapper:entity-stage-list -->
```poly
Draft: stage {
  Submit: action { transition to Active }
}
Active: stage { }
Done: stage { }
```

### Stage require

A stage may `require` an entity policy (identifier list; `require not` negates). The clause goes in the stage header, before the opening `{`, exactly where an action's `require` goes (`Name: stage require A, B {`; repeated `require` clauses are allowed). A `require` inside the stage body is a parse error. It is an invariant:
every action that can run in the stage must keep it true. Policies are defined on the entity, never
inside the stage. A `require` that proves a property present (`Note is not null`) also clears
copies of that optional property into a required one (DMEFF014) in the stage's actions; an action
that can assign null to it is DMEFF015.

The invariant is enforced on the state after an action as well as before it: the lowered action
re-checks its stage's `require` before returning success, so an action that would falsify the
requirement fails with a `Policy` failure and its writes roll back. A write inside its
declared range is invisible to Analyze, so this check — not a diagnostic — is what catches it. An
action that transitions away is judged by the stage it lands in.
The requirement holds at creation too: the factory sets the initial values, runs the initial stage's
`entry`, then checks that stage's `require` policies. Values that falsify one are refused with a
`Policy` failure — `'Create' blocked by policy 'NotCleared'. It would leave stage 'Open' invalid.`,
on the element path `Entity.Create`, guarded by the policy — and nothing is created or registered.
So an instance never starts in a stage its own values falsify (#576); a requirement the `entry`
establishes is satisfied, and a stage the instance is not in is not the one `Create` is judged by.

<!-- poly-verify: fragment:entity wrapper:entity-stage-require -->
```poly
HasNote: policy { Note is not null }
Open: stage require HasNote {
  Copy: action { assign Memo to Note }
}
```

### Entry/Exit Effects

<!-- poly-verify: fragment:entity wrapper:entity-stage-effects -->
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

A transition inside an exit block is legal only when it targets the stage that every hop leaving
that stage targets. A hop assigns the new stage after the inlined exit block, so a differing
exit-block target is overridden: the instance lands on the hop's target after the exit-block
target's entry effects ran. Analyze rejects this with `DMEFF017`, positioned on the exit-block
transition. Entity-level actions, entity-level `when` handlers and a `for` body have no static
source stage, so a transition there must match every exit-block target. A transition that follows
another transition, an `invoke`, a `create`, a `create in`, a `for … invoke` or a branch holding a
transition counts as leaving whatever stage the instance may then be in, so it must match every
exit-block target too. Guard analysis is one provable exclusion: when an entity-level action
requires a policy whose negation the stage requires (`Go: action require InB` beside
`A: stage require not InB`), the lowered body refuses the action before any effect runs, so it
cannot leave that stage and does not count against the stage's exit-block target. Every other
transition counts, including one under an `if` the analysis cannot decide, and two policies that
contradict each other without naming one another (`Mode is "b"` against `Mode is "c"`) are not
analysed either, so that document is still refused: the rule stays conservative wherever the
guard is not a policy and its own negation. An `invoke` before a hop makes it dynamic even when
the callee is pure. Making the exit-block transition target the same stage as the hop passes the
rule, and that transition is redundant: the hop owns the landing, so the target's entry effects and
subscriber notify run once, and the effects after the transition in the exit block still run before
the landing.

An entry block admits `invoke`, `create`, `create in` and `for … invoke` (§9): the entry effects run
where the instance is created or lands, and both hosts execute them. An exit block rejects the same
effects with `DMEFF018`, positioned on the effect: the exit block inlines into every hop that leaves
the stage, so a callee or a create could transition the instance behind the exit-block target check.
Move exit-block work into an action.

<!-- poly-verify: invalid:DMEFF017 -->
```poly
domain Shipping

Parcel: entity {
  Packed: stage {
    exit { transition to Returned }   // error: Ship leaves Packed for Shipped
    Ship: action { transition to Shipped }
  }
  Shipped: stage { }
  Returned: stage { }
}
```

**Supplied entry-assigned property (DMEFF019):** a `create` / `create in` initializer may not
supply a property the new instance's entry chain may assign — directly in the initial stage's
`entry`, under an `if` or `for`, through a `transition`, or through an `invoke`d action. The entry
chain overwrites the supplied value, so it could never reach the object. Omit the initializer. A
property assigned only inside an entry `if` is caught between the two rules: required, it is
`DMEFF011` when omitted and `DMEFF019` when supplied, so give it `default(...)` or mark it
`optional` to make it omittable.

<!-- poly-verify: invalid:DMEFF019 -->
```poly
domain Tokens

Token: entity {
  Kind: Text optional
  Lexed: stage { entry { assign Kind to "Keyword" } }
}
Parser: entity {
  Lex: action {
    create Token { Kind: "Keyword" }   // error: Lexed's entry assigns Kind
  }
}
```

### Explicit Initial Stage

`initial(Stage)` belongs to the entity header, not to a stage: it names the stage a new instance
starts in. With no clause the first declared stage is initial. A stage named `initial` is an
ordinary stage — the word is contextual only in header position, before `key(...)`.

<!-- poly-verify: valid -->
```poly
domain Orders

Order: entity initial(Active) {
  Number: Text
  Status: Text optional
  Draft: stage {
    Activate: action {
      transition to Active
    }
  }
  Active: stage {
    entry {
      assign Status to "entered_active"
    }
  }
}
```

`Create` starts the instance in `Active` and runs `Active`'s `entry` effects even though `Draft` is
declared first; the generated test suite asserts `Active` as the create stage and refuses `Draft`'s actions. A
name that matches no stage is `DMINI001`.

## 6. Actions

```
["public"] "action" action-name ["(" param-name ":" Type ("," ...)? ")"] ["->" ret-type] ["require" ...] "{" effect* "}"
action-name ":" "action" ["(" param-name ":" Type ("," ...)? ")"] ["->" ret-type] ["require" ...] "{" effect* "}"
```

`public` is an optional leading modifier (`public action Pay (bill: Money) { … }`). Omitting it is internal, and the generated method prints `internal` to match. `public` is a keyword, so it is not a parameter name.

Parameters (optional) appear **after** `: action`, keeping the uniform `Name: kind` member form (matches `export_dsl`). Parameter names cannot be DSL keywords (`when`, `require`, `action`, `stage`, `policy`, `entity`, `public`).



Same action name may appear on multiple stages; each stage's body and `require` gates are independent (effects are not copied onto earlier stages).

**An action is all or nothing, on both hosts.** When an action fails (a returned failure or a thrown one), every entity
it changed is restored: its own assigns, stage and the children it added to its collections; what an `invoke`d action
changed on another entity; what a `when` handler it fired changed on the subscriber; and a child it left in an existing
entity's collection or subscriber registry. The simulator and the generated C# restore the same state. Two things are
not yet covered. The storage port of the generated C# is not part of the rollback yet: a `unique` key the failed action
assigned stays reserved (a retry with the same key reports it taken), and the key it replaced stays released although
the entity holds it again. And a generated
`Create` or `Attach{Nav}` called directly by host code, with no action running, is not rolled back as a
whole. (`Create{Nav}` is `internal`, so host code in another assembly cannot call it; it creates a child through the
action that declares the `create in`, or, for a to-many with no such action, by passing already-built children to the static `Create`, which takes the collection. `internal` does not guard: a caller in the same assembly can still skip an action's
guards, until L-17.)

<!-- poly-verify: fragment:entity wrapper:entity-bodies -->
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

<!-- poly-verify: fragment:entity wrapper:entity-bodies -->
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

<!-- poly-verify: fragment:entity wrapper:entity-bodies -->
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

<!-- poly-verify: skip:illustration owner:M-02 -->
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
| General for | `for name in Rel [where expr] { effects }` — executable; fail-fast, zero records is a no-op (see "General `for`" below) |
| Conditional | `if (expr) { effects } [else if (expr) { effects }]* [else { effects }]` |

<!-- poly-verify: fragment:entity wrapper:entity-bodies -->
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

Statements may nest at most 64 levels: the level of an `if` or `for` is the number of enclosing
`if` and `for` statements, itself included, so a top-level `if` is level 1, and each `else if` rung
is one level below the rung before it (it is a nested `if`). A statement at level 65 is a positioned
parse error (`DSL0001`, "statement nesting exceeds the limit of 64") on its `if` or `for` keyword,
not a crash, however deep the input goes. Like the expression limit, it is a parse limit, not a
stack guarantee for the steps after the parse (see *Expressions*, where the compiler's own stack
and the typed failure for one that is still too small are described).

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
  returns `Failure`) — no silent swallow. Records already invoked stay mutated, and their
  subscriptions have already fired. There is no transaction around the fan-out.
- **Zero records is a no-op**, not a failure: an empty collection, or a predicate that
  matched none, means the loop body never runs and the action continues with the effect
  after the `for`. The DMREL002 analysis warning below still reports a collection no code
  writes. (Why the rule changed: `docs/decisions/2026-10-17-for-zero-records-is-a-no-op.md`.)
- **Some predicates are rejected at analysis:** a `for` predicate policy that reads a
  path-prefix hop is rejected ("reads a path-prefix hop"). A hop read answers absent since
  #936, so the refusal is now conservative rather than a host difference; the `for` form has
  no cross-host pin of its own yet, so it stays refused. One that tests `exists` on a property (not a
  relationship) is rejected ("tests 'exists' on a property") wherever every quantifier
  on the way to it resolves: on the record itself, or on a related record inside an
  `any`/`all`/`none`/`count` body. Simulate and export disagree on whether an unset
  property exists. Below a nested quantifier whose relationship does not resolve, the
  check does not run (analysis does not validate nested quantifiers yet; simulate and
  export both fail on such a policy). Relationship `exists` and collection-quantifier
  predicates are allowed and give the same answer in simulate and export. Use a **local policy over the record's own properties** instead of a
  path-prefix hop, and compare a property to a value instead of testing `exists`.

### General `for` (#409)

`for name in Rel [where expr] { effects }` parses, prints back identically
(`Write(Read(x))` is idempotent), analyzes, lowers and **executes** in simulation and in
generated C#: a loop over the relationship's records in storage order that runs the body
once per record whose `where` holds.

- **Zero records is a no-op:** an empty relationship, or a `where` that matches none,
  runs nothing and the action still succeeds.
- **Fail-fast:** the first failing body effect (for example an `invoke name.Action` that
  fails) fails the whole `for` and no later record is visited. Both hosts roll the failed
  action back, so nothing earlier records did survives it.
- **Legal body effects:** an `assign` to the owning entity's own state, `invoke name.Action`
  on the loop record. An `assign` through the binder or a relationship is another
  entity's state and is rejected with `DMEFF012`; mutate that entity with `invoke name.Action`.

- `name` binds each record; `Rel` is a relationship name (a `many` relationship once
  analysis ships); `where expr` is an **inline** expression (e.g. `where line Qty > 1`) —
  unlike the fan-out form, not a named policy. The body is any effects except another `for` (a nested `for` is a parse error).
- Distinct from fan-out: `for name in Rel …` (general) versus `for Rel as name …` (fan-out).
- **Parse errors (positioned, with line and column):** missing `in` or `as` after the name;
  an empty collection name (`for x in where …`, `for x in {`); missing `{` after
  the collection or condition; a malformed `where` expression; a `for` nested inside a
  `for` body; and a missing `}`, which is reported at the end of the input rather than
  at the `for`.
- Still missing: `first` / `filter`; `create in` inside the body is not exercised yet.

<!-- poly-verify: skip:drift:DSL0001 owner:M-02 -->
```poly
invoke Validate                              # self-only
invoke Validate(status: "ready")             # self-only with args (all params required)
invoke service.Process                       # OneToOne source → target
for items as item where item IsEligible
    invoke item.Mark(amount: item Qty)       # fan-out, policy-filtered
for items as item where item in Active
    invoke item.Mark()                       # fan-out, stage-filtered
```

A recursive invoke is not supported: any invoke cycle is a `DMEFF021` analysis error, guarded or
not (#1101). That covers a self-invoke, `Ping: invoke Pong` + `Pong: invoke Ping`, a cycle across a
relationship (`invoke order.Go` back from a line), the `for … invoke` fan-out, and a stage `entry`
that invokes the stage's own action which transitions back into that stage. Each copy of an action
— the entity-level copy and each stage's copy — is its own node, so a same-named copy that never
re-enters its own stage path is not a cycle. The reason is that the printed C# has no invoke depth
guard, so a recursion that runs deep overflows the process while the simulator stops it cleanly. A
cycle of transitions alone is `DMEFF013`'s rule, and a `when` subscription is not an edge (the
notify dispatch has its own depth guard). A handler still counts after a transition into a stage
it watches: lowering runs those handlers before the body's next effect, so one that can move the
publisher's instance widens the stage set, and the `invoke` after the transition may re-enter.

### Require Gates

Require gates reference **named policies** defined on the entity.
Named policies are predicates — they do **not** automatically gate every action.
An action is blocked by a policy only when it lists `require PolicyName`.
`require not PolicyName` negates the policy.

<!-- poly-verify: fragment:entity wrapper:policy-gate -->
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
| `any` | Fires on **each** matching transition while **at least one** linked related entity is in a matching stage |
| `all` | Fires **once** when **every** linked related entity is in a matching stage |

- `any`/`all` (and `Each`) all evaluate the **set state after the transition** —
  they do not fire "every peer bag at once". `all` only fires once the whole linked
  set is in a matching stage; until then transitions into the stage are ignored.
- The triggering transition is itself in a watched stage, so `any` over one stage fires
  on every transition into it, like `Each`; over several stages it fires while any linked
  entity is in any of them. `all` is the only quantifier that waits for the whole set.
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

<!-- poly-verify: fragment:entity wrapper:stage-subscriptions -->
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

**A subscription fires only for edges that exist.** The relationship it reads is populated
by the source entity's `create in Rel`, by the aggregate parent relationship (the generated
child-create endpoint and the printed `Attach` call), or by an explicit link
(`link_instances` or a host). A peer navigation bound as a *value* — `create in sessions
{ member: who }` — does **not** give `Member.bookings` an edge, so a subscription (or a
`for` loop) reading `Member.bookings` can never fire. Analysis reports **DMREL002** when a
collection relationship is read by a subscription or a `for` loop while nothing in the
domain writes it. The loop itself is then a no-op (zero records); the warning, not a
runtime failure, is the signal.

**A create-time entry reports every stage it entered.** The child is related to nobody
while its initial stage's `entry` runs, so nothing fires then; `create in` links the child
first and only then reports, in entry order, the initial stage and each stage that entry
`transition`ed into. A parent watching `items Ready` therefore fires when the child reaches
`Ready` inside its own entry, and a parent watching both `items Start` and `items Ready`
fires `Start` then `Ready`. A failing handler there fails the `create in` and rolls it
back like any other handler failure.

| Placement | Active when | Optional `as name` |
|-----------|-------------|--------------------|
| **Stage-scoped** (`when` inside a stage body) | Only while the subscriber is **in that stage** | Yes — same peer rules |
| **Entity-level** (`when` on the entity, outside stages) | **Always-active** (any subscriber stage) | Yes — same peer rules |

Store notify runs entity-level handlers first, then stage-scoped ones. A stage-scoped handler
checks the subscriber's stage when it runs, so an entity-level handler that moves the subscriber
out of that stage suppresses it. Both placements use the same effect-binding fail-closed checks.

Two `when`s in the same scope (entity level, or one stage) with the same relationship,
quantifier, peer binder and stage set warn **`DMSS006`**. Nothing is removed: both run, in
declaration order. A `when` that names a stage nothing transitions into, and that is not the
target's initial stage, warns **`DMSS007`**: a stage it names can never trigger it. The check is syntactic (no
transition into the stage at all), not reachability.

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
- **Reserved binder names:** the binder becomes the generated handler's parameter, and the
  handler body calls `Poly.Runtime…`, `System.…` and `DomainResult.Success()`, so a binder
  named `Poly`, `System` or `DomainResult`, or one starting with `__`, is rejected with
  `DMSTR009`, the rule action parameters follow. A binder — and equally an entity member —
  named exactly like any **generated stage enum** of the domain (`OrderStage`, including
  another entity's) is rejected with the same code: the handler and the constructor read
  `<Entity>Stage.<Stage>` from expression position, where such a name binds the parameter or
  member instead of the type. `for Rel as name` and `for name in Rel` binders are not
  affected: they lower to a generated local.
- **C# export:** the export consumes the analysis-published subscription dispatch plan
  (the same metadata the runtime uses) and emits a handler per subscription, named by
  quantifier: `WhenAny`/`WhenAll`/`WhenEach{Target}{Stage}`. Peer-dependent
  `when … as name` adds a parameter typed as the target entity and named as the binder;
  notify calls `handler(this)`. Binder path-prefix (e.g. `order Code`) lowers to that
  parameter (`order.Code`). Notification-only `when` stays zero-arg. Nested path-prefix
  under the binder is rejected (analysis + export).

<!-- poly-verify: skip:illustration owner:M-02 -->
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

### Failure

A `when` handler returns a result. A failed `invoke` (or `for ... invoke`) in it
stops the handler and returns that failure: its kind, message and guard are
unchanged, and its path is the call chain (the handler adds no segment of its
own). The notification stops at the first failing handler, in registration
order, so no later handler runs. The action that caused the transition, or the
`create in` that landed the child in a watched stage, returns that failure to
its caller; it never reports success and never throws. The failed action is
rolled back on both hosts, so the stage change that fired the subscription, and
anything an earlier handler wrote, is undone.
For a failed `invoke` in the handler of `Run` that finishes an `Item`, the path
reads `Owner.Run.Item.Finish.Owner.Y`.

A failed `create`, `create in` (including a `create in` inside a handler whose
new child's own handler fails) or constraint-violating `assign` inside a handler
is returned the same way: the handler stops, adds no path segment, and the action
that caused the transition returns the failure and is rolled back. Nothing in
lowered code throws a domain failure. Interpreter and printed C# agree.

A failing `invoke`, `create`, `create in`, constraint-violating `assign` or `for ... invoke`
in the initial stage's `entry` block fails the `Create` that ran it: `Create` returns
that failure (kind, message and guard unchanged, no segment added by the entry) and
the new instance is in no collection and no subscription registry. The path is the
callee's own (`Kid.Fails` from a bare `Create`, `Holder.Make.Kid.Fails` from the
holder's `create in`), and an action that ran the `create in` is rolled back.
A bare generated `Create` (called with no action running) whose entry fails after changing another
entity (a succeeded `invoke` of an action on a linked entity, a `create` attached to an existing
peer) rolls that change back too, as the interpreter does.

## 8. Policies

Policies are named boolean guard expressions attached to an entity. The body is always inline: `P: policy { <expression> }`. `P: policy external` is not a form — a policy has no external provider — and the parser refuses it at `external` with `'policy external' is not supported. Write the policy body inline: P: policy { <expression> }.`

<!-- poly-verify: fragment:entity wrapper:policy-facts -->
```poly
IsAdult: policy { Age >= 18 }
CanProcess: policy { isActive is true and role is "admin" }
```

**Expressions are product DSL text only** — there is no JSON expression format. `add(kind: policy)`
and `oracle_expression` take the same DSL fragment syntax as policy bodies here. Named-policy
evaluate is `create_instance` then `evaluate_policy(instanceId)`.

### Expression Grammar (Shipped in Phase 1a/1b)

The DSL accepts boolean and scalar expressions through the following precedence levels
(highest to lowest):

| Level | Operators | Example |
|-------|-----------|---------|
| Primary | `Number`, `"string"`, `true`, `false`, `null`, `(expr)`, property name | `42`, `"hello"`, `true`, `Name` |
| `-` | Prefix unary, binds tighter than `*` and `/` | `-3`, `-Balance`, `-(Total + 1)` |
| Arithmetic (multiply) | `*`, `/`, `%`, left-assoc | `Net / Count`, `N % 7` |
| Arithmetic (add) | `+`, `-`, left-assoc | `Net + Fee`, `Net - Fee` |
| Comparison | `==`, `!=`, `>`, `>=`, `<`, `<=`, `is`, `is not` | `Age >= 18`, `Status is "active"` |
| `not` | Prefix unary | `not Suspended` |
| `and` | Binary, left-assoc | `Age >= 18 and Status is "active"` |
| `or` | Binary, left-assoc | `Total > 0 or Rush is true` |

`%` is the remainder operator, with C#'s rule: the result takes the sign of the dividend, so a dividend of
`0 - 10` with a divisor of 7 gives `-3`. It is defined for `Number` and `Decimal`. `Number` `/` is integer
division, so `%` is how a rule gets a remainder (a cents split, an every-Nth check). A zero divisor is not
diagnosed for `%` or for `/` yet (#502).

An expression may nest at most 256 levels: the top level is 0, and each paren group, `not`,
comparison operand, `where` body or call/match argument adds 1, while a chain of N operands
adds N - 1, and so does a path of N words (`a b c` is one navigation per word after the first).
Deeper input is a positioned parse error, not a crash, and the parser enforces the limit without
depending on the host's stack size. That is a guarantee about parsing only: the steps after the
parse (analysis, compile, export) still walk the tree and need enough stack for it. The compiler
runs those steps on a stack of its own, so a legal document compiles from any caller thread; a
direct `session.Lower` or `DomainDslPrinter` walk on a thread too small for the tree fails with
`DSL0004` naming the stack, never a process abort.
Statements have their own limit of 64 levels (§6).

Valid expressions:
<!-- poly-verify: skip:illustration owner:M-02 -->
```poly
Age >= 18
isActive is true and role is "admin"
not Suspended
(Total > 0) or Rush is true
Status is "active"
```

Unary minus negates a `Number` or a `Decimal` expression: `-3`, `-Balance`, `-(Total + 1)`, `-2 * 3` (which is
`(-2) * 3`, because the prefix binds tighter than `*`). A `Decimal` operand keeps `Decimal` as its type, so
`-0.25` is a `Decimal`. Negating anything else is DMSEM003 naming the operand's type: `-Label` on a `Text`
property, or a `Duration`, whose arithmetic is not supported yet.

Ordering (`<`, `>`, `<=`, `>=`) is defined for `Number`, `Decimal`, `Date`, `Time`, `DateTime`, `Duration` and `Uuid`
(an optional operand and a `match` result of those types included; an absent optional operand orders false). Ordering `Text`, `Boolean`, an enum, an entity,
a value type or `Binary` is DMSEM003 (`ordering is not defined for 'Text'`); compare with `==` or `!=` instead.
A value type has value equality, so `==` and `!=` are allowed on it. Known wrong (row X-06): the generated C# compares
two value types by value, but the simulator compares the JSON text of an argument payload, so two equal values written
with their fields in another order, or `2` against `2.0` for a `Decimal` field, compare unequal there; so do equal
values a .NET caller passes as dictionaries to `ModuleInstanceExecutor.Invoke`.

Integer division truncates: `7 / 2` is 3. Storing an integral division into a `Decimal` (`assign Total to 7 / 2`,
`assign Total to N / 2 + 1`) is the warning DMNUM001, because the quotient is cut before it widens. A literal with a
zero fraction is a whole number, so `7.0 / 2` is also 3; divide by a `Decimal` property or a literal with a fraction
(`7.5 / 2`, `Total / 2`) to divide exactly. The warning covers an `assign`, a `create` initializer, a `default(...)`, a
function's return, a function argument and an invoke or fan-out argument, along the `+ - * /` spine of the value and
into the arm values of a `match` (each arm is converted to the match's type, so `match { c -> 7 / 2, else -> 1.5 }`
stores 3 for that arm). Like every analysis warning, it is not yet suppressible by an allowance (row T-08).

The other direction is refused, not rounded: a `Decimal` value cannot be stored in a `Number` property, parameter or
return. `assign N to N + D` (with `N: Number` and `D: Decimal`) is DMSEM003, `cannot assign 'Decimal' to 'Number'`,
because the fraction would be rounded away (`3 + 0.5` would store 4). Store it in a `Decimal`, or compute it as a
`Number`. The rule holds at every door — an `assign`, a `create` initializer, a function or action argument and a
`default(...)`. A `Number` value widens onto a `Decimal` target, so `assign Total to N + M` into a `Decimal` is fine.
Ruling Q6 allows a proven-safe rounding instead; that proof does not exist yet (row T-12), so nothing is rounded
silently.

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

**An unlinked `optional` navigation reads as absent.** `advisor Score` where `advisor` is declared `optional`
and the instance has no advisor is the same absent value an omitted `optional` property has, and every position
treats it that way (#936):

- a comparison answers the optional-value rule above: `advisor Score > 1` and `advisor Score is 1` are false,
  `advisor Score is not 1` and `advisor Name is not "Pat"` are **true**, and `advisor Due is null` is true while
  `advisor Due is not null` is false — the last two are how an absent read is tested;
- a policy, an action `require` or a stage `require` that reads it is false, so the action is refused with `Policy`
  (`400` over HTTP), and the same predicate under `require not` is **true**, as `not (OptDue < Today)` is true
  while the optional date is absent;
- an `assign` into an `optional` target stores absent (`assign Note to advisor Name` leaves `Note` null); into a
  required target it fails `Required` (`'Target' is required.`) and changes nothing;
- an `invoke Rel.Action` on an unlinked link stays a `Required` refusal: there is no instance to run the action on,
  and a silent no-op would skip an effect the document asked for.

`exists` follows the same rule: `exists advisor mentor` is false while `advisor` or `mentor` is unlinked, and
`not exists advisor mentor` is true. One host gap remains: an absent value assigned into an `optional` **Number**
target is `0` in the simulator and null in the printed module (an absent `Text` target agrees), listed under
*Known wrong*.


<!-- poly-verify: skip:illustration owner:M-02 -->
```poly
assignee Active
customer Tier is "VIP"
assignee exists
not certificate exists
customer where Status is "Active" and CreditLimit >= 1000
```

#### Collection Quantifiers (Q3′)

Policies can **observe** and **evaluate** collection relationships with `any`, `all`, `none`, and `count`.
Quantifiers lower to a foreach over the collection navigation in both simulate and the
standalone C# export (the host supplies the collection; there is no store-aware runtime
eval and no throwing export stub). These require a **OneToMany** relationship from the
source entity. The body is an `and`-chain (use parentheses for `or` inside the body).
Inside a quantifier body used from an action, a name that is both an action parameter
and a property of the related entity binds to the **parameter**.

| Form | Meaning | Example |
|------|---------|---------|
| `any Rel where body` | ∃ related matching body | `any orders where Priority > 5` |
| `all Rel where body` | ∀ related match body | `all items where Reserved is true` |
| `none Rel where body` | ¬∃ related matching body | `none notes where NeedsFollowUp is true` |
| `count Rel` / `count Rel where body` | Number of related (optionally filtered) | `count orders > 5` / `count orders where Status is "Open" > 0` |
| `sum Rel Prop` | Sum of a numeric property over related | `sum lines Price == 150` |

`count` produces a numeric value for use in comparisons. `sum Rel Prop` produces the sum of `Prop`, a
numeric property (`Number` or `Decimal`) on the related entity, over the related set, in `Prop`'s own type:
a `Decimal` sum is an exact decimal (`sum accounts Balance == 3.0`). A non-numeric `Prop` is `DMSEM005`
wherever the `sum` appears: a policy, an assign, a comparison, a create initializer. `sum Rel where …` and `sum Rel (expression)` are **not** forms — only a bare property name
follows the relationship. `first` and `filter` are not forms either: `first Rel where …`, `count filter Rel where …`, and an expression after a quantifier
(`sum Rel (expr)`) are positioned parse errors at the word that name the supported spelling (`'first' is not supported; use 'any', 'all', 'none', or 'count' with 'where'`).
`any`/`all`/`none` produce booleans. A body after `where` is checked against the related entity: its names
are that entity's properties (and the action's parameters), and it must be Boolean, so
`any lines where Qty == "x"` is `DMSEM003`.

**Every expression has a type.** An expression the analyzer cannot type, where its type is needed (assigned,
compared, passed, returned, a condition or an arithmetic operand), is `DMSEM006`
(`cannot determine the type of '…': <why>`), unless a more specific error already names it: a name that is no
property, parameter or enum member is `DMSEM005` wherever it appears, an unknown function `DMFUN001`. Typical causes: a path through a `many` relationship
(`parts Qty`: use `any`/`count`/`sum`), a name that is no property, parameter or enum member, a relationship
read in a function body (a function has only its parameters). Operands of different kinds do not compare:
`5 == "x"` and `count lines == true` are `DMSEM003`. A `default(...)` value is checked the same way, against the
property's type, and cannot read the entity's properties (it is evaluated before the instance exists).
A pack that adds its own expression form must register the form's type (`SessionBuilder.Meaning.Inference.Register`)
and analyze with the session that parsed it; a form with no registered type is `DMSEM006` wherever it is used.

**Empty collection semantics:**

| Form | When related set is empty |
|------|--------------------------|
| `any Rel where ...` | `false` |
| `all Rel where ...` | `false` (no vacuous true) |
| `none Rel where ...` | `true` (¬any) |
| `count Rel` | `0` |
| `sum Rel Prop` | `0` |

<!-- poly-verify: fragment:entity wrapper:policy-quantifiers -->
```poly
HasPriorityOrder: policy { any orders where Priority > 5 }
AllHighValue: policy { all orders where Total > 100 }
NoRush: policy { none notes where NeedsFollowUp is true }
OpenOrderCount: policy { count orders where Status is "Open" > 0 }
TotalOrderCount: policy { count orders > 5 }
OrderTotal: policy { sum lines Price == 150 }
```

**Analysis rules:** relationship must be OneToMany from the source; body properties validated
against the target entity; reverse-side / ManyToMany / OneToOne rejected (`DMSEM005`). A
self-relationship (`reports: many Employee`) is read like any other collection: the quantifier
walks the instance's own collection, one level, and does not descend into the children's
collections unless the document writes a nested quantifier.

#### Temporal Clock Dates (`Now`, `Today`, durations)

The **temporal library** (loaded by the product language default — MCP sessions and `ExtensionCatalog.Core.Language`)
owns the **Date**, **Time**, **DateTime**, and **Duration** catalog types, clock date values, and
relative date arithmetic. These spellings parse to `Now` / `Today` / `DateOperation` IR, pass
analysis, round-trip through `export_dsl`, and are authorable in **assign RHS and policy comparisons**:

<!-- poly-verify: skip:drift:DMSEM003 wrapper:time-facts owner:M-02 -->
```poly
Renew: action {
  assign DueDate to Now - 12 Days
  assign RenewedAt to Today - 3 Months
}

IsExpired: policy { ExpiryDate < Today }
Replenished: policy { DueDate + 14 Days > ExpiryDate }
```

| Form | Meaning | Notes |
|------|---------|-------|
| `Now` | Current UTC timestamp (clock read) | Exact spelling `Now`; folds to clock IR |
| `Today` | Current calendar date (clock read) | Exact spelling `Today`; folds to clock IR |
| `N Seconds` / `Minutes` / `Hours` / `Days` / `Weeks` / `Months` / `Years` | Relative duration | Singular and plural accepted; exact PascalCase. Milliseconds also as `ms` |

| `DateExpr ± N <unit>` | Offset a date/time by a duration | `Now`, `Today`, or a Date/DateTime/Time property on the left |
| `DateProp ± count` | Offset a Date or DateTime by a whole day count (`Due + n`) | the left is a Date or DateTime property or action parameter; the count is a Number, never a Decimal |
| `DateTimeProp ± DurationProp` | Offset a DateTime by a Duration value (`StartsAt + Length`) | the left is a DateTime property, parameter or `Now`; the Duration is a property, a parameter or a date difference (`Stamp + (Finished - Started)`). Lowers to `DateTime.Add`/`Subtract(TimeSpan)` on every door. A Date left is refused (it has no `TimeSpan` addition) |
| `DateProp - DateProp` | The difference, a Duration (`Finished - Started`) | two Date or two DateTime properties or parameters; assign it to a Duration property or compare it with a Duration |
| `assign Len to 30 Minutes`, `Len > 1 Hours` | A bare duration as a Duration value | assigned to a `Duration` property or compared with one, as a `default(...)` is; Months and Years have no fixed length and are rejected |

Clock-resolution units (`Milliseconds`/`ms`, `Seconds`, `Minutes`, `Hours`) apply to `Now`, `DateTime`, and `Time`. Calendar units (`Days`, `Weeks`, `Months`, `Years`) apply to `Now`, `Today`, `Date`, and `DateTime`. `Date`/`Today` + hours (or finer) is rejected at analysis; `Time` + days (or coarser) is rejected. A calendar count must fit in a 32-bit int (`Dt + 30 Years` is fine, `Dt + 4294967297 Years` is a `DMSEM003` error): the offset lowers to an int day/month/year count, and no date holds more.

**Fail-closed (parse):** unknown units (`12 fortnights`) are a **parse error** — never a
vacuous `DateOperation` and never a dropped unit. A bare `Number + Days` with no temporal left
operand is rejected at analysis, never a silent numeric constant. `Date + Date` (clock nodes
or two `Date` properties) is rejected at analysis.

**Fail-closed (library not loaded):** without the temporal library, `Date` / `Time` / `DateTime` /
`Duration` are unknown catalog types, `Now` stays a plain `PropertyAccess` (never a clock read),
temporal authoring (`Now - 12 Days`, `5 Days`) fails at parse, and `DateOperation` printing throws.
Product sessions load Temporal implicitly at Load. A raw `SessionBuilder` that never calls
`LoadTemporal` does not get clock meaning or temporal primitives.

**Refused at analysis, so no door runs a different answer:** a Decimal day count of any kind (`Due + 1.5`,
`Due + Days` with a Decimal `Days`, on a Date or a DateTime): use a whole Number count or a unit literal such as
`36 Hours`; a **Date** plus or minus a Duration *value* (`Due + Len` on a `Date` property: a Date has no `TimeSpan`
addition, so write the property as `DateTime`); a Duration offset whose left or Duration operand is a path, an owned
member, a `match` or a call (`part Due + Len`, `Start + part Len`: read it into a property first); an optional Duration
operand and an optional DateTime left (an absent value has nothing to add); a count offset whose left is not a date
property or parameter (`Today + 1`, `(Due + 1) + n`, `part Due + n`: write the offset as `Today + 1 Days`);
`default(Now + 7 Days)` (a date operation has no default lowering: use `default(Now)` or `default(Today)`); and a bare
duration anywhere but an assign to a `Duration` property, a comparison with a `Duration`, or the `default(...)` of one. A bare duration one too long for a `TimeSpan` is rejected. An `optional` Date, DateTime, Time or Duration compared with a required value of the same type (`OptDue < Today`, `OptLen > 1 Hours`) is C#'s lifted comparison on every door: an absent operand makes `<`, `<=`, `>`, `>=` and `==` false and `!=` true.

So a policy such as `OptDue >= Opened` used as an action's `require` is false while `OptDue` is absent, and the action is refused; `not (OptDue < Today)` is true while it is absent; and two optional values that are both absent are `==` but neither `<=` nor `>=`. To accept an absent value write `OptDue is null or OptDue >= Opened`; to demand a present one write `OptDue is not null and OptDue >= Opened`. (There is no entity-level constraint expression: a policy is the form that carries one, and a stage `require` is checked when the instance is created as well as by the actions that run in the stage.)

<!-- poly-verify: valid -->
```poly
domain Tasks
uses temporal
Task: entity {
  Opened: Date
  OptDue: Date optional
  Done: Number optional
  Accepting: policy { OptDue is null or OptDue >= Opened }
  Demanding: policy { OptDue is not null and OptDue >= Opened }
  Finish: action require Accepting { assign Done to 1 }
}
```

**Create-time defaults, assign-to-clock, and policy clock reads are shipped.** `default(Today)` /
`default(Now)` and `assign Prop to Today` / `assign Prop to Now` evaluate at instance create /
invoke (C# export emits a `??` coalesce). `Now` and `Today` share one UTC clock on every
path: `default(...)`, bare assign, offsets, and policy reads all lower to `DateTime.UtcNow`
(`Now`, and `Today` on a DateTime/Timestamp property) or `DateOnly.FromDateTime(DateTime.UtcNow)`
(`Today` on a Date property). They do not use the machine-local `DateTime.Today`.
Offsets (`Now - 12 Days`) author, lower, and execute as `Add*` on that clock value.
Adding a duration to a *required* Date or DateTime that was never set fails with `'PropertyName' is not set` and does not change the instance. An offset or a difference on an `optional` date or date-time is rejected at analysis (#648): the property may be unset, and no door computes with an absent date.
Compare a `Date` with `Today` and a `DateTime` with `Now`. A mixed comparison (`Date < Now`,
`DateTime < Today`) is rejected at analysis.
A `DateTime` ordering (`<`, `<=`, `>`, `>=`) compares instants, not clock fields: a value whose `Kind` is `Local` is
read as its UTC instant (`ToUniversalTime()`), and a `Utc` or `Unspecified` value is read as its clock time — the one
clock `Now` reads. So a .NET caller may pass `DateTime.Now`, or a value parsed without a marker, and the policy answers
for the instant it names. An `optional` DateTime is still ordered only when it is present.

**Explicitly NOT shipped:** `schedule at` / `at <time>`, business days, and named time zones are out of scope — no
`at 9am`, no business-day arithmetic, no `TimeZoneInfo` conversion. A `DateTime` still compares as its UTC instant,
above, because `Kind` marks the value rather than naming a zone the domain models.

**Rules:**
- `Rel Prop` on `many` relationships is invalid (use `any Rel where …` — Q3′ shipped). Cardinality validation is enforced at domain analysis time; the parser accepts the syntax but the analysis pipeline will reject it when relationship metadata is available.
- `Rel exists` on `many` is allowed (non-empty outbound-link intent). **Runtime is store-link presence** when domain-bound with a store — see dual paths below.
- Cross-entity reads (path-prefix, exists, where) are legal in policies, require, assign RHS, and **`if` conditions** (same preprocess as policy eval).
- Cross-entity writes (nav path as assign target) are banned.
- **Path-prefix / owned / quantifier / Rel exists** require session instances + links when the name is an outbound relationship. Ownership (`SourceOwnsTarget`) is a modeling flag; evaluation uses the same outbound links as path-prefix / many.

  **Product evaluation path:** `create_instance` → `link_instances` (when the policy reads related data) → `evaluate_policy(entity, policy, instanceId)`. Same session instances as `invoke_action`. Resolves **singular path-prefix** (including **to-one multi-hop** e.g. `loan book Title is "Classic"`), **Q3′ quantifiers**, and **`Rel exists` / `not Rel exists`** against the linked instances. **Fail closed:** missing `instanceId`, instance, or domain metadata. Empty links: a path-prefix read is absent, so its comparison answers the optional rule (#936, section 10); `Rel exists` → **false**; `not Rel exists` → **true**. Bare path-prefix on `many` is analysis-rejected (use `any`/`all`); multi-link at any hop throws at eval. `poly_check` with an `expression` is a fragment probe against the model's declared functions and enums, with each property it reads bound in `bindings` — not named-policy evaluate, and with no linked instances a relationship path still fails closed.

**Shipped in the current product surface:**
- Arithmetic (`+`, `-`, `*`, `/`, `%`) in expressions
- Conditional effects (`if (expr) { effects } else { effects }`) — exists / path-prefix / quantifiers in the condition lower the same way as in policies
- Invoke effect (`invoke ActionName` with optional arguments; cross-entity via `invoke RelName.ActionName`; fan-out via the `for` form — one mode, no `any`/`all`/`each` quantifier)
- Action parameters (`actionName: action (param: Type, ...)`)
- `default` constraints and enum-typed properties
- Ontology term metadata (`iri`, `label`, `description`, `term`) on entities, properties, and enum members — authored, printed, and exported to JSON Schema
- Owned navigation declaration (`rel: owned Entity`) + **to-one path-prefix** policy reads (single-hop and multi-hop chains; instances + `link_instances` required)
- Temporal clock dates (`Now`/`Today`, `N days`/`N months`, `DateExpr ± duration`) — authoring, analysis, `export_dsl` round-trip, and VM execution (`DateTime.UtcNow` / `DateOnly.FromDateTime`)

**Not yet shipped** (planned for future phases / residual gaps):
- **Product DSL for IR-only `OwnedAccess`** (nested value-doc shape) — path-prefix → `RelationshipNavigation` is the product authoring surface; do not treat bag `OwnedAccess` as a second policy product path
- Dedicated many-owned policy demos beyond Q3′ quantifiers on plain `many` (ownership flag is unused at eval; use `any`/`all`/`none`/`count` on `many owned` the same way)

### Shipped-surface boundaries

Decisions that narrow the shipped claim so authoring fails loud instead of silently
diverging:

- **`unique` is enforced by the session's storage port** (the compiled module's own
  check, run on every create and on every assign to the property) and projected to
  storage. `export_domain_to_csharp` prints that same compiled module, so both the
  create factory and any action that assigns the property take an `IStoragePort db`
  parameter and return `Unique constraint violated` before the write — the guard is the
  module's, not a separate export-only stub. This is the same body a source that writes
  `uses persistence` prints. `poly_export csharp` returns the whole module as **one**
  compilation unit (one banner, one `#nullable enable`, the merged `using` set, one file-scoped
  `namespace`), so saving it as a single `.cs` file compiles; `fileCount`/`files` name the
  per-entity files `polyc build` writes instead. A non-empty `uses` list replaces the implicit set
  `[storage, persistence]`, so a `unique` property needs `persistence` or a vendor library in
  that list. A vendor library is persistence for its database: listing `persistence` beside one
  (`uses persistence` with `uses sqlite`) loads the vendor only. Authoring is where the rule bites: a document that lists `uses storage` or
  `uses http` and nothing else is `DMLIB001` at define time, in `polyc` and in every MCP
  authoring call, instead of silently losing the constraint. The MCP execution overlay
  (`McpSessionStore.EnsureExecution`) still adds `persistence`; with the rule in place it adds
  no `unique` guard the document did not ask for, and its removal is C-08's. The module declares
  no public uniqueness member: `Create` carries the port guard and an assign carries the same
  guard inline. A private `EnsureUnique` remains because the lowered assign shape names it before
  the storage library rewrites that call; it is unreachable from any printed module (a `unique`
  with no storage library is DMLIB001, and the rewrite replaces every call) and it fails closed
  rather than accepting a duplicate. The EF model uses a synthetic integer `Id` key. Every
  `unique` property, including a second one on the same entity, is `HasIndex(...).IsUnique()`.
  Duplicate values fail loud at the session storage port and at the database.
  An absent `optional` `unique` value is not a key: `Create` neither checks nor records it, so any
  number of instances may omit it.
- **`Now`/`Today`/`Guid` are authorable in `default(...)`, assign RHS, and policy bodies**
  (with the temporal library, default in MCP `apply_dsl`). Policy/VM evaluation of `Now`/`Today`
  executes as `DateTime.UtcNow` / `DateOnly.FromDateTime`. A bare lowercase `now`/`guid` in a
  policy is still rejected at analysis ("property does not exist").
- **`pattern(regex)` validates stored values at write time** (create/assign) — it is a
  constraint, not a query/read filter. A body .NET cannot parse is rejected at define as
  `DMSEM004`, one positioned error per distinct body. A trailing unescaped `$` means
  absolute end of input (the guard is anchored as `\z`), so a final newline is rejected.
  Grep-style read-time matching against stored text is not expressible.
- **Quantifiers lower to foreach on both paths.** The C# export and the simulator both
  lower `any`/`all`/`none`/`count … where` to a foreach over the collection navigation
  (the host supplies the collection). Path-prefix reads and `Rel exists` / `not Rel exists`
  lower to standalone member access (to-one hops, count-vs-null checks for collections).
  A `for` predicate policy that reads path-prefix or tests `exists` on a property (also
  inside a quantifier body whose relationship resolves) is still **rejected at analysis**; a relationship `exists` or
  quantifier predicate is allowed and runs as the target's bool method.
  Fail closed without a store on the simulate path.
- **Relative date ordering is shipped** for the same clock family. A `Date` property
  compares with `Today` (e.g. `ExpiryDate < Today`); a `DateTime` property compares with
  `Now`. Mixed `Date`/`DateTime` comparisons are an analysis error. Same-family
  comparisons parse, analyze, round-trip, and evaluate on the VM via `evaluate_policy` /
  `invoke_action`.
- **Expressions are type-checked at analysis.** Wrong-typed comparisons, assigns,
  arithmetic, and defaults (e.g. `Name >= 18` on a `Text` property, `default(Today)` on
  a `Number` property) are rejected at authoring time — the export and runtime no longer
  receive type-confused expressions.
- **Property constraints propagate onto effects, and every write gets a verdict.**
  An effect that writes a property with a `range(...)` constraint is checked against it at
  analysis: a literal value (or a derived expression whose inferred value range is entirely
  outside, e.g. `assign Age to Age + 200` on `Age: Number range(0, 150)`) is a
  **`DMEFF008` error**; an expression that *can* fall outside (e.g. `assign Qty to Qty - 100`
  on `Qty: Number range(0, 100)`) is a **`DMEFF008` warning**. This covers `assign`,
  entry/exit and `when` effects, and `create`/`create in` initializers, which are evaluated
  in the **creator's** scope (the creating action's properties and parameters), never the
  created entity's. The action's guard policies are
  considered **additively**: a `require` gate that
  narrows a property's value range is combined with the property's own constraints before
  judging the downstream violation — e.g. `assign Qty to Qty + 10` under
  `require Qty <= 80` on `Qty: Number range(0, 90)` is provably within range and produces
  no diagnostic. A guard narrows through comparisons of a property with a literal (on
  either side) or another property, joined by `and`, `or` and `not`: `or` narrows to the
  span of its arms (`X <= 4 or X <= 8` gives `[…, 8]`, while `X <= 2 or X >= 9` narrows
  nothing), and `not` negates (`not (X >= 10)` is `X < 10`). On a `Number` a strict bound
  is exact (`N < 100` gives N ≤ 99, `X > 0` gives X ≥ 1) and `X != 10` drops 10 when it
  is the range's end; on a `Decimal` a strict bound keeps its endpoint (`D < 5.5` gives
  D ≤ 5.5) and `!=` narrows nothing, except that a point range minus its value is empty
  (`D == 5.5 and D != 5.5` is `DMCS001`). A guard no value can satisfy (`X > 10` on
  `X: Number range(0, 10)`) makes the action's preconditions unsatisfiable (`DMCS001`).
  An optional property's range describes its present values: a guard that also holds when
  it is null (`not (W >= 10)`, `W != 5`) narrows those values but never rejects the guard.
  `require not P` narrows by the negation of the entity's policy `P` (`require not Small`
  with `Small: policy { Qty <= 10 }` gives Qty ≥ 11). An action's `require not P` lifts its
  stage's `require P` gate at runtime, so that stage policy does not narrow the action.
  Neither applies to an assign that follows a `for`, a transition, a cross-entity or fan-out
  `invoke`, a `create` or a `create in` on its path: that assign is judged by the action's
  other `require` gates and every policy of its stage, as before `require not` narrowed.
- **Silence means verified (Q4)** — with one exception: a call-chain postcondition whose
  value range is not known (below). An interval comes from a literal, a ranged property or
  parameter (narrowed by a `require` gate, an `if`, or a stage policy), `+`, `-`, `*` and
  `/` (a quotient only when the divisor's whole range excludes 0; #502 owns the zero case),
  a `match` (the hull of its arms and its required `else`, only when every one has an
  interval), and a Text's length (below). A write analysis cannot
  decide — a bare property or parameter with no bound, a call, a `%`, a relationship leaf,
  a division by a range that includes 0, a `match` with an unbounded arm, or a constraint
  kind with no flow decision — is a **`DMCS003` warning** naming the write, the property and the constraint.
  So with
  `A: Number range(0, 10)` and `B: Number range(5, 20)`, `assign A to B` warns
  (`DMEFF008`, "can fall outside"), and `assign A to U + 1` with an unbounded `U` warns
  (`DMCS003`, no usable bound). One **fact** is recorded per write that reaches the
  recorder; the worst verdict across every stage context the action can run in decides the
  reported code; the literal and parameter arms report one diagnostic per violated
  constraint, and the parameter arm's downstream duplicate is suppressed only when exactly
  one assign reads the parameter. Observed flows go through the same recorder: a
  flow whose source carries no same-kind constraint is a `DMCS003` warning naming the flow
  plus an unverified fact on the target, a source whose value range sits inside the target
  is verified, and a wider source keeps its `DMEFF008` with a "can fall outside" fact
  beside it (an allowance still downgrades that finding). A `create` or `create in`
  initializer reads the same narrowed values as an assign at that point of the creating
  action: its `require` gates and stage policies, an enclosing `if`, and an earlier assign
  (`require Small { create Item { Qty: Qty } }` reads Qty ≤ 10). A create inside a `for`
  body, or after an effect the analysis does not follow on that path (a `for`, a
  transition, a cross-entity or fan-out `invoke`, a `create` or a `create in`), is judged
  against the declared ranges, as is a create outside an action body.
  `range(...)`, `length(...)`, `pattern(...)` and `equals(...)` are the kinds the runtime enforces
  on a write, and the kinds decided against a computed value on an `assign` or an initializer.
  A `length` is decided against the value's **length interval**: a Text's own `length(...)`
  (a Text with none is `[0, +∞)`), `+` with a literal or another bounded Text (the two lengths
  added), or a `match` over such values. A copy or a concatenation that lies inside the target's
  length is verified; one that does not is a `DMEFF008` warning naming the interval
  (`[3, 5] can fall outside length(1, 3)`) — never an error yet, because row V-08 owns the error
  side of a length. A `pattern` or `equals` is proven only when the value carries
  the identical declared one. Anything else is unverified: a Text whose length the walk cannot
  name (a relationship leaf, a call), a different pattern. A literal assigned directly is still
  checked outright and is an
  error when it fails. Excluded: `unique` (it depends on stored state, not the written value, so a
  proof about one write cannot cover it), `required` (`DMEFF014` owns it) and enum membership
  (the type check owns it). The **call chain** decides
  each postcondition through the same range decision and records a fact for every
  non-satisfied outcome (deduplicated per callee write node, so a satisfied write is not
  counted twice): an error is a violation fact and a warning an unverified fact. A
  call-chain postcondition whose value range is not known is still skipped silently.
  `poly_check` reports `unknown`, not `proven`, while any write could not be verified
  (a write finding, or a `DMCS003` from `default()` beside a `range` or `length`), and its
  summary and `data.unverifiedFindings` say how many findings were counted; `proven` means
  every write was decided inside its constraint. To resolve an `unknown`, bound the source, widen the target
  (drop or loosen the constraint), or, for a value that is a property or an expression, guard
  the write with a `require`/`if` that narrows its range. A DSL action parameter cannot yet
  carry a constraint (row F-08), and a bare parameter write ignores guards, so a `Text` or a
  `Number` parameter written into a bounded property stays `unknown` even under
  `if (v <= 10)` (`create Hall { Name: name }` into `Name: Text length(1, 80)` is the common
  case) until the source is a property with the same bound or the target is widened;
  `poly_simulate` executes the write meanwhile. A
  call-chain finding is attributed to the **callee** element that assigns (the chain is
  named in its message), so a summary scoped to the caller reads `proven` while the whole
  domain reads `violated`.
- **Invariant verification is per-stage-context and combinatory.** An action valid in
  multiple stages is analyzed once per stage: each stage's policies (plus the action's
  require gates) combine to a **net constraint per constraint type**
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
  judged against that narrower range); the else-branch uses the negated condition
  (`not`, `and` and `or` included, as for a guard). This removes false-positive downstream warnings on guarded
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
| Relationship navigation | ✅ | ✅ **shipped** (path-prefix) | `customer Tier`, multi-hop `loan book Title is "X"` (to-one hops only); **instances + link** at `evaluate_policy` |
| Existence check | ✅ | ✅ **shipped** (postfix `Rel exists`) | Store outbound-link presence for relationship names; fail-closed without store; empty → false |
| Scoped filter (`where`) | ✅ | ✅ **shipped** (`rel where and-chain`) | `customer where Status is "Active"` |
| Owned / related single-hop | ✅ | ✅ **shipped** (path-prefix) | `profile City is "Metropolis"` — same space-delimited syntax as to-one nav; **requires instances + link** at `evaluate_policy` |
| Nested multi-hop path-prefix | ✅ | ✅ **shipped** (to-one hops) | `loan book Title is "Classic"`; many-middle requires `any`/`all` quantifiers |
| Collection quantifiers (`any`/`all`/`none`/`count`) | ✅ | ✅ **Q3′ shipped** | `any items where Status is "Open"`; foreach over the collection in simulate and export. |
| Arithmetic (`+`, `-`, `*`, `/`, `%`) | ✅ | ✅ **shipped** | `Total + 5 > 10`, `Total % 7 is 0` |
| Action parameters | ✅ | ✅ **shipped** | `actionName: action (param: Text) { ... }` |
| `match { cond -> value, …, else -> value }` | ✅ | ✅ **shipped** (#409) | Expression-level branching; see below. |

**Expression bodies are DSL text only** — JSON expression bags were retired with the catalog minify.
Named-policy evaluate runs on a session instance (`create_instance` + `evaluate_policy`). `oracle_expression`
is a fragment probe: relationship/owned path-prefix and relationship `exists` fail closed without session instances.

### `match` (#409)

```
assign Tier to match { Qty > 100 -> "big", Qty > 10 -> "mid", else -> "small" }
```

- A **guard chain with no scrutinee**: arms are tried in order and the first whose condition
  is true supplies the value. Arms are separated by `,`; each is `condition -> value`.
- **`else -> value` is required and must be the last arm**, so a match is always total. A missing,
  repeated or non-final `else` is a positioned parse error. There is no implicit default.
- Arms are **expressions**, never effects. It works wherever an expression does (assign RHS, `if`
  conditions, policy bodies, `create` initializers, nested inside another `match`, or as a function-call argument such as `Add(match { Qty > 3 -> 10, else -> 1 }, 1)`).
- Conditions must be Boolean (**DMMAT001**). The arm values and the `else` must unify to one type
  (**DMMAT002**): the same type, or a `Decimal` with a `Number` (every `Number` arm is converted to
  `Decimal`, so `match { Qty > 1 -> 1.5, else -> Qty }` is a `Decimal` whichever arm comes first, and
  assigning it into a `Number` is DMSEM003). `Text` and `Number` do not unify; neither do a `Date` and a
  `DateTime`, or an enum and a `Text` that is not a string literal (a string literal unifies with an enum arm
  only where the enum is the slot: assigned to that enum, `assign Tier to match { c -> Gold, else -> "Std" }`,
  compared with an enum property of the entity, or nested in such a match; a literal that names no member of
  the enum is DMSEM003, as in `Tier == "Nope"`). An arm that
  is `null` or optional makes the match optional: it compares and orders false when absent, arithmetic over it
  is absent when the match is (into an optional property), and storing that arithmetic in a required property is DMSEM003. A
  `match` in `default(...)` is a positioned error (the generated C# cannot print it). Two arms with the same
  condition are an error, the later arm is unreachable (**DMMAT003**); only identical conditions
  are seen, overlapping ranges are the author's problem. A bare name that is no property, parameter or
  enum member is an error (**DMMAT004**).
- Malformed forms (`match { }`, an arm with no `->`, an empty condition or value, a stray or trailing
  `,`) fail with a positioned parse error at the token that faults, and a `DMMAT00x` finding is
  positioned on the `match` keyword; the message never restates the position.
- Lowers to a nested conditional; simulation and printed C# agree. Arms are lazy in the result
  (only the chosen value is selected), but a quantifier inside an arm is evaluated up front.
- Not shipped: matching on a scrutinee (`match X { "a" -> … }`), patterns, and constant-folding of
  provably unreachable arms.

## 9. Supported Effect Summary

| Effect | Can appear in |
|--------|---------------|
| `transition to Stage` | action, entry, exit |
| `assign Prop to expr` | action, entry, exit |
| `create Type { ... }` | action, entry |
| `create in Rel { ... }` | action, entry |
| `invoke Action` / `invoke Rel.Action` | action, entry (self; OneToOne source-only; fail-closed DMEFF007; depth-limited) |
| `for Rel as name [where policy \| where in stage] invoke name.Action` | action, entry (OneToMany source-only fan-out; fail-fast; zero records is a no-op) |
| `if (expr) { … } else if … else { … }` | action, entry, exit |

A `transition` in an exit block must target the stage every hop leaving that stage targets (`DMEFF017`; see Entry/Exit Effects). An entry block admits the effects above; an exit block rejects `create`, `create in`, `invoke` and `for … invoke` (`DMEFF018`). A `create` or `create in` initializer may not supply a property the new instance's entry chain may assign (`DMEFF019`; see Entry/Exit Effects).

**Linking existing instances:** graph wiring happens through `create in Rel { … }`, which the module's own body links on the instance. To connect already-existing instances, the MCP `link_instances` and `unlink_instances` tools write the relationship field on the instance (the session executor's `Link` / `Unlink`) with relationship + entity-type validation at the tool boundary. `unlink_instances` refuses to drop the last owner of an `owned` target (see §4).

**`link` / `unlink` effects (syntax and analysis only):** `link Rel to binder` and `unlink Rel from binder` parse, print and round-trip, but analysis refuses every use with `DMEFF020` until the lowering lands (L-13). Use the MCP link tools until then. The target is a bare binder of an enclosing `for x in Rel { … }`; the relationship must be declared on the action's entity and hold the binder's type. An owned navigation's owner is fixed, so `link` / `unlink` on one is refused (`DMEFF001`): it is written only by `create in`. `link` and `unlink` are reserved lowercase words: until contextual keywords land (G-21) they cannot be used as any name (entity, member, relationship, stage, action, parameter, enum value or domain); `Link` is still an ordinary name. Because the statements are not executable yet, link relationships through the host's link operations (`link_instances` / `unlink_instances`) until L-13.

<!-- poly-verify: invalid:DMEFF020 -->
```poly
domain Board

Task: entity { Name: Text }
Board: entity {
  Backlog: many Task
  Active: many Task
  Move: action {
    for t in Backlog { link Active to t }   // error DMEFF020: not lowerable until L-13
  }
}
```

## 10. Do NOT Use (Unsupported in Phase 1a/1b)

| Construct | Why |
|-----------|-----|
| `actor` | Use `entity` instead |
| `schedule`, `parallel` | Not product constructs |
| Action `when Stage` | Not a runtime or emit gate. Place the action inside the stage body; subscriptions stay `when Rel Stage { … }` |

| `relationship Name from A to B` | Use N1 nav properties instead |
| Event/publish/subscribe | Event model retired — stage transitions are the observable |
| `event.Prop` / `event Prop` in `when` bodies | Use optional peer binder: `when Rel Stage as name { … name Prop … }` |

## 11. Additional Features

### Export outputs and their dependencies

`poly_export` returns one artifact: `dsl`, `json-schema`, `csharp`, `markdown`, `html`,
`json-ld`, or `shacl`. `csharp` prints the compiled module as one or more source files, and
the success summary names what the project that compiles them needs: a `Poly.Runtime`
reference for `DomainResult`, `DomainFailure` and `IStoragePort`, and, when the export
includes a generated `<Domain>DbContext.cs`, `Microsoft.EntityFrameworkCore`. `polyc build`
prints the same requirement after its file count in text output. An export over 4 KB is
written to a temp file and returned as `{path, bytes, sha256}`; `inline: true` returns the
text.

### `uses` Contract Contributions

A library may contribute a contract surface from `Register`. `uses <id>` loads the export
package registered under that id; Load never compiles a sibling `.poly`. The surface lands
on the domain as an imported contract, so `bind` resolves unchanged. A caller at a package
boundary pins the surface in source (`uses billing 2024-11-20`); the pin is the public
surface only, never the implementation. The pin is the contiguous same-line token run after
the id, so `2024-11-20`, `v1`, and `1.2.3` each read as one value. A 64-lowercase-hex pin is
the machine **fingerprint**: it is compared to the loaded surface's `Fingerprint`; any other
pin is compared to the package `Version`. Either is exact — a mismatch is `DMCON010` and
Lower refuses. Within one workspace the pin is optional: no pin is not an error, and
analysis records the resolved fingerprint in the bind's facts.

A bind may name the exported endpoint (`Charge__Money`) or the original action (`Charge`).
When the original name matches several exported endpoints, analysis keeps the endpoints
whose payload type accepts the bound parameter (the same type, or a narrower value type with
the same property shape and inside constraints). If one payload is more constrained than the
others it wins and `DMCON011` names the choice; candidates that are not comparable are
`DMCON012` with no guess. A name no endpoint carries is `DMCON013`. A bind covers every
stage variant of the action: all variants must share one parameter list (name and type), or
the bind fails closed rather than splicing into a stage that never accepted that payload.

A hand-written `Name: contract ...` block beside a seeded surface fails closed, as does a
second block with the same name. A version on an id whose library contributed no surface
(`uses temporal v1`) also fails closed. `uses` does not change `contract` block syntax: a
hand-authored block remains the form for a surface no library contributes.

A contribution also declares the host targets its implementation is for. `uses posix` seeds
the POSIX header (`Open`, `Read`, `Write`, `Close`) and declares `posix`; the set is not on the
contract and not in the fingerprint, so another target's package keeps the pin.

**Boundary constraints.** At a bind, analysis checks the caller's argument against the
endpoint's public payload property constraints. The caller must be at least as constrained as
the endpoint. A proof is silent. A caller whose values are disjoint from an obligation is
`DMCON014` and Lower refuses. Anything the constraint merge cannot decide — a caller wider
than the obligation, or a constraint kind with no merge rule — is `DMCON015`, a warning that
does not block Lower — for a `range`, `length` or `equals` obligation. A `required`, `pattern`
or `default` obligation with no same-kind caller constraint keeps the ordinary incompatible
refusal (`DMCON001`).

Two limits keep the check from claiming more than it knows. First, a `require` precondition is
written against the **entity** the exported action came from, so it constrains entity state,
not the argument: a bare property access in one is never decided from the caller's payload and
is reported `DMCON015` rather than `DMCON014`, even when the entity and the payload share a
property name. Only a precondition that names the bound parameter of a primitive payload is
decidable. Second, `Empty` from the constraint merge is a real disjointness proof only for
`range`, `length` and `equals`; a `pattern` or `default` mismatch keeps the ordinary
incompatible refusal (`DMCON001`) instead of being reported as disjoint. A contributed
body is walked on the postcondition half: a write outside a declared `range`, `length`, `pattern`
or numeric `equals`, or a null into a required payload member, is `DMCON014` and Lower refuses; a
write the walk cannot decide, or a payload handed to a call (including in the final `return` or a
local assignment), is one `DMCON015` and the domain still lowers. A body that does not end in a
`return`, or whose factory throws, is also `unknown`. A surface-only contribution is silent. The note is not part of the surface fingerprint, and it never clears a
pin mismatch (`DMCON010`).

### Functions

A top-level `function` declares a pure named member of the domain: parameters, a return
type, and one expression body. The form is what `export_dsl` prints (spaces around `=`
included), so parse → print → parse is lossless:

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
function Add(a: Number, b: Number): Number = a + b

function Pi(): Number = 3
```

- Functions sit at the top level, after the entities whose types they name.
- The body is **one expression** (`a + b`, `3`, `Add(a, 1) * 2`, …). An identifier in that
  expression that names a declared parameter is that parameter. A function is **pure**: the
  body may use parameters, literals, arithmetic, comparisons, `and`/`or`/`not`, and calls to
  other functions. Naming an entity property, a relationship, a quantifier, or a clock is
  `DMFUN005` (a function has no subject entity).
- The body's type must be assignable to the declared return type (`DMFUN006`); two
  functions with one name are `DMFUN007`.
- `public function` is not part of the surface yet; a function has no visibility and
  prints without one.
- A declaration missing its `=` or its body fails closed with a positioned error; the
  parser never folds the following declaration into the body. `function` is not a path hop
  (`a b c` navigation) and is not a duration unit, so an expression that ends in an
  identifier or a number ends at the next `function` declaration.

#### Calling a function

A call is `Name(arg, arg, …)` — the function name, `(`, comma-separated expressions, `)` —
in policies, in `assign` values, and as arguments of other calls or in function bodies. Other
expression positions share the same parser and lowering but are not covered by tests yet.

<!-- poly-verify: fragment:top wrapper:top-default -->
```poly
Cart: entity {
  Total: Number default(0)
  Compute: action { assign Total to Add(1, 2) }              // 3
  Scale:   action { assign Total to Add(1, 2) * 10 }         // 30
  Nest:    action { assign Total to Square(Add(1, 2)) }      // 9
  IsThree: policy { Total is Add(1, 2) }
}

function Add(a: Number, b: Number): Number = a + b
function Square(a: Number): Number = a * a
```

- A call needs the `(` directly after the name. A bare identifier, even one that equals a
  function name, is still an ordinary property or parameter read.
- Arguments are full expressions (including entity properties and other calls). A zero
  argument call is `Seven()`. A leading, doubled, or trailing comma is a positioned parse
  error.
- A call evaluates to the function's declared return type. Simulation and printed C# agree:
  lowering binds each argument to the parameter and lowers the body there.
- A call is inlined where it is written; generated C# has no method for the function, so a C#
  caller cannot call it by name. The body is lowered at the call site, so the printed code and
  the simulator still agree.
- Fail-closed diagnostics (all errors): `DMFUN001` unknown function, `DMFUN002` wrong number of
  arguments, `DMFUN003` argument type not assignable to the parameter type, `DMFUN004`
  recursion (direct or through other functions), `DMFUN005` impure body, `DMFUN006` body vs
  return type, `DMFUN007` duplicate name, `DMFUN008` call in a position lowering cannot place
  (the `default(…)` of a property — assign the call in an action or entry effect instead). A
  call result of the wrong type for its use is the ordinary `DMSEM003`. A finding about the
  declaration itself (`DMFUN004`-`DMFUN007`, and the `DMSEM006` of a return type that names no
  type) is positioned on the `function` keyword; a finding about a call is positioned on the call.
- **Recursion is rejected**, not supported: a body is one expression with no base case, so a
  recursive function never terminates, and lowering would expand it without end.

#### Built-in numeric functions (#547)

Six numeric functions are built in, so a `Decimal` can be rounded to cents without a declaration:

| Call | Meaning | Result |
|------|---------|--------|
| `Round(value, digits)` | round to `digits` places, midpoint **away from zero** (the money rule, never to even) | the operand's type |
| `Abs(value)` | absolute value | the operand's type |
| `Min(left, right)` | the smaller operand | the operands' common type |
| `Max(left, right)` | the larger operand | the operands' common type |
| `Floor(value)` | the largest whole value at or below | the operand's type |
| `Ceiling(value)` | the smallest whole value at or above | the operand's type |

`Round(Balance * 0.015, 2)` rounds a computed fee to cents and assigns it to a `Decimal`;
`Round(1.005, 2)` is `1.01` and `Round(-1.005, 2)` is `-1.01` (banker's rounding would give
`1.00` and is not used). `Min(Balance, Fee) - Abs(Net)` composes with arithmetic, and the
interval analysis knows `Abs` is never negative and takes the hull of `Min`/`Max` operands;
`Round`'s result range is not modelled.

- Each call lowers to one `System.Math` call (`Round`, `Abs`, `Min`, `Max`, `Floor`,
  `Ceiling`), so the simulator, the printed C#, `polyc check` and the generated host agree.
- `digits` is a whole `Number` from `0` to `28`. `2` and `2.0` are both whole; `2.5` is refused.
- Every argument is a present numeric value: `Number`, `Int32`, `Float` or `Decimal`. `Min` and
  `Max` need two operands with a common type (`Decimal` with `Float` has none).
- A whole value (`Number`, `Int32`) needs no call for `Round`, `Floor` or `Ceiling`: it is
  already whole, so the call is that value.
- These names are not reserved. A `function` declared with the same name wins, exactly as it
  does over a contract endpoint, and its body runs.
- Fail-closed: wrong arity is `DMFUN002`; a non-numeric, absent or `Float32` argument, a
  fractional or out-of-range `digits`, or `Min`/`Max` operands with no common type is
  `DMSEM003` at the argument.

Not yet part of the surface (later slices of #406): functions on `value` types and `require`
guards, operator overloads, lowering a function to its own tree artifact with an invoke id,
and function calls in contract payload constraints.

### Action Parameters

Actions can declare typed parameters. There is no `param` keyword: a parameter is a **bare identifier** — the product parser emits it as a property access, and analysis/lowering/invoke-bindings/runtime treat an identifier matching an in-scope action parameter as a **parameter**, not a property. `ParameterAccess` is internal IR, not an authoring form. Parameters are injected for the duration of the action call and do not persist on the instance.

Canonical form puts parameters after `: action` so members stay `Name: kind` (matches `export_dsl`):

<!-- poly-verify: fragment:entity wrapper:entity-bodies -->
```poly
SetName: action (newName: Text) {
  assign Name to newName
}
```

### Constraint Reference

| Constraint | Syntax | Example |
|-----------|--------|---------|
| Optional | `optional` | `Note: Text optional` |
| Unique | `unique` | `Email: Text unique` |
| Range | `range(min, max)` | `Age: Number range(0, 150)` |
| Length | `length(min, max)` | `Code: Text length(2, 10)` |
| Pattern | `pattern(regex)` | `Zip: Text pattern("^\\d{5}$")` |
| Default | `default(value)` | `Status: Text default("Active")` |
| Equality | `equals(value)` | `Status: Text equals("Active")` |
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

Storage projection (EF `OnModelCreating`) is not the same map as the transport DTO
attributes below. What reaches the database:

| Constraint | Database projection |
|------------|---------------------|
| `unique` | `HasIndex(x => x.Prop).IsUnique()` on every unique property. The primary key is a synthetic `int` `Id`, not the unique column. |
| `range` | `HasCheckConstraint` only when invariant analysis verified every writer stays inside the range. An unverified range is not a CHECK. |
| `length(min, max)` | `HasMaxLength(max)` plus a `CK_<column>_length` CHECK. The minimum is checked on the value's UTF-8 byte length and the maximum on SQLite's character count, so the database never refuses a value `Create` accepts (it may accept a few that `Create` refuses). |
| `pattern` | No SQL projection: SQLite has no regular expressions. The pattern is still enforced when the VM writes the value. |
| `default(...)` | `HasDefaultValue(...)` for a literal or an enum member. A runtime default (`Now`, `Today`, `Guid`) has no database default. |
| no `optional` | `IsRequired()` for reference-typed columns. |
| a required link (a to-one not declared `optional`) | A `NOT NULL` foreign key (`IsRequired()` on the relationship). An `optional` link keeps a nullable key (`IsRequired(false)`). |
| delete behaviour | Deleting an owner cascades to its `owned` parts (`OnDelete(Cascade)`); any other required link restricts (`OnDelete(Restrict)`: the principal cannot be deleted while a dependent references it); an optional link is unchanged (`ClientSetNull`). An owned single part with no back-navigation keeps its key on the owner and is unchanged. A shape the analysis cannot decide (a many-to-many) keeps EF's convention. |
| one-to-one pair | The dependent side holds the key: the required side, else the part of an owned pair, else the first declared. |

There is no `DELETE` route and no delete effect in the DSL, so delete behaviour matters to code that
uses the generated DbContext directly. A database file created before this change has a different schema
(nullable keys, no delete action) and must be recreated.

**Why a declared `range` sometimes has no `CHECK`.** The CHECK is built from the
declared range, so it is emitted only when every writer is proven to stay inside it; the proof is
an interval, not a guess, and an unproven writer drops the whole CHECK (fail closed: a CHECK the
domain could legitimately trip is worse than no CHECK). On `Capacity: Number range(0, 100000)`,
`assign Capacity to 5` is the point `[5, 5]` and `assign Capacity to Capacity * 1` is
`[0, 100000]` — both inside, so `CK_capacity` is emitted. `assign Capacity to Capacity + 1` is
`[1, 100001]`, one past the declared maximum; `polyc` warns `DMEFF008: Assigned expression value
range [1, 100001] can fall outside constraint range(0, 100000) on property 'Capacity'.` and the
column gets no CHECK at all. Two consequences: the assign is still guarded when it runs
(`'Capacity' must be <= 100000.` refuses the write), so the domain itself never stores an
out-of-range value; and a writer that bypasses the domain — direct SQL, another client — is not
refused by the database. An open bound keeps the CHECK (`range(0)` with the same `+ 1` proves
`[1, ∞)` inside `[0, ∞)` and emits `capacity >= 0`), and so does any writer whose interval fits;
a wider declared maximum does not help `X + 1`, because the write interval moves with it and
still clears the top. `DMCS003` is the same verdict when the write has no computable interval
at all (`assign Capacity to amount`).

**A `Decimal` range on SQLite.** SQLite has no exact decimal store, so a `Decimal` column is `TEXT`, the
only form there that keeps every digit. SQLite compares a `TEXT` column to a numeric literal as text
(`'9' > '1000000'`), so the CHECK compares the value numerically instead: the column and both bounds are REALs
— `CAST(column AS REAL) >= 0.0 AND CAST(column AS REAL) <= 1000000.0` — and SQLite parses the bound the way it
casts the value. Both sides are then the same correctly-rounded double, and rounding is monotone, so a value
inside the declared range is never refused. A whole-number bound above 2^53 is printed with a `.0` for exactly
that reason: an INTEGER literal is compared exactly, without rounding, and would refuse the value at the bound.
The CHECK is a floor that never contradicts `Create`, which stays the guard: a value that differs from a bound
beyond REAL's 15–17 significant digits can pass it, and `Create` still refuses that value in the domain. It is not
a type check: text that is not a number casts to `0.0` and passes a range that contains 0 (only a write outside the
module can put it there; `Create` never does).

Where the storage pack projects constraints into the DB (`CHECK` constraints), the
Minimal API transport emits them as DTO validation attributes (metadata on the contract;
the module enforces the same envelopes the domain declares):

- `range(min, max)` on a `Number` property → `[Range(min, max)]` on the create DTO and on
  any **action DTO parameter** the value is directly assigned into
  (`assign Stock to amount` ⇒ `[Range(0, 1000)]` on `amount`), or
  `[Range(typeof(long), "min", "max")]` when a bound does not fit `int`. The numeric range
  uses the analysis-**verified** envelope when the invariant analysis proved no effect can
  exceed it; otherwise the declared range.
- `length(min, max)` → `[MinLength(min)]` + `[MaxLength(max)]`.
- `pattern(regex)` → `[RegularExpression(regex)]`.
- no `optional` → `[Required]` (reference-typed properties). Every
  string member of the create DTO is `[Required]`: each member is a required ctor
  slot, and optional members are absent from that DTO. The attribute is transport
  metadata: every DTO member is nullable and presence is the module's `Required` (400).
- Enum-typed properties and parameters (`Genre: Genre`) → `[EnumDataType(typeof(Genre))]`
  declares the member union on the contract (the CLR enum type already enforces
  membership at binding).

Action DTO bounds are **implicit**: not declared on the parameter, but derived from the
action's own effects — a parameter that flows into a constrained property inherits that
property's constraints, merged by intersection across all such targets. This covers
`assign Prop to param` on the action's entity and `create`/`create in` initializer
bindings (`Prop: param`) on related entities. Conflicting targets (e.g. different
patterns) merge to nothing and emit no attribute. A pinned literal in the model
(`EqualityConstraint`) still projects as `[AllowedValues]`.
Author a closed set as an **enum**, not a property constraint.

**Soundness rules for implicit derivation:**
- Only **unconditional** flows contribute. A parameter that reaches a target only inside
  an `if` branch has no universally-provable envelope — intersecting the branch ranges
  would falsely reject valid inputs, so conditional assigns emit nothing (fail-closed).
- Open range bounds (`range(0, )`) keep their open side; the emitted `[Range]` caps it at
  the CLR type's representable bound rather than collapsing it.
- **Known gap:** a parameter passed through `invoke Rel.Action(param: expr)` does **not**
  yet inherit the callee's transitive envelopes (the callee's own DTO does).

### HTTP failures (`uses http`)

The generated host reads a failure's **kind** to pick the status; it never reads the message text. Every error
response is `application/problem+json` (ProblemDetails): `status`, `title` and `detail`, where `title` is the kind
name and `detail` is the failure's message, plus `elementPath` (for example `Item.Qty`) and `guard` (the stage or
policy name) when the failure has them. A client switches on `status` first, and on `title` (the failure kind)
within a status. `type` is not part of the contract in this release (ASP.NET fills it with a per-status link).
Success bodies are unchanged. An unknown id is `404` with title `Not Found`; an unexpected exception in an
action is logged by the host and answers `500` with title `Internal Server Error` and no exception text.

| Status | Kinds |
|--------|-------|
| 409 | `Unique`, `Link`, `Conflict`, `Stage` |
| 422 | `Policy` |
| 400 | `Required`, `Range`, `Length`, `Pattern`, `Enum`, `Arithmetic`, `Argument`, `Constraint` |
| 500 | `Cascade` |

A stage failure (an action the current stage does not offer) conflicts with the resource's state, so it is `409`. A
policy failure (a `require` guard that does not hold) is `422`: the request is well-formed and a rule refused it. It
is not authorisation, so it is not `403`.

**Concurrent actions.** An action route is a read-modify-write of one aggregate, so the host saves with optimistic
concurrency: every stored row carries an integer version the host bumps before a modifying save (a shadow column, not
a JSON member). A save whose row changed after this request read it updates no row, and the request answers `409` with
title `Conflict` and a detail naming the entity and the route's key. The client retries. The host never retries for
you, because a retry would re-run the action's guards against the state it just changed, so two concurrent actions on
one aggregate give one `200` and the rest `409` — the host's `Conflict`, or the module's own `Stage` when the winner
already left the stage — never a lost update and never a second transition.

### HTTP route keys (`uses http`)

A `201 Created` response's `Location` is the route plus the row's natural key as one escaped path segment
(`Uri.EscapeDataString`; non-text keys are formatted culture-invariantly, a Date key as `2026-01-05`), so a key
with a space, `?`, `#` or a non-ASCII letter reads back at that `Location`. A key containing `/` is refused at a
root create with `400` and a message, because ASP.NET does not turn `%2F` into `/` in a route value, so no
`Location` could read it back. An entity created inside a parent action (`create in`) still stores its key, and
that detail route cannot read a `/` key back (F-13).
An entity with no natural key has a shadow key EF assigns on save: its create answers `201` with the
same shape of `Location` (`/api/orders/1`), read back from the tracked entry (#589), so the per-id routes are
reachable from the response even though that `id` is not in the JSON body (see *Known wrong*).
A `Binary` property cannot be the route key (the first `unique` property): the route cannot bind it
from a path segment, so the host **refuses the domain when the app is generated** (`polyc` exits non-zero, naming
the entity and the property). Give the entity a Text, Number, Uuid or Date `unique` property to be its key.

### HTTP actions: what the host loads (`uses http`)

An action route answers from what the host loaded before the module runs, and the host loads **everything the action's
body can read, at any depth**: a link of a link (`customer region Name`), a link read through each item of a collection
(`any lines where product Name is "x"`, `for lines as l invoke l.Look` where `Look` reads its own link), a related
entity's own links reached by `invoke`, the links of an entity-typed parameter, and the reads of a `when` handler whose
subscriber is registered in the same request (a `create in` whose new child starts in a stage the owner watches). So
such an action answers as the simulator does. A link that is empty in the database is empty to the module and the load
skips it. The host always also loads the entity's own collections and single links, whether or not the action reads them,
and the loads may include more than the action reads (an over-load never changes an answer).

Limits. A handler that watches a **stored** entity's later transition does not fire over HTTP (#560), though it does in the
simulator: the host never wires an entity's subscribers after loading it, so an action's `when` effects on another entity
run in a generated app only for subscribers registered in the same request. An action the generator cannot follow is
**refused when the app is generated** (`polyc` exits non-zero, naming the action), never served with less than it reads: an action the generator cannot follow, for example a `when` handler chain that keeps registering subscribers (a handler that creates a child which registers the same owner again). Create routes (the POST of a root) read no stored link.

### HTTP reads: what a `GET` returns (`uses http`)

A `GET` that returns an entity returns it with its declared navigations **loaded, one level**: every collection and every
single link, whether or not anything reads them. That holds on a detail route, for each row of a list route, and for each
item of a nested collection route, so a child created through its parent's action is in the parent's body instead of an
empty array (#594); it is the same navigation state the simulator's `get` names for an instance. Deeper levels are not
loaded: a link reached through one of those navigations answers with its own scalars, and its own navigations are
whatever EF fixed up. A reference that repeats a row already written in the same body is written once and `null` where it
repeats — the host sets `ReferenceHandler.IgnoreCycles` — so a body never loops and the response never fails; on
`/api/orders/{code}/lines` an item's `order` carries the owner and the owner's `lines` reads `null`. An entity with no
navigation keeps its one-expression handler (`async (db) => await db.Books.ToListAsync()`). The shape of a navigation in
the body is still **not stable** (section 14).

## 12. Dual Authoring Path

**Batch** (`apply_dsl`): Write the full domain in `.poly` and apply in one shot.
**Replaces** the entire session domain — not merged incrementally.

`validate_dsl` is the dry run for the same document: it parses, evolves, and analyzes
without changing the session, and returns structured findings (severity, diagnostic code,
element path, message). Iterate `validate_dsl` until a document has zero errors, then
`apply_dsl` it; a failed `apply_dsl` returns the same findings and changes nothing.

**Incremental** (unified tools): `add(kind, payload)` creates one element and
`remove(kind, payload)` deletes one by identity. `kind` is case-sensitive. `payload` is a
JSON object. An unknown kind, a missing field, or an unknown enum value fails closed.

`add` payloads:

- `entity`: `{"name":"Order"}`
- `property`: `{"entityName":"Order","name":"Total","typeName":"Number"}`
- `stage`: `{"entityName":"Order","name":"Active"}`
- `action`: `{"entityName":"Order","name":"Submit"}`
- `stage_action`: `{"entityName":"Order","stageName":"Draft","name":"Submit"}`
- `relationship`: `{"name":"OrderLines","source":"Order","target":"Line","cardinality":"OneToMany"}`.
  `cardinality` is `OneToOne`, `OneToMany` (the default), `ManyToMany` or `ManyToOne`.
  `source` and `target` may also be spelled `sourceEntityName` and `targetEntityName`.
  Optional `"sourceOwnsTarget": true`.
- `constraint`: `{"entityName":"Order","propertyName":"Total","type":"Range","min":0,"max":100}`.
  `type` is `Required`, `Unique`, `Range` (`min`, `max`), `Length` (`min`, `max`) or
  `Pattern` (`{"pattern":"^[a-z]+$"}`).
- `policy`: `{"entityName":"Order","name":"Adult","expression":"Age >= 18"}`. `expression`
  is DSL text, never JSON.
- `value_type`: `{"name":"Money"}`
- `contract`: `{"name":"Stripe","source":"stripe","version":"v1"}`. Optional `sourceKind`:
  `ExternalProvider` (the default) or `InternalDomain`.
- `contract_value_type`: `{"contractName":"Stripe","name":"ChargeRequest"}`
- `contract_endpoint`: `{"contractName":"Stripe","name":"Charge","payloadType":"Number"}`.
  Optional `kind` (`Operation`, the default, or `Event`) and `direction` (`Inbound`, the
  default, or `Outbound`).
- `contract_binding`: `{"name":"ChargeOrder","contractName":"Stripe","endpointName":"Charge","actionName":"Pay","parameter":"amount"}`.
  `parameter` names the action's parameter that the binding fills. It may also be spelled
  `localParameterName`.

`remove` payloads (identity fields only):

- `entity`: `{"name":"Order"}`
- `property`, `stage`, `action`: `{"entityName":"Order","name":"Total"}`
- `stage_action`: `{"entityName":"Order","stageName":"Draft","name":"Submit"}`
- `relationship`: `{"name":"OrderLines"}`. Add `"source":"Order"` when the same name is
  declared on more than one source entity.
- `policy`: `{"entityName":"Order","name":"Adult"}`. Add `"stageName"` to remove a stage
  policy or `"actionName"` to remove an action policy (at most one of the two).
- `constraint`: not supported. Use `apply_dsl`.

A new name is a letter or `_`, then letters, digits, or `_`, and not a DSL keyword. A
property may also be named `Text`, `Number`, or `Boolean` when its type is a primitive.
A name that is a C# keyword (`ref`, `event`, `value`) is legal. The C# export writes
it as `@ref`. Builtin type keywords (`string`, `int`) stay keywords when they are the type.

### Editing the applied document (`poly_define edit`)

`poly_define(workspace, edit: "[...]")` changes the document you last applied instead of
re-sending it. Pass exactly one of `poly` or `edit`. The ops splice your own stored text, so
comments, order and formatting survive; the result runs through the same analysis gate and
revision bump as a whole-document define. A rejected edit changes nothing.

A successful apply — `poly` or `edit` — replaces the lowered module, so the workspace's simulated
instances are cleared even when the edit cannot affect them. The result says so: the summary ends
with `N simulated instance(s) cleared.` and `data.instancesCleared` carries the count (absent when
none were). `apply:false` and a rejected edit clear nothing and say nothing was cleared.

| Op | Fields | Effect |
|---|---|---|
| `add` | `parent`, `dsl` | Inserts `dsl` before the closing `}` of the entity or stage `parent` (`Product`, `Product.Draft`). `parent: ""` adds at top level. |
| `remove` | `path` | Deletes the member (`Product.Total`, `Product.Draft.Publish`) and its line. |
| `replace` | `path`, `dsl` | Replaces the member with `dsl`. A snippet pasted with its own uniform indent (its first line is indented differently from the member) has that base stripped and the member's indent added; a snippet taken from the member's own line start is kept as written; a snippet starting at column 0 is kept as written when its last line is indented at least as far as the member (so a member's own text replaces itself unchanged) and otherwise gets the member's indent added to every line. Lines inside a multi-line string are never re-indented. A trailing `//` comment on the last line stays. |

`uses` lines have their own root path, `uses:<pack>`: `{"op":"remove","path":"uses:storage"}`
removes the line, and `replace` swaps it for another `uses` line. A `uses` path takes no entity
prefix and only a `uses` snippet, and a member path never takes one; an unknown `uses:<pack>`
reaches the declared packs only through a substring match or the near-miss fallback. Removing the
document's last `uses` line is refused
(`EDIT_NOT_ADDRESSABLE`): keep another `uses` line, or redefine the model with `poly` — the
compiled model keeps the packs it already loads, so a document with none would disagree with it.

```json
[{"op":"add","parent":"Product","dsl":"Amount: Decimal"},
 {"op":"remove","path":"Product.Note"}]
```

Top-level `add` placement: `uses` after the domain header; `enum`, `value` and `contract` before
the first entity; `entity` and `bind` after the last declaration. Ops run in order against the
text the earlier ops produced, and all of them apply or none do. `apply:false` splices and
validates but stores nothing; `data.spliced` returns the edited document when it is small.

Edit needs a document that `poly_define(poly)` applied; any other change to the model (the
incremental tools) drops the stored text and `edit` answers `EDIT_NO_SOURCE`. Errors, all
leaving the model unchanged: `EDIT_NO_SOURCE`, `EDIT_STALE` (the model moved meanwhile),
`EDIT_PATH_NOT_FOUND` (lists near misses), `EDIT_NOT_ADDRESSABLE` (stage `entry`/`exit`/`when`,
two members on one line, a `uses` line and a member swapped for each other, or the document's last
`uses` line), `EDIT_BAD_SNIPPET` (the `dsl` of `add`/`replace` is not exactly one declaration — a
member, a top-level declaration or a `uses` line — with balanced braces: an `}` that would close
the parent, an unclosed `{`, or a second declaration is
refused before anything is spliced, and the line and column it reports are relative to your `dsl`,
not the document), `EDIT_NAME_COLLISION`, `EDIT_UNKNOWN_OP`, `EDIT_BAD_JSON`,
`EDIT_ARGUMENT_CONFLICT`. Rename is not an op yet: renaming misses references, so redefine
with `poly`. Comments are invisible to the tokenizer: `remove` deletes a member's own trailing
comment but can orphan a comment on the line above it, and `replace` can leave a trailing
comment describing the old member.

**Golden workflow:** `poly_guide` → write `.poly` → `poly_define(apply:false)` →
`poly_define` → `poly_check` → `poly_export(format:"dsl")` → `poly_simulate`.

## 13. Example (Round-Trip Safe)

<!-- poly-verify: valid -->
```poly
domain Orders

Customer: entity {
  Name: Text
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

## 14. Stability in this release

What an author or a client may rely on in this release, what may still change, and what is known to be wrong. A
"known wrong" item says what a user sees, on which host, and the row or issue that removes it.

### Stable

- **The DSL** as this guide documents it.
- **The JSON body of an entity**: its declared scalars (camel-cased member names) and, when the entity has stages,
  `currentStage`. A `Binary` member is base64, System.Text.Json's own `byte[]` encoding (#905).
- **The HTTP failure shape**: a failure the domain raises is ProblemDetails whose `title` is the failure kind (see
  *HTTP failures*, section 11). The statuses are `400` for `Required`, `Range`, `Length`, `Pattern`, `Enum`,
  `Arithmetic`, `Argument` and `Constraint`; `409` for `Stage`, `Link`, `Unique` and `Conflict` (a save that lost a
  concurrent update is `Conflict`; two concurrent creates of one key give one `201` and `409 Unique` for the rest, from
  the unique index); `422` for `Policy`; `500` for `Cascade`; and `404` with title `Not Found` for an unknown id. A body
  the host's JSON binder refuses (a wrong JSON type, an unknown enum string, a JSON `null` for an action argument) is
  a `400` with an empty body, not ProblemDetails (see *Known wrong*).

### Not stable

- **Navigations in a JSON body**: whether they appear, and their shape, may change.
- **The C# surface**: C# text read or written by the C# reader and writer, and trees built through the API.
- **Generated C# member names** beyond those this guide documents.
- **MCP tool output text.**

### Databases

SQLite is the supported store. A generated SQLite app embeds an id for its schema and stamps a database it creates
with it (`PRAGMA user_version`). Pointed at a file made by a different version of the model, it exits at startup, before
it listens, with one line that names the file. A file that has tables and no id (made by an app generated before this
check, or by another tool) is refused the same way: nothing can vouch for it, and every SQLite database made by an
earlier release is such a file. The id covers tables, columns and their types and nullability, the stage column of an
entity with stages, keys, indexes, relationships and check constraints. A change to an action body, a policy, a comment,
a route or a `key(...)` designation does not change it, unless the change decides whether a `range` is proven: a CHECK
constraint is emitted only for a proven range, so adding or removing that CHECK changes the id. There are no migrations:
delete the file, or run the app from another directory (the path is fixed in the generated host: `<domain>.db`,
relative to the working directory). SQL Server and in-memory hosts have no startup check.

What the id does not cover, and what that costs:

- **Enum members and stage names** are stored as integers by declaration order. Inserting, reordering or removing a
  member or a stage keeps the id and silently re-labels the stored rows (renaming one, or adding one at the end, is
  harmless). This is a known wrong item too.
- **Reordering declarations** (properties, entities, links) changes the id although the schema is the same: the file is
  refused, and deleting it is the only way on.
- **A later release** whose generator prints the model differently refuses every existing database; the first line of
  the id's input (`poly-schema-id/1`) is the version of that input.
- **Two instances started at once on a fresh file, or a crash between creating the tables and writing the id,** leave a
  file the next start refuses: delete it.

### New in this release, where you will meet it

- A bare to-one link is required and permanent; `optional` opts out (section 4, *A to-one navigation is required*). Over HTTP the create body carries it as `<Link>Id`.
- `null` is always the absence of a value, and an empty string is a value (section 3, *`null` and optional values*).
- A literal written `7.0` is a whole number, so `7.0 / 2` is 3 (section 8, *Integer division*).
- `==` and `!=` on a value type: see the `==` item under *Known wrong*.

### Known wrong

- **An absent value assigned into an `optional` Number** (simulator): the printed module stores null, the simulator
  stores `0` when the write sits inside a branch, so `assign Seen to advisor Score` on an unlinked hop disagrees there;
  a `Text` target stores absent on both. The rule itself is in *an unlinked optional navigation reads as absent*
  (section 10). Not yet removed: the simulator's optional value-type slot has to carry the assigned null.
- **`==` on a value type** (simulator): it compares the JSON text of an argument payload, so equal values with their
  fields in another order, or `2` against `2.0` in a `Decimal` field, are unequal there; the generated C# is right.
  Removed by X-06.
- **`when` handlers over HTTP** (generated app): a handler fires only when its subscriber was registered in the same
  request. A handler that watches a stored entity's later transition does not fire, though it does in the simulator. It
  is the first item of the next release (#560). The shipped actions it affects are crm `Opportunity.Win` and
  `Ticket.Close` (to `Account`), hotel `ReleaseSection` and `ReleaseHall` (to `Guest`), university `Enrollment.Drop`,
  `Enrollment.Complete` and `Enrollment.Confirm`, grep `Line` Marked and `Source` Scanned, and the library loans
  Returned.
- **The library demo's `Overdue` stage** has no transition into it (DMSS007), so `when loans Overdue` cannot fire on any
  host. This is a defect of the demo document, not of a host; the document's author removes it.
- **No `id` in a `GET` for an entity without a natural key** (generated app): such an entity has no `id` in its JSON
  body, so a list names no value to `GET` by; its create answers `201` with a `Location` that does name the row (#589),
  and publishing the `id` as a body member is P-12.
- **An action called with `{}`, or with a `null` Number or Boolean argument** (simulator and HTTP): it stores `0` or
  `false`. Over HTTP an explicit JSON `null` is a `400` with an empty body. Removed by X-05.
- **Overloads, `M(decimal?)` declared before `M(long?)`** (C# surface only): an `int` argument binds `decimal?` where C#
  binds `long?`. Removed by #847.
- **A natural key containing `/`** (HTTP): a root create answers `400` with a message instead of storing a row whose
  `Location` no route can read back, because ASP.NET leaves `%2F` encoded in a route value. That 400 is the permanent
  shape for a root create. Every other key is `Uri.EscapeDataString`-escaped in its `Location` and reads back. An entity
  created inside a parent action, or a key set by an action `assign`, still takes the module's own semantics: the row is
  stored and its detail route cannot read a `/` key back (removed by F-13).
- **Optional and defaulted properties in a create body** (HTTP): they are ignored, because the create DTO carries the
  required scalars only; the simulator accepts them (P-11). A wrong JSON type, or an unknown enum string for a required
  enum, answers `400` with an empty body (P-14).
- **Names**: an action named `Add` on an entity that has a `many` link and a `unique` property (#934) fails to
  build. A domain named like a C# keyword is refused, and a domain named `DateTime`
  that declares a `DateTime` property does not compile (CS0118) (E-10).
- **`assign Qty to Dec * 2`** (a Number target, a Decimal operand): analysis accepts it and the generated C# does not
  compile (T-07).
- **An entity with no required scalar** (generated app): the host does not compile (`ItemDto` could not be found), for
  an entity of only optional properties and for one of only defaulted properties (P-11; #588, #641).
- **An entity whose required link targets an entity with no natural key** (generated app) has no `POST` route, so it
  cannot be created over HTTP: saas `Account`, whose required `subscription` targets an entity with a shadow `id`, is
  one. The same holds when a required scalar of the entity is named `<Link>Id`, which clashes with the link's body
  member (P-11, P-12).
- **An owned single part with no back-navigation** (`hq: owned Address optional`) is orphaned when its owner is deleted;
  owned parts reached by a collection or a back-navigation cascade. The behaviour is unchanged in this release (L-15).
- **An owned part whose type declares no navigation back** (`orders: many owned Order` with no `Order.customer` on the
  target): nothing records who owns it, so a second owner's link or create is accepted on both hosts (#585). Declare the
  navigation back and the part names its owner, which turns a second owner into a `Link` failure.
- **Only SQLite** is checked at startup, and only SQLite was probed for delete behaviour (`OnDelete(Restrict)` on a
  required link, cascade on owned parts).
- **An action the generator cannot follow**, such as a `when` handler chain that keeps registering subscribers, is
  refused when the app is generated, not served (#560).
- **Enum members and stage names** are stored as integers by declaration order: inserting, reordering or removing one
  re-labels stored rows with no error (see *Databases*). No check exists in this release, and no row removes it: there
  are no migrations.
- **MCP `add relationship`** cannot write `optional`, and JSON Schema, SHACL and MCP describe do not show a link's
  requiredness. Removed by L-15.
- **An optional temporal value compared with a different temporal type** (C# surface only): it is refused although C#
  defines the comparison (T-21b).
- **`poly_check`** answers `unknown` for every write it did not verify, and an action parameter cannot carry a
  constraint (F-08), so a parameter written to a bounded property is always `unknown`.

## 15. Simulating a scenario

`poly_simulate` runs an ordered list of steps against the current workspace and returns one trace
entry per step. `steps` is a JSON array of objects; every object carries `op` plus only that op's
own keys. Any other key is a failed step naming the valid ones — the tool never drops a misspelled
key and runs a different scenario.

| `op` | required keys | optional keys |
|------|---------------|---------------|
| `create` | `entity` | `properties` (JSON object of property values), `as` (alias for later steps) |
| `link` | `sourceInstanceId`, `relationship`, `targetInstanceId` | — |
| `unlink` | `sourceInstanceId`, `relationship`, `targetInstanceId` | — |
| `invoke` | `instanceId`, `action` | `args` (JSON object of action parameter values) |
| `evaluate` | `entity`, `policy`, `instanceId` | — |
| `get` | `instanceId` | — |
| `list` | — | `entity` (filter to one entity) |

An `instanceId`, `sourceInstanceId` or `targetInstanceId` takes a 32-hex instance id from an earlier
step, or the `as` alias an earlier `create` gave that instance:

```json
[{"op":"create","entity":"Account","properties":{"Name":"Acme"},"as":"acme"},
 {"op":"create","entity":"Order","properties":{"Total":42},"as":"o"},
 {"op":"invoke","instanceId":"o","action":"Submit"},
 {"op":"get","instanceId":"acme"}]
```

A create's `properties` may name a singular navigation whose value is an instance id or an alias
(`{"account":"acme"}`). Wrong-typed
values fail the step: the property coercions (`Date`, `DateTime`, `Time`, `Duration`, `Guid`,
`Decimal`) and the declared `range`/`length`/`pattern` constraints are the create's own.

A step failure does not fail the call: `ok` is true when the scenario ran, `data.trace` carries
`ok:false` and the message on the failing step, earlier steps stay committed, and `stopOnError`
(default true) ends the run there. `reset: true` clears existing instances first; a successful
`poly_define` clears them too (see section 12).
