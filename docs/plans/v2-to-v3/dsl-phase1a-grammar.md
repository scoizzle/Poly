# Phase 1a — Frozen Grammar Specification

**Date:** 2026-07-17  
**Revised:** 2026-07-17 (N1 primary; N2 legacy accepted input)  
**Status:** Frozen — parser implementation target  
**Source:** [`DOMAIN-DSL-SPEC.md`](../../experiments/DOMAIN-DSL-SPEC.md) (laboratory; this doc is authoritative for Phase 1a)  
**Relationship:** N1 primary — navigation properties inside entity blocks; N2 `relationship` form accepted as legacy input

---

## 1. Top-level structure

```
.poly = domain-header entity-definitions [ legacy-relationships ]

domain-header = "domain" identifier

entity-definitions = { entity-definition }

legacy-relationships = { relationship-definition }
```

The `domain` header must appear first. No `: kind` suffix — default `service`.

Entity names are globally unique within a `.poly` file. Relationships are defined inline as navigation properties inside entity blocks (N1 form). The legacy `relationship` keyword form (N2) is still accepted at both entity scope and top level for backward compatibility.

---

## 2. Entity members

```
entity-member = property-definition
              | stage-definition
              | standalone-action
              | policy-definition
              | nav-property-definition
              | relationship-definition       // N2 legacy
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

Constraints are order-independent. Only positional argument form is supported (`range(0, )` not `range(min: 0)`).

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

action-gates = when-clause? require-clause?

when-clause = "when" identifier-list

require-clause = "require" [ "not" ] identifier-list

action-body = { effect }

identifier-list = identifier { "," identifier }
```

Zero-ceremony `Name: {}` is equivalent to `Name: action { }`.

**Note:** `when` gates on actions are syntactically parsed but **not runtime-enforced** in Phase 1a. The action is accessible from any stage. `require` gates are enforced — they reference entity-level policies by name and the policy expression is evaluated at runtime.

**Require resolution:** `require` names are collected during parsing and resolved after the full entity body has been parsed. This means entity-level policies may appear *after* the actions that reference them (order-independent). A `require` referencing a non-existent policy produces a parse error — no silent always-true fallback.

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

Named boolean expressions referenced in `require` gates by name. Policies are entity-scoped (defined inside an entity block). A `require PolicyName` on an action references a policy defined on the same entity.

**Internal naming conventions:** When gates (`when StageName`) are consumed at parse time but produce no policies. Negated requires (`require not PolicyName`) are stored as `not_PolicyName`. Neither `when_*` nor `not_*` prefixed policies appear in DSL output — the printer reconstructs the original `require not` form.

### 2.7 Stage subscriptions (Phase 1a)

```
subscription-definition = "when" identifier identifier "{" action-body "}"
```

Where:
- First `identifier` = relationship name (resolves on `Domain.Relationships`)
- Second `identifier` = target stage name (on the related entity)
- Quantifier is implicitly `Each` (no `any`/`all` keyword in Phase 1a)

### 2.8 Relationship definitions (N2 legacy form)

```
relationship-definition = "relationship" identifier "from" identifier "to" identifier cardinality

cardinality = "one" | "many"
```

**Legacy:** Accepted at top level or inside entity blocks. The `from` entity is the relationship source; `to` is the target. `one` → `OneToOne`, `many` → `OneToMany` from source to target. Canonical output now uses the N1 form (nav-property-definition) — see §2.9.

### 2.9 Navigation property definitions (N1 form)

```
nav-property-definition = identifier ":" [ cardinality ] [ "owned" ] entity-name

cardinality = "one" | "many"
```

**Primary canonical form for relationships.** Defined inside an entity block. The entity on which the nav line appears is the relationship source.

| Pattern | Cardinality | SourceOwnsTarget |
|---------|-------------|------------------|
| `orders: many Order` | `OneToMany` | `false` |
| `orders: many owned Order` | `OneToMany` | `true` |
| `manager: Employee` | `OneToOne` | `false` |
| `manager: one Employee` | `OneToOne` | `false` |
| `manager: owned Employee` | `OneToOne` | `true` |
| `manager: one owned Employee` | `OneToOne` | `true` |

The relationship **name** is the property name (first identifier). Target entity names are resolved after all entities are parsed — order-independent within the file. A reference to an unknown entity or to a primitive type produces a parse error.

**Design rules:**
- Source side is authoritative — only the entity that owns the nav line emits it on print.
- No reverse-nav lines printed by default (re-parse would create a duplicate edge).
- N2 `relationship` input is still accepted for backward compatibility; it is normalized to N1 on print (export).

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
2. `AddPrimitiveTypeChange` for built-in types (Text, Number, Boolean, DateTime, Date) — emitted once
3. `AddEntityChange` for each entity definition
4. `AddPropertyToEntityChange` for each property
5. `AddConstraintToPropertyChange` for each constraint
6. `AddStageChange` for each stage
7. `AddActionToStageChange` or `AddActionChange` for each action
8. `AddEffectToActionChange` for each effect
9. `AddPolicyToEntityChange` for each entity-level policy
10. `AddPolicyToActionChange` for each `require` gate (resolved after full entity body is parsed — order-independent within an entity). Negated requires (`require not PolicyName`) are stored internally as policies named `not_PolicyName` with a `Not(originalExpr)` wrapper — the printer reconstructs the `require not` form on output.
11. `AddStageSubscriptionChange` for each subscription
12. `AddRelationshipChange` for each relationship (from both N1 nav properties and N2 legacy `relationship` keyword form)

---

## 5. Unsupported constructs (NOT YET SUPPORTED diagnostics)

Using any reserved keyword from the table below produces a `FormatException` with the message "`<keyword>` is not supported in Phase 1a". This includes both entity-level constructs (`actor`, `value`) and effect-level keywords (`create`, `schedule`).

| Construct | Diagnostic |
|-----------|------------|
| `actor` keyword | "Actor types are not supported in Phase 1a" |
| Entity extension (`Name: Parent { }`) | "Entity extension is not supported in Phase 1a" |
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

  HasName: policy { Name is not null }

  Draft: stage {
    Activate: action
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
```

---

## 7. Grammar reference (EBNF)

For parser implementors — hand-written scanner + recursive descent:

```
domain     = "domain" id entity* relationship*
entity     = id ":" "entity" "{" member* "}"
member     = property | stage | action | policy | relationship
property   = id ":" primType constrs? nl?
primType   = "Text" | "Number" | "Boolean" | "DateTime" | "Date"
constrs    = constr constrs?
constr     = "required" | "unique" | range | length | pattern
range      = "range" "(" [ num ] "," [ num ] ")"
length     = "length" "(" num [ "," num ] ")"
pattern    = "pattern" "(" str ")"
stage      = id ":" "stage" [ "prev" idlist ] "{" stgMember* "}"
stgMember  = action | subscription
action     = id ":" [ "action" ] [ gates ] "{" eff* "}"
gates      = whenClause? requireClause?
whenClause = "when" idlist
requireClause = "require" [ "not" ] idlist
eff        = transEff | assignEff
transEff   = "transition" "to" id
assignEff  = "assign" id "to" expr
subscription = "when" id id "{" eff* "}"
policy     = id ":" "policy" "{" expr "}"
relationship = "relationship" id "from" id "to" id card
card       = "one" | "many"
expr       = ... (standard recursive descent over comparison/and/or/primary)
idlist     = id ("," id)*
id         = letter { letter | digit | "_" }
num        = digit { digit }
str        = '"' { char } '"'
```
