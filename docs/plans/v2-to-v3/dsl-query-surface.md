# DSL Query / Expression Surface (LINQ-inspired subset)

**Date:** 2026-07-18  
**Status:** Active product-design backlog — **parallel to** [`effect-surface-completeness.md`](effect-surface-completeness.md); **before** customer ship confidence  
**Current pick:** **Q0** inventory + guide honesty; then **Q1** navigation + existence (IR already partly exists)  
**Related:** DomainExpression IR · `PolyDslParser` expression grammar · JSON policy parser · effect-surface plan · product guide  

**Principle:** Policies and guards are only as useful as the **query language** inside them. Prefer a **small, lowerable, LINQ-shaped subset** that maps to existing `DomainExpression` + Syntax AST over a full LINQ / SQL clone. No host I/O in expressions.

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

1. **Boolean / scalar** (for policies and assign RHS), not full IQueryable pipelines  
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
| `RelationshipNavigation` | ❌ | `orders.Total`, navigate related |
| `OwnedAccess` | ❌ | nested/owned document fields |
| `Exists` / `NotExists` | ❌ | optional owned / related presence |
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
| Navigate | `.` / join | `customer.Tier is "Gold"` |
| Null/optional | null pattern | exists certificate |

**Select-many pipelines, deferred IQueryable, side-effecting queries:** **out of scope.**

---

## 3. Design rules

1. **Policies stay pure** — no mutation, no CallAction, no I/O in expressions.  
2. **Lower or don’t ship** — every new DE node needs `DomainExpressionLoweringPass` + tests (VM or dual-oracle).  
3. **DSL and JSON parity over time** — MCP `add_policy` JSON should not forever be weaker than DSL (or document the split).  
4. **Collections = relationship `many` (and later owned lists)** — not arbitrary .NET IEnumerable.  
5. **Subject is implicit** — like LINQ method syntax on `this` entity: `orders.any(o => o.Status is "Open")` not free-floating `from x in db`.  
6. **No full C# LINQ** — no query comprehensions spanning databases; no expression trees from customer C#.  
7. **Ship with guide + goldens** — same honesty bar as effects (E0).  
8. **Sequence with effects** — query surface unblocks *meaningful* guards on create-in / when / delete; coordinate with effect plan but don’t block delete-self on full LINQ.

---

## 4. Proposed product surface (LINQ-inspired subset)

### Q-core (boolean / scalar on subject) — mostly **DSL expose IR**

| Syntax (sketch) | Maps to | Priority |
|-----------------|---------|----------|
| `Prop` / compare / and / or / not | existing | ✅ done |
| `rel.Prop` or `rel->Prop` | `RelationshipNavigation` | **Q1** |
| `owned.Prop` | `OwnedAccess` | **Q1** |
| `exists rel` / `exists owned` | `Exists` / `NotExists` | **Q1** |
| `param.Name` or `@Name` | `ParameterAccess` | **Q1b** (with action params story) |
| `A + B`, `*`, `-`, `/` | arithmetic DE | **Q2** |
| date ops (thin) | `DateOperation` | **Q2** |

### Q-linq (collection quantifiers / aggregates) — **new IR + lower**

| Syntax (sketch) | Meaning | Priority |
|-----------------|---------|----------|
| `rel.any(x => pred)` | ∃ related matching pred | **Q3** |
| `rel.all(x => pred)` | ∀ related matching pred | **Q3** |
| `rel.none(x => pred)` | ¬any | **Q3** (sugar) |
| `rel.count()` / `rel.count(x => pred)` | cardinality | **Q3** |
| `rel.sum(x => x.Amount)` | aggregate | **Q4** |
| `rel.where(x => pred).any(...)` | chained filter | **Q4** only if needed; prefer pred inside any |

**Method syntax preferred** over `from…where…select` query syntax (simpler parser, clearer subject).

### Non-goals (customer v1)

- `join`, `group by`, `orderby`, pagination  
- Cross-domain queries / second root  
- Mutating queries  
- Full `IQueryable` / EF translation as product claim (optional later exporter)  
- Loading strategies / N+1 productization in the language  

---

## 5. Slices

### Q0 — Freeze + honesty (**small**)

- [ ] **Q0.1** Document current DSL expression grammar in product guide (and/or/not/compare only).  
- [ ] **Q0.2** Document IR nodes not in DSL (nav, exists, arithmetic, params).  
- [ ] **Q0.3** Matrix: DE node × DSL × JSON × lower × VM (keep next to effect matrix).  
- [ ] **Q0.4** Decide method-syntax keyword set (`any`/`all`/`count` vs `Any`).  
- [ ] **Q0.5** Customer “must have” list from 1–2 target domains (not full LINQ).

**Exit:** Guide doesn’t overclaim; implementers know Q1 vs Q3.

---

