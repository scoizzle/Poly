# gpure inventory notes

**Task:** [`gpure-0-inventory.md`](./gpure-0-inventory.md)  
**Date:** 2026-08-07  
Scope: every product parse path still recursive-descent language, plus Grammar engine gaps. **No production code was changed** (docs only).

---

## A. RD residual

Grep: `private DomainExpression Parse|private Effect Parse|ParseOr|ParseAnd|ParsePrimary|ParseComparison|ParseMultiply|ParseNot|ParseConditionalEffect|ParseCreateEffect|ParsePropertyInitializers|ParseEffect` in `Poly/DomainModeling/Parsing` — 44 hits.

### A1. Expression layers — `DslExpressionParser.cs`

| Method | Line | Role | Target Grammar rule (final) |
|--------|------|------|-----------------------------|
| `ParseExpression()` | 25 | expr top entry (`ParseOr`) | `expr` = Rule("expr-or") |
| `ParseOr()` | 30 | or-layer (`or` keyword text) | `expr-or` = LeftAssoc(expr-and, Or) |
| `ParseAnd()` | 41 | and-layer (`and` keyword text) | `expr-and` = LeftAssoc(expr-not, And) |
| `ParseNot()` | 52 | `not` — operand is **`ParseAdd()`** (B3) | `expr-not` = Not+expr-add \| expr-compare |
| `ParseComparison()` | 61 | single compare (not a chain) | `expr-compare` = expr-add-no-not [compareOp expr-add]? |
| `ParseAdd()` | 96 | `+`/`-` left-assoc while-loop | `expr-add` = LeftAssoc(expr-mul, Plus/Minus) |
| `ParseMultiply()` | 109 | `*`/`/` left-assoc while-loop | `expr-mul` = LeftAssoc(expr-primary, Star/Slash) |
| `ParsePrimary()` | 122 | primary: literals, group, ident, not | `expr-primary` (+ `-no-not` variants for comparison LHS) |
| `ParseRelatedAccess()` | 180 | path-prefix / exists / where | residual handler after ident (allowed) |
| `ParseQuantifiedExpression()` | 247 | any/all/none/count | residual handler after ident (allowed) |

**Comparison LHS (`-no-not` layer):** product `ParseNot` consumes not-led input before comparison is ever reached, so a bare `not` comparison LHS is unreachable — `expr-compare` LHS uses `expr-add-no-not` / `expr-mul-no-not` / `expr-primary-no-not` (primary minus the `not` pattern). `(not a) > b` still works via the group; `x > not y` works via the full `expr-add` RHS. This is what makes `not a > b` reject on both paths (B3).

**Span vs live-path note (gpure-4):** the no-not LHS chain makes the TABLE stricter than product for nested nots inside an add chain (`a + not b`, `a + not b > c` — product accepts via primary-Not re-entry; the span table rejects because the comparison LHS chain is no-not END TO END). The live fold is product-faithful (accepts), so the span tables serve the gpure-3 span corpus + future printer/introspection, not the live gate. No span gate on the live path; fail-closed errors come from the fold (`Expected expression`).

**S1 pin (2026-08-08):** both sides are pinned in `DslExprParityTests.SpanVsFold_NotInChain_TableRejectsFoldAccepts` (span rejects + fold IR oracles). **Tracking:** when the span tables gain a live consumer (printer/validator), reconcile the no-not LHS chain with the fold — decide whether `not` may re-enter mid-chain; if yes, give the `-no-not` layers a `not` re-entry path (or split LHS/RHS chains) and delete this note.

### A2. Product parser — `PolyDslParser.cs`

| Method | Line | Role | Target Grammar rule |
|--------|------|------|---------------------|
| `ParseExpression()` | 107 | product expr entry → `DslExpressionParser` | `expr` (wired in gpure-4) |
| `ParseEffect()` | 630 | effect entry — big first-token `if` ladder | `effect` head patterns (gpure-5) |
| `ParseConditionalEffect()` | 757 | `if/else if/else` | `effect` `if` head pattern + handler |
| `ParseCreateEffect()` | 787 | `create` / `create in` | `effect` create pattern + handler |
| `ParsePropertyInitializers()` | 811 | `{ name: expr, … }` block | handler block loop |
| `ParseSubscription()` | 823 | stage `when Rel Stage { … }` | `stage-body` subscription pattern + handler |
| `ParseSubscriptionQuantifier()` | 861 | `any`/`all` identifier-text | handler text check (F7) |
| `ParseEntitySubscription()` | 879 | entity-level `when` | `entity-body` subscription pattern + handler |
| `ParsePolicy()` | 1066 | policy body `{ expr }` | handler (expr via Grammar) |
| `ParseConstraint()` | 1135 | constraint RD (`required`, `unique`, `equals`, `range`, `length`, `pattern`) | **out of suite scope** — not covered by gpure tasks; stays RD (see scope note) |
| `ParseActionParameterList()` | 530 | `(name: Type, …)` | **out of suite scope** — stays handler |
| `ParseTypeName()` / `ParseAnnotation()` / `ParseNavLine()` | 1204/1083/996 | type names, annotation args, N1 navs | already Matcher-adjacent; stay handlers |

