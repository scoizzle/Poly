# p1 — Gate

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** tasks 0–6 `[x]`  

## Exact steps

1. Full suite green.  
2. Checklist from design lock negatives all covered by tests.  
3. Guide honest.  
4. pr1 pre-ship on dirty tree.  
5. Mark p1-README **DONE** + date; update parent design lock status to “suite complete” if desired.  
6. Do **not** start P9 schedule.

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

- [ ] All green  
- [ ] Suite Done  

## Status

**Status:** Not Started  
