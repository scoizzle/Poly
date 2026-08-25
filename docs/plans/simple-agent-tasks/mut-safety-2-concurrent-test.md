# mut-safety-2 — Concurrent evolve proof test

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1  

## Objective

Automated test proves two parallel structural mutations on one session both land (or second waits), **never** silent loss of one property/entity.

## Exact steps

1. Add test class e.g. `Poly.Tests/Mcp/SessionMutationSafetyTests.cs`.

2. Test `Concurrent_TwoAdds_BothVisible` (name flexible if TUnit needs):

   - Create session.  
   - Ensure entity exists (bootstrap + add entity via current MCP API: after minify, `add` kind=entity; before minify, `add_entity` — **use whatever is registered**).  
   - Start **two** parallel tasks/threads each adding a **different** property (e.g. `A`/`B` Text) via Evolve tool.  
   - Join both.  
   - Assert both Success (or document if one retries).  
   - Assert entity detail / domain has **both** properties.

3. If flake: increase iterations (e.g. 20 parallel pairs) until failure would be deterministic without lock.

4. Optional: `Concurrent_ManyEvolves_NoLostUpdate` with N=10 sequential-properties race.

## Verification

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build -- --treenode-filter '/*/*/*Concurrent*'
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Concurrent test green  
- [ ] Full suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Tests/Mcp/*` | DomainModeling |

## Status

**Status:** Not Started  
