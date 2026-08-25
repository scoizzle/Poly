# e2e-0 — Gate

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** 0-1 … 0-5 `[x]`  

## Exact steps

1. Grep inventories + guide: no live Delete/Link/Unlink/TransitionRelationship **Effect IR**.  
2. `delete` effect parse-fails closed.  
3. `CompileMode.All` XML honest.  
4. pr1 on dirty files for this slice.  
5. Mark e2e-0-README Done.

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Status

**Status:** Done  
**Claimed by:** opencode (fleet agent, e2e-0) — 2026-08-13  
**Verified:** greps clean (only "removed" notes); delete parse-fails closed; CompileMode.All honest; pr1 clean on slice files; build 0/0; 2065/2065 green
