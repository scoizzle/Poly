# gpure-1 — Grammar engine: single recursive rule reference

**Difficulty:** M  
**Status:** `[ ]`  
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
2. Match semantics (must match tests):
   - At offset, run the same longest-match selection as `TryMatch(ruleName)` **relative to that offset** (peek-based like existing elements).  
   - On success, consume exactly the tokens of that sub-match.  
   - On failure, element fails (pattern fails).  
   - Must not infinite-loop: if sub-match consumes **zero** tokens, treat as failure.  
3. Fluent API: `PatternBuilder.Rule(string ruleName)` → appends `RuleRef`.  
4. Document in `Poly/Grammar/README.md` pattern elements table.  
5. Tests in `Poly.Tests/Grammar/` (new or extend existing):

| Test | Expect |
|------|--------|
| `RuleRef_NestedGroup_Matches` | Grammar: `primary` = Number \| LParen + Rule("expr") + RParen; `expr` = Rule("primary"); input `(1)` matches |
| `RuleRef_MissingInner_Fails` | incomplete group → no match / fail |
| `RuleRef_ZeroWidth_Fails` | empty pattern cannot recurse forever |

6. Do **not** change DomainModeling product parser yet.

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

**Status:** Not Started  
