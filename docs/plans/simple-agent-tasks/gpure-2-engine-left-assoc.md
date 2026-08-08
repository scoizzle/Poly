# gpure-2 — Grammar engine: left-associative operator chains

**Difficulty:** M  
**Status:** `[ ]`  
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

3. **Critical:** product will need op identity. Ensure `MatchResult` still exposes consumed tokens so handlers can rebuild the chain (existing MatchResult token list is enough).

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

**Status:** Not Started  
