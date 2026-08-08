# gpure-1 — Grammar engine: single recursive rule reference

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 0  

## Objective

Add a first-class pattern element so a pattern can invoke **exactly one** match of a named rule (recursive / nested languages). Today only `Many(ruleName)` exists.

## Required reading

1. `Poly/Grammar/PatternElement.cs`  
2. `Poly/Grammar/Matcher.cs` (`TryMatchElement`, `ManyOf`)  
3. `Poly/Grammar/Grammar.cs` (PatternBuilder fluent API)  
4. Inventory notes §C  

## Exact steps

1. Add element type e.g. `RuleRef<TKind>` (name for what it is: reference to a named rule) implementing `IPatternElement<TKind>`.  
2. Match semantics (must match tests) — **F4 lock:**
   - At offset, run the **same longest-match selection as `TryMatch(ruleName)`** relative to that offset (peek-based like existing elements).  
   - **Do not** reuse `ManyOf`’s “first sub-pattern wins” loop.  
   - On success, consume exactly the tokens of that sub-match.  
   - On failure, element fails (pattern fails).  
   - **Zero-width guard:** if sub-match consumes **zero** tokens → treat as **failure** (no infinite recursion).  
3. Type name: **`RuleRef<TKind>`** (element). Fluent: `PatternBuilder.Rule(string ruleName)` → appends `RuleRef`.  
4. Document in `Poly/Grammar/README.md` pattern elements table — name the element **RuleRef** / builder **Rule**.  
5. Add unit test that longest-match is used when two patterns in the referenced rule share a prefix (longer wins) — proves not ManyOf-first.  
6. Tests in `Poly.Tests/Grammar/`:

| Test | Expect |
|------|--------|
| `RuleRef_NestedGroup_Matches` | Grammar: `primary` = Number \| LParen + Rule("expr") + RParen; `expr` = Rule("primary"); input `(1)` matches |
| `RuleRef_MissingInner_Fails` | incomplete group → no match / fail |
| `RuleRef_ZeroWidth_Fails` | empty / zero-consume cannot recurse forever |
| `RuleRef_LongestMatch_NotFirstMatch` | two patterns share prefix; longer wins |

7. Do **not** change DomainModeling product parser yet.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build -- --treenode-filter '/*/Poly.Tests.Grammar/*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] `Rule` / `RuleRef` public API  
- [ ] Grammar tests green  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly/Grammar/**` | `Poly/DomainModeling/**` |
| `Poly/Grammar/README.md` | MCP |
| `Poly.Tests/Grammar/**` | |

## Status

**Status:** Done 2026-08-07 — `RuleRef<TKind>` + `PatternBuilder.Rule(ruleName)`, longest-match + zero-width guard, README row, 5 tests (`GrammarRuleRefTests.cs` incl. longest-not-first + many-zero-width no-hang). Suite 1901 green.  
