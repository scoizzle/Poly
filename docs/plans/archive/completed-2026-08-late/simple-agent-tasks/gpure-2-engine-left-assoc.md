# gpure-2 — Grammar engine: left-associative operator chains

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 1  

## Objective

Support left-associative binary expressions **in the engine** (e.g. `1 + 2 + 3`) without product RD while-loops.

## Required reading

1. `Poly/Grammar/Matcher.cs`  
2. Parent plan §4 precedence row  
3. How `DslExpressionParser` currently does `ParseAdd` / `ParseMultiply` (for semantic parity later)  

## Exact steps

1. Add an engine feature that expresses:

```text
leftAssoc(nextRule, operatorRuleOrKinds) 
→ parse nextRule, then while operator matches, parse nextRule again and fold left
```

**Preferred shape (pick one, document in inventory notes):**

- **Option A:** `PatternBuilder.LeftAssoc(string operandRule, params TKind[] opKinds)`  
- **Option B:** `LeftAssoc(string operandRule, string operatorRule)` where operatorRule matches one op token  

2. Match algorithm:
   - Match first operand via `Rule(operandRule)` semantics (reuse RuleRef).  
   - Loop: try match operator at current offset; if fail, done.  
   - Match next operand; if fail after op, whole LeftAssoc fails.  
   - Accumulate tokens (all operands + ops) for `MatchResult.Consumed`.  
   - **Do not** build DomainExpression here — only token consumption / match success. Folding IR is product handler responsibility **or** optional callback later; for pure match tests, success = full span matched.

3. **Critical (F5):** product will need op identity. `MatchResult` keeps a **flat** token list (op identity recoverable from kinds) — no nested span tree. That is enough for folding **only if** product uses **layer-by-layer MatchRule** (gpure-4 Option A). A single outer match + re-split flat tokens (Option B) loses nested-group structure — same class of problem as B1; do not design LeftAssoc assuming Option B.

4. README: document element.  

5. Tests:

| Test | Input | Expect |
|------|--------|--------|
| `LeftAssoc_AddChain_ConsumesAll` | `1 + 2 + 3` with Number/Plus grammar | match consumes all non-EOF |
| `LeftAssoc_SingleOperand` | `42` | match |
| `LeftAssoc_TrailingOp_Fails` | `1 +` | no match / fail |

Use a tiny test token kind enum in the test file (like existing GrammarMatcherTests).

6. Do **not** port product expr yet (task 3–4).

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Left-assoc feature green under Grammar tests  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/Grammar/**` | DomainModeling product port |
| `Poly.Tests/Grammar/**` | |

## Status

**Status:** Done 2026-08-07 — `LeftAssoc<TKind>` + `PatternBuilder.LeftAssoc(operandRule, params opKinds)`, flat token span incl. operators, trailing-op fails, zero-width-guarded operands; README row; 4 tests (`GrammarLeftAssocTests.cs` incl. nested operand rule). Suite 1905 green.  
