# mut-safety — Gate

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** tasks 0–5 `[x]`  

## Exact steps

1. Full suite:

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

2. Confirm parent acceptance criteria from `mcp-mutation-safety.md`:

   - [ ] Parallel safety  
   - [ ] Idempotency + was_noop  
   - [ ] Rollback diagnostics  
   - [ ] Stage ordering  
   - [ ] No smoke regression  

3. pr1 pre-ship review on dirty tree.  
4. Mark suite README **DONE** + date.

## Status

**Status:** Not Started  
