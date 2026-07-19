# DSL Query / Expression Surface (LINQ-inspired subset)

**Date:** 2026-07-18  
**Revised:** 2026-07-18 — **surface direction frozen** (§3.1 + §4.0): subject-first path-prefix, postfix `exists`, `where` without forced parens, anti-dot; **cross-entity reads legal / writes banned**  
**Status:** Active — **parallel to** [`effect-surface-completeness.md`](effect-surface-completeness.md); **before** customer ship confidence  
**Current pick:** **Q0** honesty in guide + matrix (reflect frozen surface as *planned*, not shipped); then **Q1′** implement path-prefix + `Rel exists`  
**Micro-tasks:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
**Related:** DomainExpression IR · `PolyDslParser` expression grammar · JSON policy parser · effect-surface plan · product guide  

**Principle:** Policies and guards are only as useful as the **query language** inside them. Prefer a **small, lowerable, subject-first keyword subset** that maps to existing `DomainExpression` + Syntax AST over a full LINQ / SQL / C# clone. **Cross-entity reads legal; cross-entity writes banned** (§3.1). No host I/O in expressions.

---

## 1. Why this matters for customer ship

Effects move the world; **expressions decide** whether actions run, which branches fire, and what “true” means for business rules.

Today:

| Path | Expression power |
|------|------------------|
| **DSL policies** | Property, literal, compare, `and` / `or` / `not`, `is` / `is not` |
| **DomainExpression IR** | Also: owned access, exists/not-exists, arithmetic, date ops, relationship navigation, parameters |
| **JSON MCP policies** | Comparison + and/or/not + literal (no nav / any / count) |
| **Lowering** | Maps known DE nodes → Syntax AST → VM |

So: **IR > DSL authoring** for queries (same pattern as effects). A customer domain that needs “any open order totals &gt; X” or “related Customer.Status is Active” **cannot** say that honestly in product DSL today—even if we add more effects.

A **LINQ-inspired subset** is a good fit if it stays:

1. **Boolean** for policies/require; **scalar** for assign RHS (local and/or related **reads**); assign **targets** never related (§3.1)  
2. **Deterministic and side-effect free**  
3. **Lowerable** to existing Syntax (or a thin, named extension of DE + lower)  
4. **Honest** in the product guide  

---

## 2. Gap inventory (today)

### 2.1 Product DSL (parser) — what works

```text
Age >= 18
isActive is true and role is "admin"
not Suspended
(Total > 0) or Rush is true
```

Rough grammar: `or` → `and` → `not` → comparison → primary (property | literal | paren).

### 2.2 IR present but weak/absent in DSL

| DomainExpression | DSL today | Typical need |
|------------------|-----------|--------------|
| `RelationshipNavigation` | ❌ | planned: `customer Tier is "VIP"` (path-prefix), not `customer.Tier` |
| `OwnedAccess` | ❌ | planned: path-prefix / `owned where …` |
| `Exists` / `NotExists` | ❌ | planned: `assignee exists` / `not certificate exists` |
| `ParameterAccess` | ❌ | action args in guards/effects |
| Arithmetic (`Add`…) | ❌ | totals, age math |
| `DateOperation` | ❌ | deadlines |
| Collection quantifiers | ❌ **no IR yet** | any/all/none over `many` navs |
| Aggregates | ❌ **no IR yet** | count/sum/min/max over related |

### 2.3 LINQ-shaped features customers will ask for

| Feature | LINQ analogue | Policy example |
|---------|---------------|----------------|
| Filter related | `Where` | any open order |
| Quantify | `Any` / `All` | all line items reserved |
| Aggregate | `Count` / `Sum` | order count &gt; 0, sum totals |
| Project (limited) | `Select` scalar | rarely needed in guards |
| Navigate | subject-first path | `customer Tier is "Gold"` |
| Null/optional | postfix exists | `certificate exists` / `not certificate exists` |

**Select-many pipelines, deferred IQueryable, side-effecting queries:** **out of scope.**

---

## 3. Design rules

1. **Policies stay pure** — no mutation, no CallAction, no I/O in expressions.  
2. **Lower or don’t ship** — every new DE node needs `DomainExpressionLoweringPass` + tests (VM or dual-oracle).  
3. **DSL and JSON parity over time** — MCP `add_policy` JSON should not forever be weaker than DSL (or document the split).  
4. **Collections = relationship `many` (and later owned lists)** — not arbitrary .NET IEnumerable.  
5. **Subject is implicit** — bare props on current entity; related forms are subject-first (`Rel …`), not free-floating queries.  
6. **No full C# LINQ** — no query comprehensions spanning databases; no expression trees from customer C#.  
7. **Ship with guide + goldens** — same honesty bar as effects (E0).  
8. **Sequence with effects** — query surface unblocks *meaningful* guards on create-in / when / delete; coordinate with effect plan but don’t block delete-self on full LINQ.  
9. **Cross-entity reads legal; cross-entity writes banned** — see §3.1.

### 3.1 Cross-entity reads vs writes (product rule)

**Rule:** You may **observe** related/owned data. You may **not** mutate another entity’s fields through `assign` (or any effect that pretends to be assign).

