# Phase 1a — Frozen Grammar Specification

**Date:** 2026-07-17  
**Status:** Frozen — parser implementation target  
**Source:** [`DOMAIN-DSL-SPEC.md`](../../experiments/DOMAIN-DSL-SPEC.md) (laboratory; this doc is authoritative for Phase 1a)  
**Relationship:** N2 interim — relationships use explicit record form, not property-line DSL

---

## 1. Top-level structure

```
.poly = domain-header { entity-definition }

domain-header = "domain" identifier

entity-definition = identifier ":" "entity" "{" { entity-member } "}"
```

The `domain` header must appear first. No `: kind` suffix — default `service`.

Entity names are globally unique within a `.poly` file.

---

## 2. Entity members

```
entity-member = property-definition
              | stage-definition
              | standalone-action
              | policy-definition
```

### 2.1 Property definitions

```
property-definition = identifier ":" primitive-type [ constraints ]

primitive-type = "Text" | "Number" | "Boolean" | "DateTime" | "Date"

constraints = constraint { constraint }

constraint = "required" | "unique"
           | "range" "(" [ number ] "," [ number ] ")"
           | "length" "(" number [ "," number ] ")"
           | "pattern" "(" string-literal ")"
```

Constraints are order-independent. Named argument form (`range(min: 0, max: 100)`) is accepted by the parser; the canonical printer normalizes to named syntax.

### 2.2 Stage definitions

```
stage-definition = identifier ":" "stage" [ prev-guard ] "{" stage-member-list "}"

stage-member-list = { stage-action | subscription-definition }

prev-guard = "prev" identifier-list
```

`prev` names one or more parent stages for hierarchy (analyzers compute effective actions from parent chain).

### 2.3 Actions

```
stage-action = identifier ":" [ "action" ] [ action-gates ] "{" action-body "}"

standalone-action = identifier ":" "action" [ action-gates ] "{" action-body "}"

action-gates = stage-gate-list policy-gate-list

stage-gate-list = "when" identifier-list

policy-gate-list = "require" identifier-list
                 | "require" "not" identifier-list

action-body = { effect }

identifier-list = identifier { "," identifier }
```

Zero-ceremony `Name: {}` infers no extra gates (action is available while entity is in the declaring stage). Full form `Name: action { }` is equivalent.

### 2.4 Effects (Phase 1a)

```
effect = transition-effect | assign-effect

transition-effect = "transition" "to" identifier

assign-effect = "assign" identifier "to" expression
```

### 2.5 Expressions (for assign RHS and policy bodies)

```
expression = comparison
           | logical-and

comparison = logical-and { ("is" | "is" "not" | ">" | ">=" | "<" | "<=" | "==" | "!=") logical-and }

logical-and = logical-or { "and" logical-or }
            | logical-or { "&&" logical-or }

logical-or = primary { "or" primary }
           | primary { "||" primary }

primary = "(" expression ")"
        | "not" primary
        | "!" primary
        | identifier              // property reference
        | number-literal
        | string-literal
        | "true" | "false"
        | "null"
```

Expression parsing maps directly to existing `DomainExpression` nodes:
- `identifier` → `PropertyAccess(name)`
- `number-literal` → `Literal(value)`
- `string-literal` → `Literal(string)`
- `"not" expr` → `Not(expr)`
- `expr1 "and" expr2` → `And(expr1, expr2)`
- `expr1 "or" expr2` → `Or(expr1, expr2)`
- `expr1 "is" expr2` → `Equal(expr1, expr2)`
- `expr1 "is" "not" expr2` → `NotEqual(expr1, expr2)`
- `expr1 ">" expr2` → `GreaterThan(expr1, expr2)`, etc.
- `"true"` → `Literal(true)`, `"false"` → `Literal(false)`

### 2.6 Policy definitions

```
policy-definition = identifier ":" "policy" "{" expression "}"
```

Named boolean expressions referenced in `require` gates by name.

### 2.7 Stage subscriptions (Phase 1a)

```
subscription-definition = "when" identifier identifier "{" action-body "}"
```

Where:
- First `identifier` = relationship name (resolves on `Domain.Relationships`)
- Second `identifier` = target stage name (on the related entity)
- Quantifier is implicitly `Each` (no `any`/`all` keyword in Phase 1a)

---

## 3. Lexical rules

```
identifier = letter { letter | digit | "_" }

number-literal = digit { digit }

string-literal = "\"" { char } "\""

// Comments
line-comment = "//" { char } newline

// Whitespace and newlines are insignificant (braces delimit blocks).
// The parser does not use indentation for structure.
```