**Scope note:** The pure end-state (parent §3) targets expressions, effects, and open forms. Constraints, action params, annotations, and nav lines are **not** ported by the gpure suite — they remain handlers. If constraints later move to tables, that is a follow-up suite, not gpure.

### A3. `not` precedence (B3 probe)

`ParseNot` (DslExpressionParser.cs:52) parses its operand via **`ParseAdd()`** — one layer *below* comparison. Consequences (verified today):

- `not a > b` → `ParseNot` consumes `not a`, returns `Not(a)`; `> b` is left unconsumed → caller's `Expect(RBrace/…)` fails → **product rejects**.
- `not a + b` → binds as `not (a + b)`.

**Parity pin:** Grammar `expr-not` operand must be the **add layer**, and the parity harness (gpure-3/4) must assert `not a > b` **fails on both paths**. Do not "fix" `not` to bind over `>` — parity wins (no product test demands it).

---

## B. Already Matcher-driven

Grep: `MatchRule\(|TryMatch\(` — 9 hits.

| Site | Line | Rule(s) | Status |
|------|------|---------|--------|
| `DslExpressionParser.ParsePrimary` | DslExpressionParser.cs:128 | `expr-primary` | documentation first-token check only (`_ = MatchRule(...)`) — kept |
| `IDslParseCursor.MatchRule` | IDslParseCursor.cs:17 | interface | kept |
| `PolyDslParser` explicit `MatchRule` | PolyDslParser.cs:100 | explicit interface impl | kept |
| `Parse()` top-level dispatch | PolyDslParser.cs:127 | `top` | Matcher ✓ |
| `ParseEntity` body loop | PolyDslParser.cs:193 | `entity-body` | Matcher ✓ |
| `TryParseRegisteredAnnotation` | PolyDslParser.cs:424 | `annotation` | Matcher ✓ (with fail-closed fallback) |
| `ParseStage` body loop | PolyDslParser.cs:466 | `stage-body` | Matcher ✓ |
| `MatchRule` impl (dual-cursor) | PolyDslParser.cs:1227 | — | engine hook: Unread head → `TryMatch` → Read restore (**no Consume** — B1) |

---

## C. Grammar engine gaps + facts

Parent §4 gaps with pure-need marking:

| Gap | Needed for pure? | Proposed engine feature |
|-----|------------------|-------------------------|
| Recursive single rule ref | Y | `RuleRef<TKind>` element / `PatternBuilder.Rule(ruleName)` |
| Left-assoc binary chains | Y | `PatternBuilder.LeftAssoc(operandRule, opKinds)` |
| Nested language (expr inside effect) | Y (via RuleRef; B1 head/body split) | covered by RuleRef + handler loops |
| Semantic predicates after partial match | N (residual handlers OK) | — |
| Open pack literals (`12 days`) | Y (gpure-6 patterns) | `expr-primary` patterns + pack contributor |
| Error recovery / ExpectedTokens | N (deferred; see F8) | — |
| Print/parse symmetry | N (printer deferred — F9) | — |

### Engine facts (F4 — record, do not invent)

| Fact | Implication |
|------|-------------|
| `Matcher.TryMatch(rule)` uses **longest** match among the rule's patterns (Matcher.cs:35–47) | `RuleRef` must reuse longest-match selection relative to offset — **not** the `ManyOf` inner loop |
| `ManyOf` stops at the **first** sub-pattern that matches with count > 0 (Matcher.cs:161–186) | Do not copy ManyOf's loop into RuleRef |
| Zero-width match: a sub-match consuming 0 tokens must **fail** | Infinite-recursion guard for `Rule("empty")` shapes; `ManyOf` already guards via `subTokens.Count > 0` |
| Nested-span / dual-cursor: product `MatchRule` Unreads head, TryMatches, then **Reads restore without Consume** (PolyDslParser.cs:1227–1235) | A pattern that fully spans a nested Balanced body leaves the handler **without a live cursor inside the body** → B1 head/body split is mandatory for effects (gpure-5) |
| `Optional` succeeds with zero width | `Optional(RuleRef(empty))` inside `Many` is safe only via ManyOf's zero-width guard |
| Grammar sorts patterns by first-token kind then length descending (Grammar.cs `SortPatterns`) | Longer patterns win ties under longest-match; first-token sets stable for error messages |