| | Legal | Banned |
|--|--------|--------|
| **Cross-entity read** | Policies, require, constraints; scalar reads as **assign RHS** (value comes from related, write is still local) | — |
| **Cross-entity write** | — | Target of `assign` is another entity/nav/owned path; any “set related field” sugar |

| Part | Product surface | Notes |
|------|-----------------|--------|
| **Assign LHS (target)** | Single property on **this** entity only | Today: `assign Status to …`. **Never** `assign customer Status to …` / `assign assignee Active to …`. |
| **Assign RHS (value)** | Scalar expression — **local and/or related reads** | Literals, local props, path-prefix **scalar** reads (e.g. related Text/Number/Bool prop), later Q2 arithmetic / params. Boolean-only forms (`Rel exists`, `where`, `any`) are not scalar assign values. |
| **Policies / require** | Full observation dialect | Path-prefix, `Rel exists`, `where`, quantifiers — pure reads. |
| **Other graph mutation** | Not via assign | `create` / `create in`, link (E2), invoke on related (E3b) if ever — explicit, not assign. |

```poly
// yes — local write, local value
assign Status to "Open"
assign Total to 0

// yes — local write, cross-entity read (value only)
assign Label to customer Tier
assign Flag to assignee Active

// yes — cross-entity read in policy (no write)
customer Tier is "VIP"
assignee exists
not certificate exists

// no — cross-entity write (target is not this entity)
assign customer Status to "Active"
assign assignee Active to true
```

**Why:** Observation is what makes guards and denormalized local fields useful. Mutation of peers is a different capability (spawn, link, invoke) with different runtime and honesty costs. Do not smuggle peer writes through `assign`.

**Parser / analysis:**

- LHS stays one identifier → current entity property (already true).  
- RHS may use **scalar** related reads once path-prefix ships; reject related **boolean** forms as RHS (`assign X to assignee exists` fail-loud).  
- Fail-loud on multi-token or nav-shaped assign targets.

---

## 4. Proposed product surface (LINQ-inspired subset)

