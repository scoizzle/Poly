# DSL Query / Expression Surface (LINQ-inspired subset)

**Date:** 2026-07-18  
**Revised:** 2026-07-23 — Q3′ core **shipped** (`bb5032b`); residual R0–R4 **code-complete uncommitted**; **§17** honesty gate open  
**Status:** Active — Q1′ + Q3′ product vertical shipped; **do not mark residual batch Done** until §17 high items green  
**Current pick:** **§17 Q3.R′** — Description/guide honesty before commit  
**Micro-tasks:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md)  
**Related:** DomainExpression IR · `PolyDslParser` expression grammar · JSON policy parser · effect-surface plan · product guide · formal spec **§4.5**

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
| Arithmetic (`Add`…) | ✅ shipped | totals, age math (`Total - Discount > 100`) |
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

1. **Policies stay pure** — no mutation, no InvokeAction, no I/O in expressions.  
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

**Q1.1 open bits:** frozen in **§4.5.0** / §4.5.5 — do not re-litigate.

---

### Q-core (boolean / scalar on subject) — mostly **DSL expose IR**

| Syntax (product) | Maps to | Priority |
|------------------|---------|----------|
| `Prop` / compare / and / or / not | existing | ✅ done |
| `Rel Prop` / `Rel Prop op value` (path-prefix) | `RelationshipNavigation` / `OwnedAccess` | **Q1′** |
| `Rel exists` / `not Rel exists` | `Exists` / `NotExists` | **Q1′** |
| `Rel where and-chain…` (to-one multi-pred) | rebind + nav IR | **Q1′** |
| Scalar path-prefix on **assign RHS** | same nav IR; write stays local | **Q1′** |
| `@Name` / `param Name` | `ParameterAccess` | **Q1b** (action params DSL shipped for declarations; param *access* in expressions still open) |
| `A + B`, `*`, `-`, `/` | arithmetic DE | ✅ **shipped** (was Q2) |
| date ops (thin) | `DateOperation` | **Q2** residual |

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
- **Collection quantifiers** (`any`/`all`/`none`/`count`): path-prefix + `where` + `exists` is the shipped observation surface; quantifiers add IR complexity without named dogfood pain  

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
| `RelationshipNavigation` | ✅ path-prefix / `where` | — | ❌ | ✅ | 🟡 **authoring green**; **DMREL001** on many; **VM/store eval pull** (§13) | Subject-first |
| `OwnedAccess` | 🟡 path-prefix print | path-prefix | ❌ | ✅ | 🟡 | Printer anti-dot (`3c99221`) |
| `Exists` | ✅ `Rel exists` (nav or property) | — | ❌ | ✅ | 🟡 authoring; **RT eval pull** | N1 rel names accepted (`25a79ec`) |
| `NotExists` | 🟡 surface `not Rel exists` → **`Not(Exists)`** | optional `NotExists` IR | ❌ | ✅ | 🟡 | Guide honest (`3c99221`) |
| `ParameterAccess` | ❌ | **Q1b**: `@param` or `param Name` | ❌ | ❌ (needs type info) | ❌ (needs args) | Action param *declarations* DSL shipped; expression access still open |
| `Add` / `Subtract` / `Multiply` / `Divide` | ✅ `+ - * /` | — | ❌ | ✅ exists | ✅ | Shipped in E6 gap closure (suite **1398**) |
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
| 13 | "Total with tax" | `Total + Tax > 1000` — arithmetic | ✅ shipped | Order |
| 14 | "Overdue by more than 30 days" | date operation | **Q2** residual | Ticket |

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
| `assignee exists` | **`Exists(PropertyAccess("assignee"))`** — nav name as property/nav bag slot; lower → `Member(subject,"assignee") != null`. **Not** `RelationshipNavigation` with empty target. | Policy, require, constraint |
| `not certificate exists` | **`NotExists(PropertyAccess("certificate"))`** preferred (or `Not(Exists(...))` if printer folds) | Policy, require, constraint |
| `assignee Active` | `RelationshipNavigation("assignee", PropertyAccess("Active"))` | Policy, require, constraint; **scalar** on assign RHS |
| `customer Tier is "VIP"` | `RelationshipNavigation("customer", Comparison(PropertyAccess("Tier"), Eq, Literal("VIP")))` | Same; **scalar** RHS allowed |
| `customer where Status is "Active" and CreditLimit >= 1000` | Prefer lower as: eval body with subject = linked instance of `customer` (may be sugar over nav + and of compares). Document exact IR in Q1.3b if not pure `RelationshipNavigation` wrap. | Policy/require only |

> **Q1′′.1 (review):** Empty-string `PropertyAccess("")` inside `RelationshipNavigation` is **invalid** — fixed above. Q1.2/Q1.3 implementers must follow this table.

### 4.5.3 Validation rules

| Rule | Enforcement |
|------|-------------|
| Path-prefix bool/compare on `many` | Parse error — use `any Rel where …` (Q3′) |
| `Rel exists` on `many` | **Allowed** — non-empty check |
| Assign LHS with nav path | Parse error — cross-entity write banned |
| Assign RHS with `Rel exists` / `where` / quantifier | Parse error — boolean not a scalar value |
| Missing relation name | Evolution-time domain analysis error |
| `where` body may not nest `where` / quantifiers in v1 | Parse error or defer — keep body = comparisons + and |

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

