# gpure — Suite gate

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** tasks 0–8 `[x]`  

## Objective

Prove pure Grammar product path. No new features.

## Exact steps

1. **Grep gates**

```bash
# No classic RD arithmetic loops in product parsing
rg -n "while \(_c\.Current\.Kind == TokenKind\.(Plus|Minus|Star|Slash)" Poly/DomainModeling/Parsing --glob '*.cs'

# Effect entry uses MatchRule
rg -n 'MatchRule\("effect"\)|TryMatch\("effect"\)' Poly/DomainModeling/Parsing --glob '*.cs'

# Engine features exist
rg -n "RuleRef|LeftAssoc|class Rule" Poly/Grammar --glob '*.cs'
```

2. **Build + full suite**

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

3. **pr1** pre-ship review on dirty tree; fix 🔴🟠.

4. Mark `gpure-README.md` **DONE** + date.  
5. Tick parent plan §8 success definition fully.  

## Verification

- [ ] Greps pass  
- [ ] Full suite green  
- [ ] pr1 clean  
- [ ] Suite Done  

## Status

**Status:** Not Started  
