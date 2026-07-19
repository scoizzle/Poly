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
action-name ":" "action" "{" effect* "}"
```

Effects in an action body:

| Effect | Syntax |
|--------|--------|
| Stage transition | `transition to StageName` |
| Property assignment | `assign PropertyName to expression` |
| Create entity | `create EntityType { prop: value }` |
| Create in relationship | `create in RelationshipName { prop: value }` |

```poly
PlaceOrder: action {
  assign Status to "processing"
  create in orders { Total: 100 }
  transition to Active
}
```

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

**Rules:**
- `Rel Prop` on `many` relationships is invalid (use `any Rel where …` — Q3′ planned). Cardinality validation is enforced at domain analysis time; the parser accepts the syntax but the analysis pipeline will reject it when relationship metadata is available.
- `Rel exists` on `many` is allowed (non-empty check).
- Cross-entity reads (path-prefix, exists, where) are legal in policies, require, and assign RHS.
- Cross-entity writes (nav path as assign target) are banned.
- **Related policies are authoring-complete** — they parse, apply, and export correctly. Full runtime evaluation (true/false via `evaluate_policy`/`simulate_policy` against linked instances) is a future enhancement. Today the `evaluate_policy` and `simulate_policy` tools evaluate local entity properties only — cross-entity expression evaluation through the VM graph traversal pipeline is not yet connected.

**Not yet shipped** (planned for future phases):
- `any`/`all`/`none`/`count` over collections (Q3′)
- Arithmetic (`+`, `-`, `*`, `/`)
- Date operations
- Owned/nested access (partial: simple `owned Prop` via path-prefix)

### Expression Gaps — IR vs DSL

The following expression capabilities exist in the runtime expression IR (`DomainExpression`)
and lowering pipeline but are **not yet authorable in product DSL**:

| Capability | IR exists | DSL status | Notes |
|------------|-----------|------------|-------|
| Relationship navigation | ✅ | ✅ **shipped** (path-prefix) | `customer Tier`, not `customer.Tier` |
| Existence check | ✅ | ✅ **shipped** (postfix `Rel exists`) | `assignee exists` / `not assignee exists` |
| Scoped filter (`where`) | ✅ | ✅ **shipped** (`rel where and-chain`) | `customer where Status is "Active"` |
| Owned/nested access | ✅ | Pull (same path-prefix approach) | `profile Field is "x"` — not `profile.Field` |
| Collection quantifiers (`any`/`all`/`none`/`count`) | ❌ no IR | Q3′ (planned) | New IR + lowering needed |
| Arithmetic (`+`, `-`, `*`, `/`) | ✅ | Pull (planned) | Date operations also in IR |
| Action parameters | ✅ | Not yet planned | `ParameterAccess` in IR |

**JSON policies** (`add_policy` / `simulate_policy`) support comparison + and/or/not + literal only — **not** path-prefix, `Rel exists`, or `where`. Use DSL for related reads; JSON remains limited to local property comparisons and logical composition.

## 8. Supported Effect Summary

| Effect | Can appear in |
|--------|---------------|
| `transition to Stage` | action, entry, exit |
| `assign Prop to expr` | action, entry, exit |
| `create Type { ... }` | action |
| `create in Rel { ... }` | action |
| `delete` | action, entry, exit (soft-deletes the current instance) |

The following effects exist in the runtime library but have **no DSL syntax** yet:
- **link / unlink**: Connect existing instances. **Product path uses `create in Rel { ... }`** for graph writes instead (or `create` with `RelationshipName`). Link/Unlink remain available through the `DomainInstanceStore` library API for test code.
- **invoke**: Call another action on the same instance. Not yet authorable in DSL.
- **TransitionRelationship**: IR exists but **not executed at runtime** — do not use.

> **Note:** `delete` performs a **soft-delete** — it sets the `IsDeleted` flag on the current instance. Any subsequent `call_action` on a deleted instance is refused. This is not a typed mass-delete.

## 9. Do NOT Use (Unsupported in Phase 1a/1b)

| Construct | Why |
|-----------|-----|
| `actor` | Use `entity` instead |
| `value { }` | Value types not supported |
| `schedule`, `parallel`, `for` | Control flow not supported |
| `invoke` | Not supported in Phase 1a/1b (runtime library only) |
| `relationship Name from A to B` | Use N1 nav properties instead |
| `function` | Functions not supported |
| Event/publish/subscribe | Event model retired |

## 10. Dual Authoring Path

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