- [x] **Q1.1** Spec freeze — **§4.5** (`beeb922`); open bits closed in §4.5.0  
- [x] **Q1.2** Parse/print + lower **path-prefix** (`Rel Prop`, `Rel Prop op value`) for policy + scalar assign RHS.  
- [x] **Q1.3** Parse/print + lower **`Rel exists`** / **`not Rel exists`** per §4.5.2 mapping.  
- [x] **Q1.3b** Parse/print + lower to-one **`Rel where` and-chain** (rebind).  
- [x] **Q1.4** Goldens: path-prefix policy, exists, optional where, scalar assign RHS; refuse related assign **LHS**.  
- [x] **Q1.5** JSON policy shapes for same (or document DSL-only split).  
- [x] **Q1.6** Guide examples (subject-first only; state cross-entity read/write rule).

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

### Q3′ — Collection quantifiers / count (**shipped** `bb5032b`)

**Goal:** Collection predicates over OneToMany — keyword forms, not C# LINQ.

- [x] **Q3.1** DE: `AnyExpr` / `AllExpr` / `NoneExpr` / `CountExpr`  
- [x] **Q3.2** Body rebind; `and`-chain; `or` needs parens  
- [x] **Q3.3** Runtime: store preprocess → literal before VM  
- [x] **Q3.4** Empty: any false; **all false** (no vacuous true); none true; count 0  
- [x] **Q3.5** DSL parse/print + guide  
- [x] **Q3.6** Library RT goldens + apply/export authoring  
- [x] **Q3.7** Analysis: OneToMany only; unknown / OneToOne fail  

**Exit (core):** met.  
**Residuals:** R0–R4 code in working tree; **§17 Q3.R′** honesty gate (Description/guide — no invent `link_instances`) before ship.

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
- [x] Q0 guide honesty (shipped vs planned) — `f483c2f`  
- [x] Q1.1 formal spec §4.5 — `beeb922`  
- [x] E2.1 create-in-only — effect plan decision log  
- [x] Q1′ parse/print/round-trip green (`959c6e7`, suite **1373**)  
- [x] Q1′ apply/export + assign goldens (`3c99221`, suite **1381**)  
- [x] Product claim **narrowed to authoring** — guide + tests (`514e21c`)  
- [x] many+property **DMREL001** analysis rejects (`76568a3`)  
- [x] Nested `where` ban + parse error (`514e21c`)  
- [x] Unknown-rel + reverse-direction analysis (`514e21c`)  
- [x] Path-prefix body validated against **target** entity (`514e21c`)  
- [x] **`Rel exists` on real nav** analysis green (`25a79ec`)  
- [x] Q3′ any/all/none/count **shipped** (`bb5032b`) — library RT + DSL  
- [~] Q3′ residuals R0–R4 **code-complete in working tree** — **§17** high honesty items before commit  
- [x] JSON policy split documented (no quantifiers in JSON)  
- [x] No full C# LINQ; no I/O in expressions  

---

## 8. Agent pick

**Micro-tasks:** [`simple-agent-tasks/qe-README.md`](simple-agent-tasks/qe-README.md) · **§17**.

```text
DONE:    Q3′ core bb5032b; residual R0–R4 implementation (uncommitted)
CURRENT: §17 Q3.R′ — honesty blockers (no invent link_instances; guide/instanceId alignment)
THEN:    Commit residual batch under review gate; optional E1 hygiene
PULL:    Q4; production IR; E3b; L*; public link MCP tool only if dogfood needs it
```

**Implementer watch-outs**

- **No inventing MCP tools in Descriptions** (`link_instances` does **not** exist).  
- `evaluate_policy(instanceId=)` **is** the product path for Q3′ MCP eval when the instance is store-registered with links.  
- Guide must document **real** linking: store/`create in Rel` / library Link — **not** a fake tool name.  
- Do not flip plan Status to Complete while §17 high items are open.

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
| 2026-07-18 | Q1.1 frozen | §4.5.0 — where=`and_expr`; many `exists` OK; soft-miss; repeat nav |
| 2026-07-18 | E2.1 | create-in only — effect-surface decision log |
| 2026-07-18 | **Q1′′ plan review** | §10 — pre-implement follow-ups |
| 2026-07-18 | **Q1′ ship** | `959c6e7` — parse/print/round-trip; suite 1373 |
| 2026-07-18 | **Q1′′′ post-ship review** | §11 — original residual list |
| 2026-07-18 | **Q1′′′ residual ship** | `3c99221` suite 1381 — assign/export/owned; eval partial |
| 2026-07-18 | **Q1′′′′ review** | §12 — reopen .1/.2 honesty; do not jump to Q3′ |
| 2026-07-18 | **Q1′′′′ ship** | `76568a3` — DMREL001; authoring-only tests; suite 1381 |
| 2026-07-18 | **Q1''''' review** | §13 — open list before ship |
| 2026-07-18 | **Q1''''' ship** | `514e21c` suite 1382 — guide, nested where, unknown-rel, body validation |
| 2026-07-18 | **Q1'''''' review** | §14 — Rel exists analysis bug identified |
| 2026-07-18 | **Q1'''''' ship** | `25a79ec` suite 1385 — Exists accepts N1 rel names; goldens |
| 2026-07-18 | **Q1''''''' review** | §15 — authoring complete; Q3′ next; low hygiene residual |
| 2026-07-23 | **Q3′ product vertical shipped** | `bb5032b` — any/all/none/count DSL+RT+analysis+guide |
| 2026-07-23 | **2B residual route** | Do not re-implement; simple-agent Q3.R0–R4 close-out |
| 2026-07-23 | **§17 residual review** | R0–R4 code-complete but **not ship-ready** — invent `link_instances` + plan overclaim |