Identifiers are case-sensitive. Primitive type names are capitalized (`Text`, `Number`, etc.).

---

## 4. Parser output

The parser produces `IReadOnlyList<DomainChange>` representing the entire domain model as a sequence of evolution steps. The order is:

1. `SetDomainNameChange` for the domain header
2. `AddPrimitiveTypeChange` for each referenced primitive (Text, Number, Boolean, DateTime, Date)
3. `AddEntityChange` for each entity definition
4. `AddPropertyToEntityChange` for each property on each entity
5. `AddConstraintToPropertyChange` for each constraint on each property
6. `AddRelationshipChange` for each relationship
7. `AddStageChange` for each stage on each entity
8. `AddActionToStageChange` or `AddActionChange` for each action
9. `AddParameterToActionChange` for each action parameter
10. `AddEffectToActionChange` for each effect
11. `AddPolicyToEntityChange` for each entity-level policy
12. `AddPolicyToStageChange` for each stage-level policy
13. `AddPolicyToActionChange` for each action-level stage gate / require
14. `AddStageSubscriptionChange` for each subscription
15. `AddOnEntryEffectToStageChange` / `AddOnExitEffectToStageChange` for entry/exit effects

The parser may instead emit a single batch apply that internally expands to micro-changes — either approach is acceptable as long as the resulting `Domain` is structurally identical.

---

## 5. Unsupported constructs (NOT YET SUPPORTED diagnostics)

| Construct | Diagnostic |
|-----------|------------|
| `actor` keyword | "Actor types are not supported in Phase 1a" |
| `Name: Parent { }` extension | "Entity extension is not supported in Phase 1a" |
| Value types (`Name: value { }`) | "Value types are not supported in Phase 1a" |
| `create` / `create in` effects | "Create effects are not supported in Phase 1a" |
| `when any` / `when all` | "Collection quantifiers are not supported in Phase 1a" |
| `schedule at` | "Schedule effects are not supported in Phase 1a" |
| `for` iteration | "Iteration is not supported in Phase 1a" |
| `parallel` | "Parallel effects are not supported in Phase 1a" |
| `DateTime.Now` / static members | "Static member references are not supported in Phase 1a" |
| `invoke` / `start` | "Cross-entity action calls are not supported in Phase 1a" |
| Functions (`Name() -> Type`) | "Functions are not supported in Phase 1a" |
| `domain Name: kind` | "Domain kinds are not supported in Phase 1a" |
| Collection query (`.all()`, `.any()`, `.count()`) | "Collection queries are not supported in Phase 1a" |
| Match expressions | "Match expressions are not supported in Phase 1a" |

---

## 6. Example

```poly
domain SupplyChain

Product: entity {
  SKU: Text required unique
  Name: Text required
  UnitCost: Number range(0, ) required
  ReorderPoint: Number range(0, 10000)

  Draft: stage {
    Activate: action
      when Draft
      require HasName
    {
      transition to Active
    }
  }

  Active: stage {
    Discontinue: action {
      transition to Discontinued
    }
  }

  Discontinued: stage {
    Archive: action {
      transition to Archived
    }
  }
  Archived: stage {}
}

HasName: policy { Name is not null }
```

---

## 7. Grammar reference (EBNF)

For parser implementors — hand-written scanner + recursive descent:

```
domain     = "domain" id "{" { entity } "}"
entity     = id ":" "entity" "{" { member } "}"
member     = property | stage | action | policy
property   = id ":" primType [ constrs ] nl?
primType   = "Text" | "Number" | "Boolean" | "DateTime" | "Date"
constrs    = constr [ constrs ]
constr     = "required" | "unique" | range | length | pattern
range      = "range" "(" [ num ] "," [ num ] ")"
length     = "length" "(" num [ "," num ] ")"
pattern    = "pattern" "(" str ")"
stage      = id ":" "stage" [ "prev" idlist ] "{" { stgMember } "}"
stgMember  = action | subscription
action     = id ":" [ "action" ] [ gates ] "{" { eff } "}"
gates      = whenClause? requireClause?
whenClause = "when" idlist
requireClause = "require" [ "not" ] idlist
eff        = transEff | assignEff
transEff   = "transition" "to" id
assignEff  = "assign" id "to" expr
subscription = "when" id id "{" { eff } "}"
policy     = id ":" "policy" "{" expr "}"
expr       = ... (standard recursive descent over comparison/and/or/primary)
idlist     = id { "," id }
id         = letter { letter | digit | "_" }
num        = digit { digit }
str        = '"' { char } '"'
```
