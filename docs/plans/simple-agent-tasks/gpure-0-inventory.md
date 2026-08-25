# gpure-0 — Inventory RD residual + Grammar gaps

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** none  

## Objective

Document every product parse path that is still recursive-descent language, and list Grammar engine gaps. **No production code.**

## Exact steps

1. Create `docs/plans/simple-agent-tasks/gpure-inventory-notes.md`.

2. Fill section **A. RD residual** by grepping and reading:

```bash
rg -n "private DomainExpression Parse|private Effect Parse|ParseOr|ParseAnd|ParsePrimary|ParseComparison|ParseMultiply|ParseNot|ParseConditionalEffect|ParseCreateEffect|ParsePropertyInitializers|ParseEffect" \
  Poly/DomainModeling/Parsing --glob '*.cs'
```

Table columns: Method | File | Role (expr layer / effect / other) | Target Grammar rule name (proposed).

3. Fill section **B. Already Matcher-driven**:

```bash
rg -n "MatchRule\(|TryMatch\(" Poly/DomainModeling/Parsing --glob '*.cs'
```

4. Fill section **C. Grammar engine gaps** — copy parent §4 and mark each:

| Gap | Needed for pure? (Y/N) | Proposed engine feature name |
|-----|------------------------|------------------------------|
| Recursive single rule ref | Y | `RuleRef` / `PatternBuilder.Rule` |
| Left-assoc binary chain | Y | `LeftAssoc` |
| … | | |

**C must also record these engine facts (review F4 — do not invent):**

| Fact | Implication for RuleRef / pure port |
|------|-------------------------------------|
| `TryMatch(rule)` uses **longest** match among patterns in the rule | `RuleRef` **must** reuse longest-match selection (same as `TryMatch`), **not** the `ManyOf` loop which takes **first** successful sub-pattern |
| `ManyOf` stops at first sub-pattern that matches with count > 0 | Do not copy ManyOf’s inner loop for RuleRef |
| Zero-width match | If a sub-match consumes **0** tokens → **fail** (infinite recursion guard) |
| Nested-span / dual-cursor | Product `MatchRule` Unreads head, TryMatches, then **Read restores head without Consume** — a pattern that fully spans nested Balanced body leaves the handler **without a cursor inside the body**. Record this for gpure-5 (B1). |

5. Fill section **D. File ownership map** for later tasks (who owns Grammar vs DslGrammar vs PolyDslParser).

6. Fill section **E. Product `not` precedence probe (B3)** — read `DslExpressionParser.ParseNot`:

   - Today: after `not`, operand is **`ParseAdd()`**, not comparison.  
   - So `not a > b` is **not** valid product parse today (compare binds outside / fails).  
   - Record exact expected behavior with one probe example for the parity harness (gpure-3).  
   - **Do not** “fix” `not` to bind tighter over `>` unless a failing product test forces it — parity wins.

7. Do **not** edit `Poly/**` code.

## Verification

- [ ] Notes file exists with A–E  
- [ ] §C includes longest-vs-first-match + zero-width + nested-span facts  
- [ ] §E pins `not` operand level  
- [ ] `git diff --stat` shows only docs under `simple-agent-tasks/`  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `gpure-inventory-notes.md` (new) | `Poly/**`, `Poly.Mcp/**` |

## Status

**Status:** Done 2026-08-07 — inventory notes at `gpure-inventory-notes.md` (A–E: RD residual, Matcher-driven, engine facts incl. longest-vs-first + zero-width + nested-span, ownership, `not` probe).  