---

## 10. Plan review — Q0 + Q1.1 + E2.1 (`f483c2f` / `beeb922`) *[historical]*

**Scope:** Query-surface plan, product guide Q0 sections, qe micro-suite, effect E2.1 decision.  
**Code:** Docs/plans only (suite **1360** unchanged).  
**Verdict:** Direction and Q0 honesty are **good enough to implement Q1.2**. Formal §4.5 is the right ship artifact. Several **doc consistency / DE mapping** issues must be fixed before or during Q1.2–Q1.3 (listed below).

### Solid

| Item | Notes |
|------|--------|
| §3.1 read/write split | Clear; assign LHS ban + scalar RHS read is coherent |
| §4.0 subject-first dialect | Path-prefix, postfix exists, unparenthesized `where` |
| Q0 guide shipped-vs-planned | Grammar table matches local expressions |
| Parity matrix | Useful; Q1′/Q3′ columns |
| Must-have list | Product spellings; banned write called out |
| Q1.1 §4.5 BNF + decisions | where=`and_expr`; many exists; soft-miss; repeat nav |
| E2.1 create-in-only | Aligns with write ban; effect plan decision log |

### Findings → follow-up tasks

| ID | Sev | Finding | Owner task |
|----|-----|---------|------------|
| **Q1′′.1** | **High** | §4.5.2 originally mapped `Rel exists` → `Exists(RelationshipNavigation(rel, PropertyAccess("")))` — empty property is invalid IR. **Corrected** to `Exists(PropertyAccess(relName))` (lower: Member ≠ null). Absence: prefer `NotExists(PropertyAccess(relName))`. | Q1.3 (+ re-read §4.5.2) |
| **Q1′′.2** | **High** | Guide **Expression Gaps** ends with: use JSON for IR capabilities not in DSL — **false for nav/exists** (JSON has no nav). Trailing `|` typo. Fix honesty: JSON is **weaker/equal** for comparison-only; related reads are DSL Q1′ only until Q1.5. | **Q0′ hygiene** (below) or early Q1.6 |
| **Q1′′.3** | Med | Status drift after commit: agent pick / success criteria still said CURRENT Q0.1; Q1.1 checklist open; qe-q1-1 task file Status still Not Started. **Partially fixed this review** in plan header/§5/§8. | Process — keep qe-README + task Status in sync on every ship |
| **Q1′′.4** | Med | Matrix row for `OwnedAccess` said `owned.Prop` (dot). **Fixed** to path-prefix. Guide Q0.2 still says owned = “Pull” while matrix says Q1′ — align: owned uses **same path-prefix as nav** in Q1′ when name resolves owned. | Q0′ hygiene + Q1.2 analysis |
| **Q1′′.5** | Med | §4.5.2 `where` mapping as pure `RelationshipNavigation` wrap may not match lower (body is boolean tree, not a “target property”). Q1.3b must document real IR (rebind eval vs new node). | **Q1.3b** |
| **Q1′′.6** | Low | Validation rules used `Rel.BoolProp` naming (dot in prose). Prefer “path-prefix on many”. Soft wording fix in §4.5.3. | done in this review if applied |
| **Q1′′.7** | Low | No `agent-summaries/qe-*-summary.md` for Q0/Q1.1/E2.1 — suite rule asked for them. Optional catch-up or drop requirement for docs-only tasks. | Optional process |
| **Q1′′.8** | Low | Effect plan header still “Q0 in progress”; E1′′′ residual text stale. Sync effect-surface agent pick to **Q1.2**. | docs hygiene |
| **Q1′′.9** | Low | `where` body BNF says `and_expr` which can recurse into primaries including another `where` — v1 should **forbid nested where/quantifiers** in body (validation row added). | Q1.3b goldens |
| **Q1′′.10** | Info | Embed: any guide fix requires MCP rebuild so `get_dsl_guide` matches file. | every guide PR |

### Follow-up checklist (write-back) — §10 closed or superseded by §11

- [x] **Q1′′.1** Exists → `Exists(PropertyAccess(relName))` — enforced in ship tests  
- [x] **Q1′′.2** Guide JSON honesty  
- [x] **Q1′′.3** Task Status sync on Q1′ ship  
- [ ] **Q1′′.4** Owned guide/printer — see **§11 Q1′′′.4**  
- [x] **Q1′′.5** `where` IR = `RelationshipNavigation(rel, body)` with And/Comparison body (shipped)  
- [x] **Q1′′.6–.8** docs hygiene  
- [ ] **Q1′′.9** Nested `where` — see **§11 Q1′′′.5**  

