# gpure-3 — Product expression rules on DslGrammar

**Difficulty:** L  
**Status:** `[ ]`  
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

2. **Parity of precedence** with current `DslExpressionParser` (or/and/not/compare/add/mul). Document any intentional difference (there should be none).

3. Keyword ops `and`/`or`/`is`/`not`: today often `Identifier` text or dedicated kinds — match **existing tokenizer** (`DslTokenKind`). Do not change tokenizer keywords unless tests force it; prefer Predicate on Identifier text if needed.

4. Comparison is **not** pure left-assoc chain in current product (single compare). Model as:  
   `expr-compare → expr-add [compareOp expr-add]?`  
   Use optional second half pattern or two patterns (with-op / bare).

5. Quantifiers / path-prefix / related-access remain **handlers** after matching `ident` primary for now if too hard — **allowed residual** if listed in notes under “still handler after primary match”. Goal of this task is binary layers + primaries on the table.

6. Tests `Poly.Tests/Grammar/DslExprGrammarTests.cs` (or DomainModeling test folder):

| Test | Expect |
|------|--------|
| `ExprGrammar_AddMul_Precedence_TokenSpan` | Matcher on `expr` consumes `1 + 2 * 3` fully |
| `ExprGrammar_AndOr_Consumes` | `a and b or c` full span (per product precedence) |
| `ExprGrammar_Compare_Consumes` | `Age >= 18` |

7. Do **not** switch product `ParseExpression` yet.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Rules exist in `DslGrammar`  
- [ ] New tests green  
- [ ] Full suite green (no product wire yet)  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/DomainModeling/Parsing/DslGrammar.cs` | Delete DslExpressionParser |
| `Poly.Tests/**` expr grammar tests | Effect port |

## Status

**Status:** Not Started  