> **Product surface (frozen):** Subject-first, anti-dot. See [§4.0](#40-product-surface--subject-first-anti-dot). Implement Q1′ against this section; do **not** ship `rel.Prop` / prefix `exists rel` / C# method chains.

### 4.0 Product surface — subject-first, anti-dot

#### Why dots are a weak default for this DSL

Today the product expression surface is already **subject-centric and bare-identifier**:

```poly
Age >= 18
isActive is true and role is "admin"
```

There is no `this.Age`. Introducing `customer.Status` or `orders.Total` would:

| Issue | Detail |
|-------|--------|
| **False friend** | Reads like C# / type.member; agents invent host types |
| **Cardinality lie** | `orders.Status` is ill-typed for `many` — collection has no `Status` |
| **Parser ambiguity** | `.` already tokenized; property vs nav vs future methods collide |
| **Two dialects** | Q1 = dots for to-one; Q3 = methods for to-many — agents mix them badly |
| **IR ≠ surface** | `RelationshipNavigation` can lower from *any* surface form; we need not print dots |

Tokenizer already has `Dot` and `Arrow` — that does **not** commit us to shipping either in policies.

#### What “LINQ expression syntax” can mean here

| Style | Example | Dot load | Fit |
|-------|---------|----------|-----|
| **A. C# method chain** | `orders.Any(o => o.Status == "Open")` | Heavy (receiver + members) | Feels .NET; fights anti-dot; needs lambdas + `==` |
| **B. Method-ish, no receiver dots** | `any orders (Status is "Open")` | Low | Keyword quantifiers; subject rebinding inside body |
| **C. Query keywords** | `from o in orders where Status is "Open"` | Low–medium if `o.` required | True “query expression”; heavier grammar |
| **D. Dot nav only (old Q1)** | `customer.Status is "Active"` | High for to-one | Small IR expose; weak for `many`; dead end for Q3 |
| **E. Arrow nav** | `customer->Status is "Active"` | Medium | Distinguishes nav from C#; still scalar-nav dialect |

**Frozen product lean:** keyword **`where` only** (no `has`/`with`); **no forced parentheses** on `where` bodies; **path-prefix** for simple related predicates; **postfix `exists`** with absence **`not Rel exists`**. Still not full C# LINQ.

#### Two ways to talk about related data (both anti-dot)

| Form | When | Example |
|------|------|---------|
| **Path-prefix (no `where`)** | One comparison / one bool prop on a **to-one** (or owned) | `assignee Active` · `customer Tier is "VIP"` |
| **`where` rebind** | Multi-predicate on same related, or **quantifiers** | `customer where Status is "Active" and CreditLimit >= 1000` · `any orders where Status is "Open"` |

Parentheses are **only** normal boolean grouping — never required solely because `where` appeared:

```poly
// good — no parens around where body
customer where Status is "Active" and CreditLimit >= 1000
any orders where Status is "Open" or Status is "Processing"

// good — parens only to group outer vs quantified
Age >= 18 and (any orders where Status is "Open")
```

#### Path-prefix (simple boolean / compare — no `where`)

Idea: a **relationship (or owned) name** is the **subject** of a small postfix/operand phrase. Same left edge for props, compares, and **presence** — no leading `exists`.

```poly
// Ticket subject — related subject first, operand after
assignee Active                          // bool prop on related Agent
customer Tier is "VIP"                   // compare on related Customer
customer Status is "Active"
not assignee Active                      // related bool, negated

assignee exists                          // presence — postfix operand
not certificate exists                   // absence — outer not + postfix exists
```

**Why postfix `exists`:** same shape as `assignee Active` and `customer Tier is "VIP"` — **nav is always on the left**. Prefix `exists assignee` was the odd one out (verb first).

**Negation:** product form is **`not Rel exists`** only — reuses boolean `not`; no `Rel not exists` dialect.

| Form | Role of `Rel` |
|------|----------------|
| `Rel exists` | presence of linked/owned target |
| `not Rel exists` | absence (`NotExists`) |
| `Rel BoolProp` | bool field on to-one related |
| `Rel Prop op value` | compare on to-one related |
| `Rel where …` | rebind multi-pred / quantifier body |

**`many` + postfix exists:** `orders exists` can mean “at least one link” (≈ `count orders >= 1` / non-empty). That is coherent and **does not** allow `orders Status is "Open"` without `any`/`all`. Still fail-loud on bare many + property.

**Repeat the nav name** when combining two simple path-prefixes (no rebind yet):

```poly
customer Status is "Active" and customer CreditLimit >= 1000
assignee exists and assignee Active
assignee Active and customer Tier is "VIP"
```

**Prefer `where` once** when several predicates share the same related subject:

```poly
customer where Status is "Active" and CreditLimit >= 1000
// CreditLimit binds to customer (rebind), not outer Ticket
```

Rough parse intent (not final BNF):

```text
// inside policy / require / constraint expression context:
related_simple :=
    RelName 'exists'                     // → Exists(nav target)
  | RelName BoolProperty                 // → nav + property (bool)
  | RelName Property CompareOp Literal   // → nav + comparison
  | RelName Property is [not] Literal
// absence: outer not_expr — 'not' related_simple  →  not Rel exists

// not: many-rel property without quantifier
// bad: orders Status is "Open"     // use any orders where …
// ok:  orders exists               // non-empty many (if we allow)
```

#### Subject rebinding with `where`

After `Rel where`, bare names bind to **related** until the end of that quantified/scoped primary’s body (see precedence note).

```poly
// to-one multi-predicate
customer where Status is "Active" and CreditLimit >= 1000

// to-many quantifiers (Q3′)
any orders where Status is "Open" and Total > 0
all lineItems where Reserved is true
none notes where NeedsFollowUp is true
count orders where Total > 100 >= 1
```

Optional range variable: **pull only** if a golden needs outer props inside a related body.

#### Where does path-prefix / `where` apply?

| Context | Cross-entity **reads**? | Notes |
|---------|-------------------------|--------|
| **`policy { … }`** | **Yes** — full boolean observation | Path-prefix, `exists`, `where`, quantifiers |
| **`require` / constraints** | **Yes** when boolean | Same |
| **`assign` RHS** | **Yes** — **scalar** related reads only | e.g. `customer Tier`; not `assignee exists` as value |
| **`assign` LHS** | **No** — write target is this entity only | §3.1 |
| **Effect conditions** | Later; reads OK if boolean | |

**Rationale:** **reads across the graph are legal and useful; writes across the graph are banned** (§3.1).

#### Unified surface sketch (anti-dot, refined)

| Need | Surface | Maps toward |
|------|---------|-------------|
| Local props | `Age >= 18` | `PropertyAccess` (today) |
| Related **present** | `customer exists` (postfix) | `Exists` |
| Related **absent** | `not certificate exists` | `NotExists` |
| **To-one simple** | `assignee Active` · `customer Tier is "VIP"` | Nav + prop/compare (**no `where`**) |
| **To-one multi** | `customer where Status is "Active" and CreditLimit >= 1000` | Rebind + body (**no forced parens**) |
| **To-many** ∃ | `any orders where Status is "Open"` | Quantifier IR |
| **To-many** ∀ / none / count | `all` / `none` / `count` … `where` … | Quantifier IR |
| Params | `@amount` / `param amount` | `ParameterAccess` |
| Arithmetic | `Total + Tax > 100` | Q2 |

**Explicit non-syntax for product v1:**

- `rel.Prop`, `rel->Prop`, `o.Prop`  
- Forced `where ( … )` solely for the keyword  
- `orders Status is "Open"` without `any`/`all`/`count`  
- Cross-entity **write** via assign (related LHS)  
- Full `from…join…select` / `.Where().Any()`

#### Precedence (so optional parens stay optional)

Treat quantified / scoped forms as **primaries** (like a parenthesized subexpression):

```text
or_expr  := and_expr ('or' and_expr)*
and_expr := not_expr ('and' not_expr)*
not_expr := 'not' not_expr | comparison
comparison := primary (CompareOp primary)? | related_simple | …
primary  :=
    '(' or_expr ')'
  | 'any'|'all'|'none' RelName 'where' or_expr     // body rebound
  | RelName 'where' or_expr                        // to-one scope
  | RelName 'exists'                               // postfix presence (related_simple)
  | related_simple                                 // path-prefix prop/compare
  | property | literal | …
```

**Greediness rule (proposed):** the `where` body is a full `or_expr`, but because the whole `any R where …` / `R where …` is a **primary**, outer `and`/`or` bind **outside** only if the parser finishes the body first.

Practical authoring:

```poly
// body includes both related compares (rebind)
any orders where Status is "Open" and Total > 0

// outer and quantified: parens recommended for clarity when mixing
Priority is "High" and (any notes where Internal is true)

// or put the simple local first and keep quantifier as last primary without extra and-tail
Priority is "High" and any notes where Internal is true
// → And(Priority High, Any(notes, Internal))  if where body is only "Internal is true"
```

Implementers: lock body extent in Q1.1 with tests (especially `and` after `where`). Prefer **where body = or_expr that does not cross `)` of an enclosing group**; document that **related `and` tails after `where` belong to the body** when the quantifier/scope is the right operand of an outer `and` (standard Pratt: primary consumes its where-or_expr fully — define where-or_expr as and-chain of comparisons only if needed to reduce ambiguity).

**Simpler implementable rule (recommended for v1):**

- `where` body = **`and_expr` of comparisons** (not top-level `or`), OR  
- `or` inside body requires parens: `any orders where (Status is "Open" or Status is "Processing")`

That keeps “no forced parens for the common and-chain” while making `or` in body opt-in parens. **Decide in Q1.1.**

#### How this reshapes Q1 vs Q3

| Old plan | Refined lean |
|----------|----------------|
| **Q1** = `rel.Prop` + exists | **Q1′** = `exists` + **path-prefix** (`customer Tier is "VIP"`, `assignee Active`) + optional to-one `rel where …` |
| **Q3** = method `any` | **Q3′** = `any`/`all`/`none`/`count` **rel where …** (same `where`, no dots) |

**Thin vertical samples (Ticket):**

```poly
HasAssignee: policy {
  assignee exists
}

AssignedToActiveAgent: policy {
  assignee Active
}

VipCustomer: policy {
  customer Tier is "VIP"
}

ActiveVip: policy {
  customer where Status is "Active" and Tier is "VIP"
}

NeedsHuman: policy {
  Priority is "High" and not assignee exists
}
```

**“Has any open order”** remains **Q3′**.

#### Parser / lower notes (implementers)

1. Path-prefix props/compares only for **to-one / owned**; `Rel exists` allowed for to-one, owned, and optionally **many** (= non-empty). Fail-loud on `many` + property without quantifier.  
2. `where` body — rebound subject; **no required paren pair**.  
3. Lower path-prefix / to-one where via `RelationshipNavigation` / `OwnedAccess` + compare; prefer no new VM ops.  
4. `any R where P` needs store enumeration — not scalar Member lower.  
5. Printer: path-prefix and `where` keyword forms; never dots.  
6. Boolean observation in policy/require/constraint; **scalar** related reads OK on assign RHS; assign **LHS** never related (§3.1).  
7. JSON: document split or add parallel shapes later.

#### Locked for Q1′ (do not re-litigate in implementer tasks)

| Decision | Choice |
|----------|--------|
| Primary dialect | **B1+path** — path-prefix + postfix `exists` + to-one `where` |
| Dots / arrows | **No** product `rel.Prop` / `rel->Prop` |
| Exists | **`Rel exists`** / **`not Rel exists`** |
| Where parens | **Not required** for body; optional for grouping / `or` |
| Cross-entity | **Reads legal; writes banned** (§3.1) |
| Q3′ quantifiers | `any`/`all`/`none`/`count` **Rel where …** (later) |

**Still open (Q1.1 may freeze):** where body = `and_expr` vs full `or_expr`; many-side `orders exists` = non-empty?

---

### Q-core (boolean / scalar on subject) — mostly **DSL expose IR**

| Syntax (product) | Maps to | Priority |
|------------------|---------|----------|
| `Prop` / compare / and / or / not | existing | ✅ done |
| `Rel Prop` / `Rel Prop op value` (path-prefix) | `RelationshipNavigation` / `OwnedAccess` | **Q1′** |
| `Rel exists` / `not Rel exists` | `Exists` / `NotExists` | **Q1′** |
| `Rel where and-chain…` (to-one multi-pred) | rebind + nav IR | **Q1′** |
| Scalar path-prefix on **assign RHS** | same nav IR; write stays local | **Q1′** |
| `@Name` / `param Name` | `ParameterAccess` | **Q1b** |
| `A + B`, `*`, `-`, `/` | arithmetic DE | **Q2** |
| date ops (thin) | `DateOperation` | **Q2** |

### Q-linq (collection quantifiers / aggregates) — **new IR + lower**

| Syntax (product) | Meaning | Priority |
|------------------|---------|----------|
| `any rel where …` | ∃ related matching pred | **Q3′** |
| `all rel where …` | ∀ related matching pred | **Q3′** |
| `none rel where …` | ¬any | **Q3′** (sugar) |
| `count rel` / `count rel where …` | cardinality | **Q3′** |
| `sum …` | aggregates | **Q4** pull |

**Keyword / query-shaped preferred** over C# method chains and over full `from…select` comprehensions. **No product dependence on `.` for navigation.**

### Non-goals (customer v1)

- Dot or arrow **navigation dialect** (`rel.Prop`, `rel->Prop`)  
- Prefix `exists rel` / `rel not exists`  
- C# method chains (`.Where().Any()`) and `o => o.Prop`  
- Full `from…join…group by…orderby…select`  
- **Cross-entity writes** via assign or silent peer mutation (§3.1)  
- Cross-domain queries / second root  
- Full `IQueryable` / EF translation as product claim  
- Loading strategies / N+1 productization in the language  

---

### Expression Parity Matrix (DE node × DSL × JSON × lower × VM)

Legend: **✅** shipped · **🟡** partial · **❌** missing · **🚫** non-goal · **Q1′** / **Q3′** planned

| DE node | DSL today | Planned DSL | JSON (`add_policy`) | Lower → Syntax AST | VM eval | Notes |
|---------|-----------|-------------|---------------------|--------------------|---------|-------|
| `PropertyAccess` | ✅ property name | — | ✅ `{"property":"Name"}` | ✅ `Member(propertyName)` | ✅ | Core read |
| `Comparison` | ✅ `==`, `!=`, `>`, `>=`, `<`, `<=`, `is`, `is not` | — | ✅ via op field | ✅ `Equal`/`NotEqual`/less/greater nodes | ✅ | 6 kinds |
| `And` | ✅ `and` | — | ✅ `{"and":[...]}` | ✅ `And` | ✅ | n-ary, folded left |
| `Or` | ✅ `or` | — | ✅ `{"or":[...]}` | ✅ `Or` | ✅ | n-ary, folded left |
| `Not` | ✅ `not` prefix | — | ✅ `{"not":{...}}` | ✅ `Not` | ✅ | Unary |
| `Literal` | ✅ numbers, strings, `true`, `false`, `null` | — | ✅ `{"literal":V}` | ✅ `Constant` | ✅ | |
| `RelationshipNavigation` | ❌ | **Q1′**: `Rel Prop`, `rel exists`, `rel where …` | ❌ | ✅ exists | ✅ via store | Subject-first, anti-dot |
| `OwnedAccess` | ❌ | **Q1′**: `owned.Prop` via path-prefix | ❌ | ✅ exists | ✅ | |
| `Exists` | ❌ | **Q1′**: `Rel exists` (postfix) | ❌ | ✅ `Exists` | ✅ | |
| `NotExists` | ❌ | **Q1′**: `not Rel exists` | ❌ | ✅ `NotExists` | ✅ | |
| `ParameterAccess` | ❌ | **Q1b**: `@param` or `param Name` | ❌ | ❌ (needs type info) | ❌ (needs args) | Action params |
| `Add` / `Subtract` / `Multiply` / `Divide` | ❌ | **Q2**: `A + B` etc. | ❌ | ✅ exists | ✅ | Arithmetic |
| `DateOperation` | ❌ | **Q2**: date math | ❌ | ✅ exists | ✅ | AddDays/DiffDays |
| Quantifiers (`any`/`all`/`none`/`count`) | ❌ | **Q3′**: `any Rel where …` | ❌ | ❌ new IR | ❌ new | Store enumeration |
| Aggregates (`sum`/`avg`/`min`/`max`) | ❌ | **Q4** pull | ❌ | ❌ new IR | ❌ new | |

**Implementation status:** IR and lowering exist for DE nodes marked "✅ exists". DSL and JSON are the primary authoring gaps. VM evaluation is the canonical runtime path.

---

### Decision Log: Quantifier Keyword Forms (Q3′)

**Date:** 2026-07-18  
**Status:** **Confirmed** — product forms are keyword-based, not method-chain-based.

| Form | Example | Status |
|------|---------|--------|
| `any` | `any orders where Status is "Open"` | ✅ Confirmed |
| `all` | `all lineItems where Reserved is true` | ✅ Confirmed |
| `none` | `none notes where NeedsFollowUp is true` | ✅ Confirmed |
| `count` | `count orders where Total > 0 >= 1` | ✅ Confirmed |

**Token status:** `any`, `all`, `none`, `count`, `exists`, `where` are **not** current tokenizer keywords — they parse as `Identifier`. This is fine: they can be added as contextual keywords in the tokenizer (`WordToKind`) when Q3′ implementation begins. No collisions with existing grammar.

**Rejected alternatives:**
- C# method chains: `orders.Any(o => o.Status == "Open")` — rejected (anti-dot principle)
- Suffix methods: `orders.any(Status is "Open")` — rejected (dot ambiguity)
- Prefix without `where`: `any orders Status is "Open"` — rejected (parsing ambiguity; `where` is the clear scope delimiter)

**Next:** Q3′ implementation not started until Q1′ ships.

---

### Customer Must-Have Expression List (Product Spellings)

Mapped against two dogfood domains: **Ticket** (support) and **Order/Customer** (e-commerce).

| # | English | Product form (or status) | Slice | Domain |
|---|---------|--------------------------|-------|--------|
| 1 | "Is a high-value order" | `Total > 1000` | ✅ today | Order |
| 2 | "Order has a positive total" | `Total > 0` | ✅ today | Order |
| 3 | "Customer is active" | `Status is "active"` | ✅ today | Customer |
| 4 | "Has an assignee" | `assignee exists` | **Q1′** | Ticket |
| 5 | "Has no certificate on file" | `not certificate exists` | **Q1′** | Ticket |
| 6 | "Assigned to an active agent" | `assignee Active` | **Q1′** | Ticket |
| 7 | "VIP customer" | `customer Tier is "VIP"` | **Q1′** | Ticket |
| 8 | "Active VIP with sufficient credit" | `customer where Status is "Active" and CreditLimit >= 1000` | **Q1′** | Ticket |
| 9 | "Has any open order" | `any orders where Status is "Open"` | **Q3′** | Customer |
| 10 | "All line items are reserved" | `all lineItems where Reserved is true` | **Q3′** | Order |
| 11 | "Set customer's status to Active" | ❌ **Banned** — cross-entity write. Use `create in Rel { ... }` or assign local field only | 🚫 | Customer |
| 12 | "Assign to the first available agent" | `assign assignee to ???` — needs link/invoke path (E2/E3) | **pull** | Ticket |
| 13 | "Total with tax" | `Total + Tax > 1000` — arithmetic | **Q2** | Order |
| 14 | "Overdue by more than 30 days" | date operation | **Q2** | Ticket |

---

## 4.5 Formal Product Spec (Q1′ — Subject-First Related Reads)

This section is the **implementable spec** for Q1′. It extends the shipped expression grammar (§4.0) with subject-first path-prefix, postfix `Rel exists`, and to-one `rel where …`. No changes to the shipped grammar for local-only expressions.

### 4.5.0 Open Decisions (frozen in this spec)

| Topic | Decision | Rationale |
|-------|----------|-----------|
| `where` body extent | **`and_expr` of comparisons**; `or` in body requires `(…)` | Avoids ambiguity; keeps "no forced parens for common and-chain" while making `or` opt-in |
| `orders exists` (many) | **Allowed** as non-empty check (≥1 link) | Coherent semantics; does not gate on Q3′. `many` + property without quantifier still fail-loud |
| Missing to-one on path-prefix | **`not exists` == false** (soft miss) — the nav is absent, so the compare/boolean is false | Avoids parse failures for runtime-valid queries; matches `Exists` semantics |
| Sticky vs repeat nav | **Repeat** for path-prefix; `where` rebinds | `customer A and customer B` is explicit; `where` body rebinds once |

### 4.5.1 Grammar (BNF extension to shipped expression grammar)

```text
(* Extends the existing expression grammar with related-entity forms.
   Precedence: or < and < not < comparison < primary
   "related_simple" and "where_scoped" replace some primary alternatives. *)

related_simple ::=
    RelName 'exists'                                         (* → Exists *)
  | RelName BoolPropertyName                                 (* → RelationshipNavigation + PropertyAccess (bool) *)
  | RelName PropertyName CompareOp value                     (* → RelationshipNavigation + Comparison *)
  | RelName PropertyName 'is' ['not'] value                  (* → RelationshipNavigation + Equal/NotEqual *)

where_scoped ::=
    RelName 'where' and_expr                                 (* to-one: → RelationshipNavigation + rebind body *)

(* many + property without quantifier is banned — parse error *)
```

### 4.5.2 Mapping to DomainExpression

| Product form | DE construction | Valid context |
|-------------|-----------------|---------------|
| `assignee exists` | `Exists(RelationshipNavigation("assignee", PropertyAccess("")))` | Policy, require, constraint |
| `not certificate exists` | `Not(Exists(RelationshipNavigation("certificate", …)))` | Policy, require, constraint |
| `assignee Active` | `RelationshipNavigation("assignee", PropertyAccess("Active"))` | Policy, require, constraint; **scalar** on assign RHS |
| `customer Tier is "VIP"` | `RelationshipNavigation("customer", Comparison(PropertyAccess("Tier"), Eq, Literal("VIP")))` | Same; **scalar** RHS allowed |
| `customer where Status is "Active" and CreditLimit >= 1000` | `RelationshipNavigation("customer", And(Comparison(PropertyAccess("Status"), Eq, Literal("Active")), Comparison(PropertyAccess("CreditLimit"), Gte, Literal(1000L))))` | Policy/require only |

### 4.5.3 Validation rules

| Rule | Enforcement |
|------|-------------|
| `Rel.BoolProp` on `many` | Parse error — use `any Rel where …` (Q3′) |
| `Rel.Prop op value` on `many` | Parse error — use `any Rel where …` (Q3′) |
| `Rel exists` on `many` | **Allowed** — non-empty check |
| Assign LHS with nav path | Parse error — cross-entity write banned |
| Assign RHS with `Rel exists` | Parse error — boolean not a scalar value |
| Missing relation name | Evolution-time domain analysis error |

### 4.5.4 Implementation pointers for Q1.2–Q1.6

| Task | Focus |
|------|-------|
| **Q1.2** | Parse path-prefix (`Rel Prop`, `Rel Prop op value`). Printer outputs same form. Lower via `RelationshipNavigation`. |
| **Q1.3** | Parse `Rel exists` / `not Rel exists`. Printer outputs postfix `exists`. Lower via `Exists`/`NotExists`. |
| **Q1.3b** | Parse `Rel where and_expr`. Rebind subject for body. Lower via `RelationshipNavigation` wrapping body. |
| **Q1.4** | Goldens: policies evaluate, assign RHS scalar read works, related LHS rejected, many+property error. |
| **Q1.5** | JSON policy parity: document that JSON lacks nav/exists — prefer split documentation. |
| **Q1.6** | Guide examples using subject-first forms only. Add §3.1 cross-entity rule table. |

### 4.5.5 Decision Log

| Date | ID | Decision |
|------|----|----------|
| 2026-07-18 | Q1.1 | `where` body = `and_expr`; `or` inside body needs parens |
| 2026-07-18 | Q1.1 | `orders exists` allowed for many (non-empty) |
| 2026-07-18 | Q1.1 | Missing to-one on path-prefix = `not exists` = false (soft miss) |
| 2026-07-18 | Q1.1 | Repeat nav name for multiple path-prefixes; `where` rebinds |  

---

## 5. Slices

### Q0 — Freeze + honesty (**small**)

- [x] **Q0.1** Document current DSL expression grammar in product guide (and/or/not/compare only).  
- [x] **Q0.2** Document IR-only gaps + **planned** subject-first surface (not shipped yet).  
- [x] **Q0.3** Matrix: DE node × DSL × JSON × lower × VM.  
- [x] **Q0.4** Confirm quantifier keywords for Q3′ (`any`/`all`/`none`/`count` + `where` — already frozen lean).  
- [x] **Q0.5** Customer “must have” list using **product** spellings (`Rel exists`, path-prefix, etc.).

**Exit:** Guide doesn’t overclaim shipped surface; planned dialect is named; implementers know Q1′ vs Q3′.

---

### Q1′ — Path-prefix + postfix exists + to-one `where` (**medium**)

**Goal:** Cross-entity **reads** for policies (and scalar assign RHS) **without** dots, without collection quantifier IR, **without** cross-entity writes.  
**Direction:** §3.1 + §4.0 frozen (B1+path).

- [ ] **Q1.1** Spec freeze: BNF/examples for path-prefix, `Rel exists` / `not Rel exists`, to-one `Rel where`, assign LHS/RHS rules; remaining open bits (where-body and/or; many `exists`).  
- [ ] **Q1.2** Parse/print + lower **path-prefix** (`Rel Prop`, `Rel Prop op value`) for policy + scalar assign RHS.  
- [ ] **Q1.3** Parse/print + lower **`Rel exists`** / **`not Rel exists`**.  
- [ ] **Q1.3b** Parse/print + lower to-one **`Rel where` and-chain** (rebind).  
- [ ] **Q1.4** Goldens: path-prefix policy, exists, optional where, scalar assign RHS; refuse related assign **LHS**.  
- [ ] **Q1.5** JSON policy shapes for same (or document DSL-only split).  
- [ ] **Q1.6** Guide examples (subject-first only; state cross-entity read/write rule).

**Exit:** `assignee exists`, `customer Tier is "VIP"`, `not certificate exists` authorable; `assign customer X to …` rejected.  
**Not exit:** `any orders where …` — that is Q3′.

**Risk:** Runtime bag model must resolve navigation (instances + store). Align with RT instance graph.

---

### Q1b — Parameters in expressions (**small–medium**, after action params story)

- [ ] **Q1b.1** Action parameters in DSL (if not already product-complete).  
- [ ] **Q1b.2** `ParameterAccess` in guards/effects.  
- [ ] **Q1b.3** Golden: require amount &gt; param threshold.

---

### Q2 — Arithmetic + thin date (**small–medium**)

- [ ] **Q2.1** Parse/print arithmetic with precedence.  
- [ ] **Q2.2** Lower existing DE arithmetic.  
- [ ] **Q2.3** Thin date ops only with real domain need.  
- [ ] **Q2.4** Goldens + guide.

---

### Q3′ — Collection quantifiers / count (**medium–large**)

**Goal:** Collection predicates over `many` relationships — **same keyword family as Q1′**, not a new dot/method dialect.

- [ ] **Q3.1** New DE nodes (e.g. `Any`, `All`, `Count`) or lower sugar to existing Syntax.  
- [ ] **Q3.2** Subject rebind: `any orders where Total > 0` — body uses bare props on related. Optional `as x` later.  
- [ ] **Q3.3** Runtime: enumerate linked instances via store (needs RT session for MCP goldens).  
- [ ] **Q3.4** Empty collection semantics (any → false, all → true).  
- [ ] **Q3.5** DSL parse/print + guide (keyword forms).  
- [ ] **Q3.6** Goldens: any/all/count with 0, 1, N related instances.  
- [ ] **Q3.7** Analysis: unknown rel, wrong cardinality (`any` on `one`? → suggest `where` / exists).

**Exit:** “Has any open order” / “all lines reserved” / “order count ≥ 1” in product DSL + RT.

---

### Q4 — Aggregates / where-chain (**pull**)

- [ ] sum/min/max; optional `where` chain only if Q3 preds insufficient.

---

### Q pull / non-goals

| Item | Notes |
|------|--------|
| Full LINQ query comprehensions / C# method chains | Prefer keyword scope (`any rel where`); no `.Where().Any()` |
| Dot/arrow nav as primary product dialect | See §4.0 — IR may still use RelationshipNavigation |
| EF/SQL generation as truth | Optional L\* exporter later; VM remains policy truth |
| Effect surface E\* | Parallel; don’t block E1 delete on Q3′ |
| Host I/O in expressions | Never |

---

## 6. Relationship to other plans

| Plan | Interaction |
|------|-------------|
| **Effect surface** | Effects **write** this instance / spawn / link; queries **read** graph. Cross-entity write not via assign. Ship Q1′ before marketing rich related guards. |
| **RT** | Q3′ **requires** instance graph + links to evaluate any/all. |
| **SA** | Unrelated except guards on stage actions. |
| **§6d L\*** | C#/LINQ **codegen** is different from **in-DSL query**; don’t conflate. |
| **Plugin/column** | Facets don’t replace query power. |

**Suggested sequencing vs effects:**

```text
E0/E1 (delete honesty + soft-delete)     // done
Q0 honesty (shipped vs planned surface)  // next
Q1′ path-prefix + Rel exists + where     // cross-entity reads
E2.1 link decision                         // graph writes still explicit
Q3′ any/all/count where                    // many-side; RT
E3a/E3b invoke                             // workflows
Q2 arithmetic as needed
```

Customer ship confidence: **kernel effects + Q1′ + (Q3′ or honest “no collection queries”)** — reads across graph; **no** peer writes via assign; **no** dots.

---

## 7. Success criteria (thin)

- [x] §4.0 surface direction frozen (B1+path; subject-first; anti-dot)  
- [x] §3.1 cross-entity reads legal / writes banned  
- [ ] Q0 guide honesty (shipped vs planned)  
- [ ] Q1′ path-prefix + `Rel exists` + to-one `where` green (DSL → lower → evaluate/simulate)  
- [ ] Assign: related LHS rejected; scalar related RHS OK  
- [ ] Q3′ any/all **or** explicit non-goal “no collection quantifiers in v1”  
- [ ] JSON policy parity plan (or documented split)  
- [ ] No full C# LINQ; no I/O in expressions  

---

## 8. Agent pick

**Micro-tasks:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md) — **primary pick order**.