**Superseded next:** see **§11** (post-ship review).

---

## 11. Plan review — Q1′ ship (`959c6e7`, suite **1373**)

**Scope:** `PolyDslParser.ParseRelatedAccess`, `DomainDslPrinter` subject-first print, McpSmokeTests Q1′ block, guide Q1′ section, plan checklists.  
**Verdict:** **Accepted as parse/print vertical.** Product claim for **customer policies that evaluate** is **not** fully met — tests stop at AST shape + round-trip. Do **not** open Q3′ until §11 high items are green or explicitly deferred with honesty.

### Solid

| Item | Notes |
|------|--------|
| Path-prefix DE | `RelationshipNavigation(rel, PropertyAccess \| Comparison)` |
| `Rel exists` | `Exists(PropertyAccess(rel))` — matches §4.5.2 / Q1′′.1 |
| `not Rel exists` | Parses; prints; structure `Not(Exists(...))` |
| `Rel where and_expr` | `RelationshipNavigation(rel, And(...))`; no forced parens on input |
| Printer subject-first | Simple → `Rel Prop` / compare; complex → `Rel where …` |
| Local expressions | Age/or/and regression tests present |
| Guide §3.1 | Reads legal / writes banned stated |
| JSON split | Documented (Q1.5) |
| Suite | **1373** green |

### Findings → follow-up tasks

| ID | Sev | Finding | Recommended fix |
|----|-----|---------|-----------------|
| **Q1′′′.1** | **High** | Q1.4 task required evaluate/simulate/InvokeAction + true/false; ship tests are **parser-only** (no RT, no PolicyEvaluator, no soft-miss). | DomainModeling (or MCP) goldens: linked instance → policy true/false; missing to-one → false (soft-miss); `Rel exists` with/without link |
| **Q1′′′.2** | **High** | Guide claims `Rel Prop` on **many** is a **parse error** — parser has **no** cardinality check; any two identifiers become path-prefix. | Analysis fail-loud (preferred) or parser with domain context; **or** reword guide to “invalid / fail at analysis” until enforced; add negative test |
| **Q1′′′.3** | **Med** | No tests for **assign** related LHS reject or scalar related **RHS** (task Q1.4 listed both). | `assign Label to customer Tier` parse OK; `assign customer Status to "X"` fail-loud golden |
| **Q1′′′.4** | **Med** | `OwnedAccess` printer still **`owned.Inner` with dots**; guide still says “owned.prop” in one place and “Pull” in gaps table. | Print path-prefix; align guide with nav (same surface) |
| **Q1′′′.5** | **Med** | Nested `where` / nested related in `where` body still parse (`ParseAnd` → full primaries). Spec wanted ban. | Reject nested `where`/quantifier in body + test; or document allowed |
| **Q1′′′.6** | **Med** | Absence IR is `Not(Exists)` not `NotExists` — guide table says `NotExists`. | Prefer `NotExists` in parser **or** fix guide map to `Not(Exists)` |
| **Q1′′′.7** | Low | Q1′ tests live in `McpSmokeTests` but exercise pure DomainModeling parser (no MCP session). | Move to `Poly.Tests/DomainModeling/Parsing/` (hygiene) |
| **Q1′′′.8** | Low | Dead setup in first Q1′ test (unused `DomainEvolution` poly). | Delete dead lines |
| **Q1′′′.9** | Low | Matrix / success criteria / agent pick still partially pre-ship — **fixed this review**. | Keep §8 CURRENT = §11 residuals |
| **Q1′′′.10** | Low | Two consecutive local identifiers always path-prefix (`Foo Bar` → nav). Rare; document or analyze unknown rel. | Analysis unknown-rel diagnostic when domain known |
| **Q1′′′.11** | Info | `where` print may add parens around And body — OK if round-trips | Accept |
| **Q1′′′.12** | Info | No MCP embed verification test that `get_dsl_guide` contains Q1′ section | Optional smoke |

### Follow-up checklist (write-back)

- [~] **Q1′′′.1** **Partial** — apply/export/AST only (`3c99221`); **true RT/eval still open** → §12 **Q1′′′′.1**  
- [~] **Q1′′′.2** **Partial** — guide reworded; **analysis does not reject** many+property → §12 **Q1′′′′.2**  
- [x] **Q1′′′.3** Assign LHS ban + scalar RHS goldens  
- [x] **Q1′′′.4** OwnedAccess printer anti-dot  
- [ ] **Q1′′′.5** Nested `where` ban (or documented allow) + test  
- [x] **Q1′′′.6** Guide absence = `Not(Exists)`  
- [ ] **Q1′′′.7** Move parser tests to DomainModeling.Parsing  
- [ ] **Q1′′′.8** Remove dead code in first Q1′ test (unused DomainEvolution poly)  
- [x] **Q1′′′.9** Plan status (superseded by §12)  
- [ ] **Q1′′′.10** Unknown-rel analysis (pull)  
- [ ] **Q1′′′.12** Optional get_dsl_guide content smoke  

**Superseded next:** see **§12**.

---

## 12. Plan review — Q1′′′ residual ship (`3c99221`, suite **1381**)

