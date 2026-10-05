# mut-safety-1 — Per-session write lock on Evolve

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 0  

## Objective

All `McpSessionStore.Evolve` (and any other mutate path that read-modify-writes session domain) serialize per `sessionId`. Concurrent evolves must not drop each other’s changes.

## Required reading

1. Inventory notes  
2. `Poly.Mcp` session store source (path from inventory)  
3. Parent plan Phase 1  

## Exact steps

1. Introduce per-session write serialization. Preferred shapes (pick one, document in notes):

   - `ConcurrentDictionary<string, object>` locks, or  
   - wrapper type holding `McpSessionState` + `object WriteLock` / `SemaphoreSlim(1,1)`  

2. **Every** mutate path that does Get→Evolve→Update must hold the lock for the whole critical section.  
3. **Read** tools (`TryGet` for get_*) stay **without** write lock.  
4. Do not change evolution domain logic.  
5. No public API break required beyond internal store.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Code review: Evolve cannot interleave two writers on same session  
- [ ] Suite green (task 2 adds the concurrent proof test)  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/**/McpSessionStore*.cs` (and related session files only) | DomainModeling evolution algorithms |
| Notes | Product domain types |

## Status

**Status:** Not Started  