```text
DONE:    E1 delete-self; §3.1 + §4.0 surface direction frozen (design)
CURRENT: qe Q0.1 — guide honesty for shipped expressions only
THEN:    Q0.2–Q0.5 (planned subject-first dialect named, not shipped)
THEN:    Q1.1 spec residual → Q1.2 path-prefix → Q1.3 Rel exists → Q1.3b where → Q1.4–Q1.6
THEN:    Q3′ any/all/count where OR explicit non-goal
PARALLEL: E2.1 link decision after Q0.1–Q0.2
LATER:   Q2 arithmetic; Q1b params; Q4 aggregates
PULL:    C# method chains; query comprehensions; EF-as-truth; product dots; cross-entity assign
```

**Implementer watch-outs**

- **Subject-first:** `Rel exists`, `Rel Prop…`, `Rel where…`, `any Rel where…` — never `rel.Prop`.  
- **`not Rel exists`** for absence (not `Rel not exists`, not prefix `exists Rel`).  
- **Cross-entity reads OK; writes banned** — assign target = this entity only.  
- **any/all need linked instances** — test under RT, not parse-only.  
- Keep DE → Syntax lower pure; no domain VM opcodes for this.  
- Printer: subject-first forms, never dots.  
- Guide + embed rebuild same PR as surface change.