### F8 — `ExpectedTokens` callers

`ExpectedTokens(...)` currently has **no callers** in `Poly/**` (only defined on `Matcher`, Matcher.cs:63). Extending `expr-primary` therefore changes no live error message today; gpure-8 step 6 still checks the guide if any message text shifts once the product error paths read expected tokens.

---

## D. File ownership map

| Area | Owner | Used by |
|------|-------|---------|
| Engine: element types, Matcher, Grammar builders, MatchResult, Printer | `Poly/Grammar/**` | gpure-1, gpure-2 |
| Engine docs | `Poly/Grammar/README.md` | gpure-1, gpure-2, gpure-8 |
| Engine tests | `Poly.Tests/Grammar/**` | gpure-1, gpure-2 |
| Product grammar tables | `Poly/DomainModeling/Parsing/DslGrammar.cs` | gpure-3, gpure-5 |
| Product expression parse | `Poly/DomainModeling/Parsing/DslExpressionParser.cs` | gpure-3 (read), gpure-4 (wire), gpure-7 (delete dual) |
| Product structure parser | `Poly/DomainModeling/Parsing/PolyDslParser.cs` | gpure-4 (wiring), gpure-5 (effects), gpure-7 |
| Open forms | `Poly/DomainModeling/Parsing/ExpressionFormRegistry.cs` | gpure-6 |
| Parity harness | `Poly.Tests/DomainModeling/Parsing/DslExprParityTests.cs` | gpure-3…7 |
| Docs | `docs/CORE.md`, READMEs, parent plan | gpure-8 |
| MCP catalog | `Poly.Mcp/**` | **do not edit** (suite-wide) |

---

## E. Product `not` precedence probe (B3)

Confirmed by reading `DslExpressionParser.ParseNot` (line 52): operand = **`ParseAdd()`**.

| Input | Product today | Grammar must |
|-------|---------------|--------------|
| `not x` | accept `Not(x)` | accept |
| `not a + b` | accept `Not(Add(a,b))` | accept, same shape |
| `not a > b` | **reject** (`> b` unconsumed → later Expect fails) | **reject** (do not bind `not` over comparison) |

Parity harness carries these three probes from gpure-3 onward; never "fix" `not` unless a failing product test forces it.

---

## Open forms (gpure-6)

- `IExpressionPrimaryForm` implementations: **only the test `MagicLiteralForm`** (`DslExpressionE1Tests`) — **no product forms yet**. The registry hook is a bridge for p1 temporal, not a product surface.
- `ExpressionFormRegistry.ContributeGrammarPatterns` runs at `DslGrammar.Build` via `PolyDslParser` ctor (line 86) — packs add patterns without editing core.
- **How temporal p1 should register (documented for the bridge):** `DomainInputBuilder.RegisterExpressionForm`/`ContributeGrammarPatterns`; register the unit pattern on **both** `expr-primary` and `expr-primary-no-not` (the comparison LHS uses the no-not primary — a pattern on `expr-primary` alone does not cover top-level unit expressions). Example test: `PackPattern_NumberUnit_ExtendsPrimarySurface` (DslExpressionE1Tests).
- Residual RD forms: none today. If p1 needs a form the engine cannot host, it must cite the missing engine feature (parent §4) before using opaque `IExpressionPrimaryForm`.

## Effects head/body split (B1 — filled by gpure-5)

| Effect | Head pattern matches | Body strategy |
|--------|----------------------|---------------|
| `assign` | `Assign` + prop + `To` (3 tokens) | handler: `ParseExpression` for RHS |
| `transition` | `Transition To StageName` (full statement) | handler builds IR from match tokens |
| `create` / `create-in` | `Create [In] RelName` head | handler: `Expect(LBrace)` + `ParsePropertyInitializers` |
| `delete` | `Delete` | handler |
| `invoke` | `Invoke` (head only) | handler: quantifier text, rel/action, args, `where` (F7 text predicates) |
| `if` | `If LParen` (head only) | handler: `ParseExpression` condition + then/else via `MatchRule("effect")` loops |

**Enforced:**
- No effect pattern Balanced-consumes a statement body (B1).
- No `when` pattern under `effect` (F7) — rejected in bodies with the original message.
- Entry: `MatchRule("effect")` first (PolyDslParser.cs:631); handlers consume the head and parse tails.
- IR data (stage name, prop name, rel name, type name) comes from `match.Tokens` — no re-parse.