**Scope:** New McpSmokeTests “Q1′′′” block; OwnedAccess printer; guide many/absence lines; plan checklists marked complete for .1/.2.  
**Verdict:** **Good incremental honesty** on assign + owned print + absence IR table. **Do not treat §11 high items as closed.** Q1′′′.1/.2 were checked off while still incomplete relative to original intent.

### Solid (accepted)

| Item | Evidence |
|------|----------|
| Assign multi-token LHS fail | `AssignLHS_MultiToken_Rejected` — apply_dsl fails |
| Assign scalar RHS parse | `assign Label to customer Tier` + export contains path-prefix |
| Owned printer anti-dot | `owned Inner` not `owned.Inner` |
| Absence guide | `Not(Exists(...))` matches parser |
| Apply/export authoring path | Policies with path-prefix / exists / where survive apply_dsl + export |
| Suite | **1381** green |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **Q1′′′′.1** | **High** | Q1′′′.1 tests **explicitly skip** VM/store eval (“future enhancement”). Names like `WithLinkedInstance_True` / `_TrueFalse` **overclaim**. No `PolicyEvaluator` / `evaluate_policy` / create_instance+link true-false. | Real eval goldens **or** rename tests + plan success criteria to **authoring-complete only**; do not claim RT-complete |
| **Q1′′′′.2** | **High** | Guide: “analysis pipeline **will reject**” many+property. **Code:** `PolicyConstraintAnalyzer` **skips** `RelationshipNavigation` entirely — no cardinality check. Test only proves parser accepts. | Implement analysis diagnostic **or** reword: “not yet validated; do not use on many; Q3′ for collection preds” |
| **Q1′′′′.3** | Med | `Flag exists` golden uses **local Boolean property**, not a relationship — weak product proof for “related exists” | Prefer `assignee exists` with nav in domain; optional soft-miss |
| **Q1′′′′.4** | Med | Plan/roadmap marked Q1′′′ residuals **Complete** / agent pick jumped to **Q3′** while .1/.2 incomplete | Fixed this review — CURRENT = §12 |
| **Q1′′′′.5** | Low | Nested `where` still open (Q1′′′.5) | Ban + test or document allowed |
| **Q1′′′′.6** | Low | Dead setup in `Parser_PathPrefix_RelBoolProp_*` still present | Delete unused poly/evolution |
| **Q1′′′′.7** | Low | Guide “Not yet shipped” still says `owned.prop` (dot) | `owned Prop` path-prefix |
| **Q1′′′′.8** | Low | Tests still in McpSmokeTests | Move to DomainModeling.Parsing |
| **Q1′′′′.9** | Info | Store/VM wiring for RelationshipNavigation may need DomainModeling work beyond DSL — spike if eval blocked | Spike before Q3′ |

### Follow-up checklist (write-back)

