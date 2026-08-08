# gpure-3 — Product expression rules on DslGrammar

**Difficulty:** L  
**Status:** `[x]`  
**Prereq:** tasks 1–2  

## Objective

Define **product** expression pattern tables on `DslGrammar` using `Rule` + `LeftAssoc` (or equivalent). Do **not** delete RD yet — dual path OK until task 4 wires and proves parity.

## Required reading

1. `Poly/DomainModeling/Parsing/DslGrammar.cs`  
2. `Poly/DomainModeling/Parsing/DslExpressionParser.cs` (precedence order to mirror)  
3. Inventory notes proposed rule names  

## Exact steps

1. Extend `DslGrammar.Build` with rules (names may match; must be documented in notes):

```text
expr              → or-layer (top)
expr-or           → LeftAssoc(expr-and, Or | "or" keyword if needed)
expr-and          → LeftAssoc(expr-not-or-compare, And | "and")
… mirror product: not, comparison, add, mul, primary
expr-primary      → already partial; extend: group uses Rule("expr"), literals, ident
```

   **F8 — `ExpectedTokens` drift:** extending `expr-primary` (or any rule used by `ExpectedTokens(...)`) can change error messages. Note current callers of `ExpectedTokens("expr-primary")` in inventory notes; if messages change, update guide/tests in the same PR or track for gpure-8 step 6 — do not leave silent drift.

2. **Parity of precedence** with current `DslExpressionParser` (or/and/not/compare/add/mul). **Zero intentional differences** unless a product test already documents them.

3. **B3 — pin `not` (mandatory):**  
   Current product: `ParseNot` binds operand at **`ParseAdd`**, not comparison.  
   - Grammar must mirror that (not-operand = add-layer).  
   - Add parity probe (see §6): `not a > b` must **fail** (or match current product error) on **both** RD and Grammar paths — do **not** silently accept as `not (a > b)` unless product already does (it does not).

4. Keyword ops `and`/`or`/`is`/`not`: match **existing tokenizer** (`DslTokenKind`). Prefer Predicate on Identifier text if needed.

5. Comparison is **not** pure left-assoc (single compare). Model:  
   `expr-compare → expr-add [compareOp expr-add]?`

6. Quantifiers / path-prefix may remain handlers after `ident` primary if listed in inventory.

7. **B2 — start dedicated parity harness** (not “1–2 goldens”):

   Create `Poly.Tests/DomainModeling/Parsing/DslExprParityTests.cs` (name fixed).  
   Pattern: for each case, parse expression via **legacy RD entry still in tree** (or snapshot of current semantics) **and** via Grammar match + fold (once fold exists in gpure-4; for this task at minimum assert **token-span / accept-or-reject** parity).

   **Minimum corpus in this task** (accept/reject + span; expand IR equality in gpure-4):

| Case | Expect (product today) |
|------|-------------------------|
| `1 + 2 * 3` | accept full expr |
| `a and b or c` | accept (or-layer above and) |
| `Age >= 18` | accept |
| `not x` | accept |
| `not a > b` | **reject** / fail (B3 pin) |
| `1 +` | reject |
| `(1 + 2)` | accept |

   Keep harness **growing** through gpure-4…7 — do not delete cases.

8. Do **not** switch product `ParseExpression` yet (gpure-4).

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Rules exist in `DslGrammar`  
- [ ] `DslExprParityTests` exists with B3 + precedence cases  
- [ ] Full suite green (no product wire yet)  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DslGrammar.cs` | Delete DslExpressionParser |
| `Poly.Tests/**` expr grammar tests | Effect port |

## Status

**Status:** Done 2026-08-07 — expr rules on DslGrammar (expr/or/and/not/compare/add/mul/primary + `-no-not` comparison LHS for B3); `DslExprParityTests.cs` with 10 accept/reject+span cases; E1 group assertion updated to full-span. Suite 1915 green.  
