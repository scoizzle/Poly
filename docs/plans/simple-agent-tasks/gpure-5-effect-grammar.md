# gpure-5 — Effect statement grammar + wire

**Difficulty:** L  
**Status:** `[x]`  
**Prereq:** task 4  

## Objective

Effect **statement selection** is Matcher-driven. Handlers produce existing `Effect` IR **without needing a cursor inside a already-consumed Balanced body**.

## B1 — Nested-span IR (blocking lock)

**Problem:** Product `MatchRule` restores the head token after TryMatch **without Consume**. If an effect pattern uses `Balanced(LBrace,RBrace)` (or otherwise spans the whole body), the match reports success but the handler never walks the body with a live cursor — IR cannot be built from “inside” the span.

**Required design (do not violate):**

| Layer | What the `effect` pattern matches |
|-------|-----------------------------------|
| **Head only** | Keyword + fixed syntax through end of **condition / target header** (not the body block contents) |
| **Bodies** | After dispatch, handler expects `{` and loops **`MatchRule("effect")` + Consume + handle** until `}` — each nested statement is table-selected again |

Examples:

- `if`: pattern matches `if ( … )` only (parens for condition via Balanced **on the condition** or Rule expr — **not** the then/else statement blocks). Then handler parses then-stmt (single effect or block of effects via loop).  
- `assign`: pattern may match `assign` + path through `to` + start of expr, **or** only `assign` keyword then handler expects rest — prefer enough head for unambiguous dispatch; expr via Grammar expr parse.  
- Block of effects (entry/exit/subscription): **do not** one-shot Balanced-match the whole block as a single effect pattern for IR; open brace then loop effect rules.

Document the chosen head/body split for each effect kind in `gpure-inventory-notes.md` §Effects.

## Exact steps

1. Add `DslGrammar` rule `effect` with **head-only** patterns:

| Pattern | Matches (head) | Body strategy |
|---------|----------------|---------------|
| `assign` | `assign` … (through clear dispatch point) | handler + expr |
| `transition` | `transition` … | handler |
| `create` / `create-in` | `create` [`in` …] | handler + block fields |
| `delete` | `delete` | handler |
| `invoke` | `invoke` … | handler |
| `if` | `if` + condition group only | then/else via effect loop / single effect |

   **F7 — text predicates, not new kinds:**
   - **`when` must stay rejected inside effect/action bodies** (product error like “Unexpected 'when' inside action body”). Do **not** add a `when` pattern under `effect` that accepts it.
   - `invoke any` / `all` / `where` and subscription quantifiers match **Identifier text** (or existing token text), not new `DslTokenKind`s — use `MatchPredicate` / predicate elements on Identifier where product already does.

2. Entry `ParseEffect`:

```text
match = MatchRule("effect")  // head
if null → fail
switch pattern → Handle*(…)  // may Consume(match) then parse tails
```

3. Nested lists: `while (MatchRule("effect") is match) { Consume; Handle }` until fail, then expect `}`.

4. **Forbidden:** effect pattern that `Balanced`-consumes then/else bodies and expects handler to re-enter that span without a span-IR API.

5. Expand parity if effects affect expr only via conditions — rely on product effect corpus + add:

   - `Effect_If_ThenAssign_RoundTrip` or existing apply_dsl goldens  
   - Unit: if-head match does **not** require body tokens present for pattern name (optional)

6. **F6 — fail-closed negatives** (at least 2–3; existing product checks must survive):

   | Case | Expect |
   |------|--------|
   | `assign x to` (missing expr) | fail loud |
   | `if (x) {` unterminated | fail loud |
   | `invoke any Action` (if product rejects today) | still fail — local fail-closed must survive rewrite |

7. Grep: effect entry dispatches via MatchRule first.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
rg -n 'MatchRule\("effect"\)|TryMatch\("effect"\)' Poly/DomainModeling/Parsing --glob '*.cs'
```

- [ ] B1 head/body split documented and implemented  
- [ ] No Balanced-full-body effect patterns for IR building  
- [ ] F7: `when` still rejected in effect bodies; quantifiers via text predicate  
- [ ] F6 fail-closed negatives present  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `DslGrammar.cs`, `PolyDslParser.cs` effects | Delete expr RD dual (gpure-7) |
| inventory notes §Effects | Temporal product |

## Status

**Status:** Done 2026-08-07 — `effect` head-pattern rule on DslGrammar; `ParseEffect` entry is `MatchRule("effect")` + switch; `if`/`invoke` tails refactored to `ParseIfEffectCore`/`ParseInvokeEffectTail`; B1 head/body split enforced; F7 `when` rejection preserved; 4 effect grammar tests incl. fail-closed negatives. Suite 1927 green.  