---

## 9. Decision log

| Date | Decision | Notes |
|------|----------|-------|
| 2026-07-18 | Pursue **LINQ-inspired subset**, not full LINQ | Subject-centric; lowerable; **not** full C# LINQ |
| 2026-07-18 | Q1′ before Q3′ | Path-prefix + exists before collection quantifiers; RT for Q3′ |
| 2026-07-18 | **Anti-dot product surface** | No `rel.Prop` / `rel->Prop` |
| 2026-07-18 | **`where` is the scope keyword** | No `has`/`with` dialect |
| 2026-07-18 | **No forced `where (…)` parens** | Parens only for normal boolean grouping / `or` in body if needed |
| 2026-07-18 | **Path-prefix for simple related** | `assignee Active`, `customer Tier is "VIP"`; multi-pred → `rel where …`; policies + scalar assign RHS; never assign LHS |
| 2026-07-18 | **Cross-entity reads legal; writes banned** | §3.1 — observe related in policies and scalar assign RHS; assign target always current entity; peer mutation via create-in / link / invoke only |
| 2026-07-18 | **Postfix `exists`** | `Rel exists` (not prefix `exists Rel`) |
| 2026-07-18 | **Absence = `not Rel exists`** | Reuse outer `not`; **no** `Rel not exists` product spelling |
| 2026-07-18 | **B1+path frozen** | Path-prefix + postfix exists + to-one `where` — not where-only / not dots |
| 2026-07-18 | Open: many-side `orders exists` | Non-empty many? Decide in Q1.1 |
| 2026-07-18 | Open: where-body and vs or | Prefer and-chain + paren for `or` — finalize in Q1.1 |