- [x] **Q1′′′′.1** Tests renamed authoring-only (`76568a3`) — **guide still weak** → §13 Q1'''''.1  
- [x] **Q1′′′′.2** **DMREL001** shipped + apply_dsl golden  
- [ ] **Q1′′′′.3** Related `exists` on real nav — §13  
- [x] **Q1′′′′.4** Plan pick (superseded by §13)  
- [ ] **Q1′′′′.5** Nested where — §13  
- [ ] **Q1′′′′.6** Dead code — §13  
- [x] **Q1′′′′.7** Guide `owned Prop`  
- [ ] **Q1′′′′.8** Test placement — §13  
- [ ] **Q1′′′′.9** Eval pipeline spike — §13  

**Superseded next:** see **§13**.

---

## 13. Plan review — Q1′′′′ ship (`76568a3`, suite **1381**)

**Scope:** `PolicyConstraintAnalyzer.ValidateRelationshipCardinality` + `DMREL001`; McpSmokeTests renames + many-analysis golden; guide owned spelling; plan checklists.  
**Verdict:** **Accepted for many+property analysis.** Authoring-only is honest in **tests** but still **under-documented in the product guide**. Do not open Q3′ until nested-where policy is deliberate; optional RT eval remains a separate product decision.

### Solid

| Item | Notes |
|------|--------|
| **DMREL001** | `OneToMany` / `ManyToMany` path-prefix → analysis error with Q3′ hint |
| Apply golden | `orders Status is "Open"` on many → `apply_dsl` fails; message mentions orders/many |
| Exists on many | Still `Exists(PropertyAccess)` — not RelationshipNavigation — so `orders exists` not blocked by DMREL001 (matches §4.5.0) |
| Test renames | Dropped false `Eval_*_True` names; comments state authoring-only |
| Owned guide | `owned Prop` anti-dot spelling |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **Q1'''''.1** | **Med** | Guide § Subject-First Related Reads does **not** say related policies are **authoring-only** / not RT-evaluated. Agents still read it as full product evaluate path. | One clear guide sentence + optional “Not yet: RT evaluate related” under rules |
| **Q1'''''.2** | **Med** | Nested `where` / nested related forms in body still parse; no ban/test | Ban nested `where` + test **or** document allowed |
| **Q1'''''.3** | **Med** | Unknown / non-source relationship name: `ValidateRelationshipCardinality` **returns without error** — no “unknown nav” diagnostic | Report error when rel not found on entity (or document deferral) |
| **Q1'''''.4** | **Med** | Path-prefix **body** properties not validated against **target** entity (early return after cardinality check) — `customer Bogus is "x"` may not fail analysis | Validate TargetProperty against related entity type |
| **Q1'''''.5** | Low | Many+property test asserts message substrings, not code **`DMREL001`** | Assert diagnostic code in evolution/analysis result if surface exposes it |
| **Q1'''''.6** | Low | Dead unused poly in `Parser_PathPrefix_RelBoolProp_*` still present | Delete |
| **Q1'''''.7** | Low | Gaps table still lists Owned as **Pull** while path-prefix ships for nav names; DSL never emits `OwnedAccess` (always `RelationshipNavigation`) | Align owned story: path-prefix via RelationshipNav **or** parse owned as OwnedAccess |
| **Q1'''''.8** | Low | Tests still in McpSmokeTests | Move DomainModeling parser/analysis tests under DomainModeling |
| **Q1'''''.9** | Pull | True RT/eval true-false + soft-miss | Separate product slice when store/VM path ready |
| **Q1'''''.10** | Info | Roadmap line still said “until Q1′′′′.1 green” while .1 closed as authoring-only — fixed this review | — |

### Follow-up checklist (write-back)

- [x] **Q1'''''.1** Guide: explicit **authoring-only** (no RT evaluate claim for related policies yet)  
- [x] **Q1'''''.2** Nested `where` ban + test  
- [x] **Q1'''''.3** Unknown relationship name fail-loud + reverse-direction diagnostic in analysis  
- [x] **Q1'''''.4** Validate path-prefix body props against **related** entity (TargetField now fails)  
- [ ] **Q1'''''.5** Assert `DMREL001` code in golden (deferred — ApplyDsl response doesn't surface diagnostic codes directly)  
- [ ] **Q1'''''.6** Dead code cleanup  
- [ ] **Q1'''''.7** OwnedAccess vs RelationshipNavigation product story  
- [ ] **Q1'''''.8** Test placement hygiene  
- [ ] **Q1'''''.9** Optional RT eval slice (pull)  

**Slice exit (partial):** Nested-where, unknown-rel, body validation, guide authoring-only **shipped**. **Blocking residual:** §14 **Q1''''''.1** (`Rel exists` vs property analysis).

---

## 14. Plan review — Q1''''' ship (`514e21c`, suite **1382**)

**Scope:** Guide authoring-only sentence; `_inWhereBody` nested-where ban; `ValidateRelationshipCardinality` unknown/reverse + `ValidateRelatedBodyProperties`; analysis test flip; nested-where smoke.  
**Verdict:** **Strong analysis/parser hygiene.** **One high-severity product bug** remains for the headline form `assignee exists`.

### Solid

| Item | Notes |
|------|--------|
| Guide authoring-only | Explicit: parse/apply/export; RT evaluate related = future |
| Nested `where` | Parse error + `Parser_NestedWhere_Rejected` |
| Unknown rel | Fail-loud; reverse-direction message when name exists on other entity |
| Body vs target entity | `TargetField` missing on Target → analysis fail; test updated |
| Nested-where flag | `try/finally` restores `_inWhereBody` |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **Q1''''''.1** | **High** | Product IR is `Exists(PropertyAccess(relName))`. N1 nav creates a **relationship only** (not an entity `Property`). Analysis recurses into `Exists` → validates `PropertyAccess` against **source properties** → **false “property does not exist”** for real `assignee exists` / `customer exists`. No apply_dsl golden covers nav+exists (only `Flag exists` local prop). | Special-case `Exists`/`Not`/`NotExists` targets: if name is a **relationship** on the entity, accept (optional: allow missing-link soft semantics later). Golden: `assignee: Agent` + `policy { assignee exists }` apply succeeds |
| **Q1''''''.2** | Med | Nested **path-prefix** inside `where` body still parses (`customer where other Status is "X"`) — only nested **`where` keyword** banned | Ban nested `RelationshipNavigation` in where body **or** document allowed |
| **Q1''''''.3** | Med | Guide authoring sentence mixes “subscription fan-out” with related-expression eval — slightly confusing | Tighten wording: related **expression** policies not evaluated; local policies still evaluate |
| **Q1''''''.4** | Low | No happy-path analysis test: body prop **exists** on target → success | One positive `DomainEvolution` test |
| **Q1''''''.5** | Low | Carry Q1'''''.5–.8 hygiene (DMREL001 code, dead code, owned story, test placement) | Pull |
| **Q1''''''.6** | Pull | RT eval related policies | When product needs evaluate |

### Follow-up checklist (write-back)

- [x] **Q1''''''.1** Fix `Rel exists` analysis for **nav relationships** + apply_dsl golden — `assignee: Agent` + `policy { assignee exists }` applies cleanly  
- [x] **Q1''''''.2** Nested path-prefix in where body — **plan/commit claim “documented allowed”**; **product guide still silent** → §15  
- [x] **Q1''''''.3** Guide wording tightened — removed confusing subscription fan-out reference  
- [x] **Q1''''''.4** Happy-path body validation test — `customer Tier is "VIP"` on valid target entity succeeds  
- [ ] **Q1''''''.5** Low hygiene carry → §15  
- [ ] **Q1''''''.6** RT eval pull  

**§14 exit:** `Rel exists` on nav relationships fixed. See **§15** for residual hygiene + Q3′ pick.

---

## 15. Plan review — Q1'''''' ship (`25a79ec`, suite **1385**)

**Scope:** `IsRelationshipOnEntity` in PropertyAccess validation; apply_dsl goldens for `assignee exists` / `not certificate exists`; happy-path `customer Tier is "VIP"`; guide tighten.  
**Verdict:** **Accepted.** Blocking §14 high item closed. Q1′ **authoring vertical is product-complete** under stated claims (not RT eval). Remaining work is **Q3′ decision** or low hygiene — not more “fix the surface.”

### Solid

| Item | Notes |
|------|--------|
| Rel exists on N1 | `PropertyAccess` missing from entity props but matching source-side relationship → no false error |
| apply_dsl goldens | `HasAssignee: policy { assignee exists }`; `not certificate exists` |
| Happy-path body | `customer Tier is "VIP"` succeeds when Tier on Customer |
| Guide | Local-only evaluate/simulate; related expression eval future |
| Suite | **1385** green |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **Q1'''''''.1** | Low | Nested path-prefix-in-where “documented allowed” only in plan/commit — **not in product guide** | One guide bullet: nested `where` banned; nested path-prefix in body allowed (or ban later) |
| **Q1'''''''.2** | Low | Bare `PropertyAccess` that is **only** a relationship name (not under `Exists`) also suppressed — `policy { assignee }` may pass analysis | Optional: only suppress when parent is `Exists`/`NotExists`/`Not(Exists)` (requires parent context in walk) |
| **Q1'''''''.3** | Low | Dead unused poly still in first path-prefix parser test | Delete |
| **Q1'''''''.4** | Low | Owned still “Pull” in gaps table vs path-prefix Reality | Align docs |
| **Q1'''''''.5** | Low | Tests still mostly in McpSmokeTests | Move DomainModeling-focused tests |
| **Q1'''''''.6** | Low | Plan/roadmap status drift risk (row still “Next” while shipped) — **fixed this review** | Process |
| **Q1'''''''.7** | Product | Q3′ any/all/count → **explicit non-goal** (see §7 / §15) | Non-goal written 2026-07-18 |
| **Q1'''''''.8** | Pull | RT eval related policies | When product needs evaluate |

### Follow-up checklist (write-back)

- [ ] **Q1'''''''.1** Guide note: nested path-prefix in `where` body allowed (keyword nested `where` banned)  
- [ ] **Q1'''''''.2** Optional tighten Exists-only exception for relationship names  
- [ ] **Q1'''''''.3–.5** Hygiene (dead code, owned story, test placement)  
- [x] **Q1'''''''.7** Q3′ → **explicit non-goal** (collection quantifiers not shipped in v1)
- [ ] **Q1'''''''.8** RT eval pull  
- [x] **Q1'''''''.9** Arithmetic (**Q2**) DSL shipped — E6 gap closure 2026-07-19; matrix/rows updated  
- [ ] **Q1'''''''.10** Q1b: parameter *access* in expressions (`@Name` / `param Name`) — declarations DSL shipped via effect plan; expression IR access still open  
- [ ] **Q1'''''''.11** DateOperation thin surface (remaining Q2 residual)  

**Recommended next:** §15 low hygiene (optional); effect **E6.1** RT goldens if on effect track; RT eval related policies when product needs it. Do not reopen Q3′ without named dogfood pain.

---

## 16. Cross-plan note — E6 gap closure (2026-07-19)

Effect-surface review **E6** closed arithmetic authoring (this plan’s former Q2 row) along with invoke/conditional/params/inheritance/equals/enum/owned on the effect track. See [`effect-surface-completeness.md`](effect-surface-completeness.md) §13 for code-review verdict and residual **E6.1–E6.13**.

| Query residual | Status after E6 |
|----------------|-----------------|
| Arithmetic DE | ✅ DSL shipped |
| Parameter **declaration** on actions | ✅ DSL shipped (effect plan) |
| Parameter **access** in expressions | ❌ still Q1b |
| Date ops | ❌ still Q2 residual |
| Collection quantifiers | ✅ **shipped** Q3′ (`bb5032b`) — supersedes prior non-goal |
| Related RT eval | ✅ store-linked `EvaluatePolicy`; MCP `instanceId` path in residual batch |

---

## 17. Plan review — Q3′ residual batch (uncommitted, 2026-07-23)

**Scope:** Working-tree residual close-out after Q3′ core (`bb5032b`):

| Area | Files |
|------|--------|
| MCP | `DomainTools.cs` — `evaluate_policy` optional `instanceId` |
| Tests | `McpSmokeTests.EvaluatePolicy_Q3Prime_Any_WithLinkedInstances` |
| Guide | empty-collection table; Q3′ removed from “Not yet shipped”; related-policy eval paragraph |
| Plans | `qe-README` R0–R4; residual micro-task files; roadmap/README pointers |

**Suite:** Not re-run in this review pass; smoke test is present and asserts Message true/false.  
**Verdict:** **Do not commit / do not mark Complete** until **high** honesty items below are fixed. Product code direction is right (`instanceId` on store-registered instances). Docs + Description invent a non-existent MCP tool and overclaim residual Done.

### Solid

| Item | Notes |
|------|--------|
| `instanceId` path | Correct seam: resolve from `InstanceMap`, entity-name check, fail-loud missing instance |
| Empty semantics table | Matches product: any F, **all F** (no vacuous true), none T, count 0 |
| Q3′ off “Not yet shipped” | Correct relative to `bb5032b` |
| Residual 2B route | Right — do not re-build IR/parser |
| MCP true/false proof | Smoke exercises `any` true + `none` false with linked orders |
| Fail-closed instance checks | Unknown id + wrong entity return error responses (not silent local eval) |

### Findings → follow-up tasks

| ID | Sev | Finding | Fix |
|----|-----|---------|-----|
| **Q3.R′.1** | **High (honesty)** | `evaluate_policy` MCP **Description** tells agents: `create_instance + link_instances` then `instanceId`. **There is no `link_instances` MCP tool** (grep: zero product tool; only `DomainInstanceStore.Link` / `TryModifyInstances` in tests). | Rewrite Description: no invented tool. Honest path = `create_instance` (store-registered) + graph via **`create in Rel`** / `invoke_action` effects, **or** library Link in tests. Then pass `instanceId`. |
| **Q3.R′.2** | **High (honesty)** | Both product guides claim the same invented path (`create_instance + link_instances + invoke_action`) **and** still say standalone `evaluate_policy`/`simulate_policy` are local-only only — while residual code **does** evaluate store-linked instances when `instanceId` is provided. | Rewrite related-policies paragraph: (1) local subject bag = local-only; (2) `evaluate_policy(instanceId=)` = store-attached eval for Q3′/related; (3) real linking story without `link_instances`. |
| **Q3.R′.3** | **Med (contract)** | R2 golden meets “true/false” but links via **`TryModifyInstances` + `InstanceStore.Link`** — not an agent-callable MCP path. R2 exit “Uses MCP/session tools agents would call” is **partial**. | Keep test (valid session-store proof). Document honesty: MCP eval path proven; **agent linking** still `create in` / library. Optional later: pure MCP golden via `create in` + invoke. |
| **Q3.R′.4** | **Med (plan honesty)** | Plans overclaim Done: header Status Complete; `qe-README` Gate `[x]`; master-roadmap residual **Complete** — while uncommitted + invent-tool Description open. | Flip residual status to open until §17 high green; Gate `[ ]` until re-review. |
| **Q3.R′.5** | Low | Golden asserts `Message.Contains("true"/"false")` instead of structured `Data.result`. Works with current message text but brittle. | Prefer `Data` / anonymous `result` bool when easy. |
| **Q3.R′.6** | Low | No MCP golden for **empty links** → `any` false (R2 step mentioned empty; only covered via library goldens). | Optional one smoke: create customer, no links, `any` → false. |
| **Q3.R′.7** | Low | `qe-q3-r0-status-inventory.md` still claims `link_instances` path and “evaluate_policy throws on Q3′” — stale vs `instanceId`. | Refresh inventory notes or leave historical with strikethrough. |
| **Q3.R′.8** | Pull | Public `link_instances` MCP tool | Only if dogfood proves agents cannot wire graphs via `create in` alone. Not required to close residual honesty. |
| **Q3.R′.9** | Low | When `instanceId` set, `age`/`properties` ignored silently | Description note: instanceId wins; subject bag unused. |

### Three-layer defense (this batch)

| Concern | Parse | Analyze | Runtime / MCP |
|---------|-------|---------|----------------|
| Q3′ quantifiers | already shipped | OneToMany only | store preprocess + VM |
| Missing instanceId target | n/a | n/a | fail-loud “not found” ✅ |
| Invented link tool | n/a | n/a | **Description contract fails agents** — fix before ship |
| Empty collection | n/a | n/a | fail-closed semantics documented ✅ |

### Follow-up checklist (write-back)

- [ ] **Q3.R′.1** Fix `evaluate_policy` Description — **remove `link_instances`**; document real path + `instanceId`  
- [ ] **Q3.R′.2** Fix both guides — eval claim + real linking; dual-path local vs `instanceId`  
- [ ] **Q3.R′.3** Document R2 golden honesty (store Link in test OK; agent link path separate)  
- [ ] **Q3.R′.4** Plan/README/roadmap status: residual open until high items green + commit  
- [ ] **Q3.R′.5** (optional) Assert `Data.result` in MCP golden  
- [ ] **Q3.R′.6** (optional) Empty-links `any` false MCP smoke  
- [ ] **Q3.R′.7** (optional) Refresh R0 inventory stale claims  
- [ ] **Q3.R′.9** (optional) Description: `instanceId` supersedes age/properties  
- [ ] **Gate** Re-run [`pr1-uncommitted-review-gate.md`](simple-agent-tasks/pr1-uncommitted-review-gate.md); build + suite; only then commit  
- [ ] **Pull Q3.R′.8** Public link tool — only on dogfood pain  

**Recommended next:** Fix **Q3.R′.1 + Q3.R′.2** (smallest honesty loop) → re-review dirty tree → commit residual batch. Do **not** open Q4 / public link tool to close this gate.

---