### Q1 — Navigate + exists (expose IR) (**medium**)

**Goal:** Guards over related/owned data without new quantifier IR.

- [ ] **Q1.1** Spec: navigation syntax (`orders.Status` vs `orders->Status` vs `Orders.Status`).  
- [ ] **Q1.2** Parse/print + lower `RelationshipNavigation` / `OwnedAccess`.  
- [ ] **Q1.3** Parse/print + lower `exists` / `not exists` (or method form).  
- [ ] **Q1.4** Goldens: policy on related field; simulate_policy / evaluate_policy / CallAction require.  
- [ ] **Q1.5** JSON policy shapes for same (or document DSL-only until Q1.5).  
- [ ] **Q1.6** Guide examples.

**Exit:** “Customer of this order is Active” / “has certificate” authorable in DSL.

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

### Q3 — LINQ quantifiers / count (**medium–large**)

**Goal:** Collection predicates over `many` relationships.

- [ ] **Q3.1** New DE nodes (e.g. `Any`, `All`, `Count`) or lower sugar to existing Syntax.  
- [ ] **Q3.2** Lambda binder: `orders.any(o => o.Total > 0)` — scope `o` as related subject.  
- [ ] **Q3.3** Runtime: enumerate linked instances via store (needs RT session for MCP goldens).  
- [ ] **Q3.4** Empty collection semantics (any → false, all → true).  
- [ ] **Q3.5** DSL parse/print + guide.  
- [ ] **Q3.6** Goldens: any/all/count with 0, 1, N related instances.  
- [ ] **Q3.7** Analysis: unknown rel, wrong cardinality (any on `one`?).

**Exit:** “Has any open order” / “all lines reserved” / “order count ≥ 1” in product DSL + RT.

---

### Q4 — Aggregates / where-chain (**pull**)

- [ ] sum/min/max; optional `where` chain only if Q3 preds insufficient.

---

### Q pull / non-goals

| Item | Notes |
|------|--------|
| Full LINQ query comprehensions | Parser + mental load; method syntax first |
| EF/SQL generation as truth | Optional L\* exporter later; VM remains policy truth |
| Effect surface E\* | Parallel; don’t block E1 delete on Q3 |
| Host I/O in expressions | Never |

---

## 6. Relationship to other plans

| Plan | Interaction |
|------|-------------|
| **Effect surface** | Effects change graph; queries **observe** it. Ship Q1 before marketing rich when/create-in rules. |
| **RT** | Q3 **requires** instance graph + links to evaluate any/all. |
| **SA** | Unrelated except guards on stage actions. |
| **§6d L\*** | C#/LINQ **codegen** is different from **in-DSL query**; don’t conflate. |
| **Plugin/column** | Facets don’t replace query power. |

**Suggested sequencing vs effects:**

```text
E0/E1 (delete honesty + soft-delete)     // still valuable
Q0 → Q1 (nav + exists)                   // unblocks real policies
E2 decision (create-in vs link)          // graph writes
Q3 (any/all/count)                       // needs graph + RT
E3a/E3b invoke                           // workflows
Q2 arithmetic as needed
```

Customer ship confidence: **kernel effects + Q1 + (Q3 or honest “no collection queries”)** — not effects alone.

---

## 7. Success criteria (thin)

- [ ] Q0 guide honesty for expression surface  
- [ ] Q1 nav + exists green (DSL → lower → evaluate/simulate)  
- [ ] Q3 any/all **or** explicit non-goal “no collection quantifiers in v1”  
- [ ] JSON policy parity plan (or documented split)  
- [ ] No full LINQ; no I/O in expressions  

---

## 8. Agent pick

```text
DONE:    Lifecycle kernel DSL effects; boolean property policies
CURRENT: Q0 — document expression surface + IR gaps (parallel E0 effects)
THEN:    Q1 navigation + exists (expose IR)
THEN:    Q3 any/all/count (new IR + store enumeration) OR explicit non-goal
PARALLEL: E1 delete-self (effect plan)
LATER:   Q2 arithmetic; Q1b params; Q4 aggregates
PULL:    Full LINQ; EF-as-truth; query comprehensions
```

**Implementer watch-outs**

- Do not paste C# LINQ into `.poly` as a goal.  
- **any/all need linked instances** — test under MCP RT, not parse-only.  
- Keep DE → Syntax lower pure; fix gaps with new DE nodes + lower, not VM opcodes.  
- Printer round-trip for every new form.  
- Update product guide in the same PR (same as effect E0.1 discipline).

---

## 9. Decision log

| Date | Decision | Notes |
|------|----------|-------|
| 2026-07-18 | Pursue **LINQ-inspired subset**, not full LINQ | Method syntax; subject-centric; lowerable |
| 2026-07-18 | Q1 before Q3 | Nav/exists reuse IR; quantifiers need new work + RT |
