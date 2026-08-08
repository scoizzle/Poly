# link-1 — `unlink_instances` MCP tool

**Suite:** [`dogfood-link-README.md`](dogfood-link-README.md)  
**Source findings:** S2-B1 (no unlink/reassign)  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

Added `UnlinkInstances` MCP tool (RuntimeTool) mirroring the existing `LinkInstances` pattern:

- Validates session, source instance, target instance, instance store, relationship existence, and entity type match (same validation as link_instances)
- Fail-closed: requires the link to exist before unlinking — calls `store.IsLinked()` first
- Removes the edge via `store.Unlink(relationshipName, source, target)` inside `McpSessionStore.TryModifyInstances`
- Returns success with `Unlinked '{source}' -/→ '{target}' via '{rel}'`
- 4 tests: happy path link→unlink, no-existing-link fails, unknown relationship fails, missing source fails

## Files changed

- `Poly.Mcp/Tools/RuntimeTool.cs` — added `UnlinkInstances` tool
- `Poly.Tests/Mcp/McpSmokeTests.cs` — 4 tests

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Total: 1624 passed (4 new).
