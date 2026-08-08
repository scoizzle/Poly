# Grammar pure end state — design direction

**Date:** 2026-08-07  
**Status:** **Direction lock + agent suite ready**  
**Agent suite:** [`simple-agent-tasks/gpure-README.md`](simple-agent-tasks/gpure-README.md) (`gpure-0`…`gpure-8` + gate)  
**Supersedes (as end-state policy):** hybrid-as-forever wording in archived [`archive/completed-2026-08-mid/grammar-integration.md`](archive/completed-2026-08-mid/grammar-integration.md) §5.2 item 3  
**Related:** E1 open forms live; temporal pack may use bridge; mcp-minify retires JSON dual media  

---

## 1. Decision

**Target end state:** product `.poly` parse (and eventually print) is **Grammar-table-driven** end-to-end: structure, expressions, effects — one registration model, one matcher story, packs extend tables/handlers, not private RD forks.

**Cutover hybrid is temporary infrastructure**, not the destination.

| Was (2026-08 cutover) | Becomes |
|----------------------|---------|
| Matcher for structure + annotations | Same, keep |
| `DslExpressionParser` RD precedence layers | **Grammar-owned expr** (engine may grow) |
| Hand `ParseEffect` / action bodies | **Grammar-owned effect** rules + handlers |
| `IExpressionPrimaryForm` RD hooks | Prefer **pattern registration** (+ thin handlers); forms that only work as opaque RD are a smell |
| “Painful ⇒ keep RD forever” | **Painful ⇒ evolve `Poly.Grammar`** |

---

## 2. Why

1. **One extension model** — temporal units, facets, future packs should not invent a second parser dialect.  
2. **Honesty** — “pattern table is the grammar” (Grammar README) is false while half the surface is method trees.  
3. **Maintainability** — every new surface in RD re-creates the problem GI was meant to stop.  
4. **Engine fitness** — if pure tables can’t express product DSL, the fix is **engine capability**, not permanent product RD.

---

## 3. What still isn’t Grammar-driven (inventory)

| Area | Today | Pure target |
|------|--------|-------------|
| Domain / entity / stage headers | Matcher ✓ | Keep |
| Annotations | Matcher + handlers ✓ | Keep; packs register patterns |
| Policy/effect **expressions** | RD layers in `DslExpressionParser` | Table + engine support for precedence / recursion |
| Open primaries (`Now`, `N days`) | `IExpressionPrimaryForm` (RD) | Prefer grammar patterns + specialization registry |
| Effects (`assign`, `if`, `create`, `invoke`, …) | Hand `ParseEffect` | Effect rule table + handlers → same `Effect` IR |
| Action bodies / params / require | Hand RD | Grammar rules |
| Printer | Domain walk | Prefer walk pattern table where structure is table-defined |

---

## 4. How Grammar should evolve (when pure is painful)

Do **not** grow `PolyDslParser` method trees as the long-term answer. Prefer engine upgrades, in order of likely need:

| Gap (product pain) | Possible Grammar evolution |
|--------------------|----------------------------|
| Left-assoc binary ops / precedence | Precedence-climbing or Pratt **in Matcher** (rule-associated), or layered rules with explicit left-recursion handling |
| Nested language (expr inside effect inside block) | Named recursive rule invoke as first-class element (not only `Many(rule)`) |
| Semantic predicates after partial match | Handler hooks / committed partials without full RD |
| Open pack literals (`12 days`) | Pattern + unit registry; fail-closed unknown |
| Error recovery / ExpectedTokens | First-token sets already partial; complete for agent UX |
| Print/parse symmetry | Printer already walks table — product should prefer table-shaped structure |

**Principle:** product code registers **patterns + handlers**; engine owns **scan/control flow**. Handlers produce IR; they should not re-implement a private recursive language unless the engine lacks a feature — then **file a Grammar change** in the same suite as the product need.

---

## 5. Relationship to current E1 / temporal

| Artifact | Role under pure direction |
|----------|---------------------------|
| `ExpressionFormRegistry` | **Bridge**: keep for temporal p1 vertical if needed; migrate forms → grammar patterns when engine can host them |
| `DslExpressionParser` | **Bridge**: shrink as rules move into tables; delete when empty |
| `DateOperation` IR | Unchanged — pure Grammar does not mean new temporal IR |
| p1 suite | May land on bridge; pure expr is **not** a hard prereq for first temporal goldens, but **new** expr sugar should not dig more RD |

---

## 6. Sequencing (admitted suite)

Solidified as **`gpure-*`**: see [`simple-agent-tasks/gpure-README.md`](simple-agent-tasks/gpure-README.md).

```text
gpure-0  Inventory
gpure-1  Engine Rule(ruleName)
gpure-2  Engine LeftAssoc
gpure-3  DslGrammar expr tables
gpure-4  Wire product expr parse
gpure-5  Effect grammar + wire
gpure-6  Open forms → patterns
gpure-7  Delete RD residual
gpure-8  CORE/docs
gpure-gate
```

**To finish one stream completely:** admit **gpure** as sole CURRENT until gate Done.  
Defer mcp-minify / mut-safety / p1 until gpure gate (or human waive).

---

## 7. Explicit non-goals (for first pure suite)

- Rewrite analysis/runtime  
- Pure Grammar for JSON bags (JSON expr is cancelled)  
- Perfect ExpectedTokens UX before green cutover  
- Multi-assembly `Poly.Dsl` until second consumer  

---

## 8. Success definition (pure done)

- [ ] No recursive-descent **language** left in DomainModeling Parsing for product `.poly` (handlers only map matches → IR).  
- [ ] New syntax = pattern registration (product or pack), not a new `ParseFoo` method tree.  
- [ ] Packs extend expr/effects without editing Matcher core.  
- [ ] CORE: Grammar owns engine; DomainModeling owns product/pack tables + handlers.  
- [ ] Full suite green; guide unchanged except error-shape honesty.  

---

## 9. Decision

**Agree with pure Grammar end state.**  
Hybrid cutover stands until a pure suite lands. **If pure is painful, evolve Grammar** — do not re-normalize permanent product RD.  
