# link-3 — Expose navigation property IDs in `get_instance`

**Suite:** [`dogfood-link-README.md`](dogfood-link-README.md)  
**Source findings:** S2-B3 (get_instance doesn't show nav values)  
**Difficulty:** Small  
**Status:** `[x]`

## What was done

`get_instance` now returns a `navigationLinks` field alongside scalar properties. Each entry contains:

- `relationshipName` — the domain relationship name (e.g. `"loans"`)
- `direction` — `"source→target"` or `"target→source"` depending on which side of the relationship the instance is on
- `linkedInstanceIds` — array of instance IDs linked via this relationship

**Implementation:** In `RuntimeTool.GetInstance`, after building the property snapshot, the code iterates all domain relationships where the instance's entity participates (as source or target), calls `state.InstanceStore.GetRelatedInstances(rel.Name, instance)`, and resolves each linked instance to its InstanceMap key.

**Empty state:** Instances with no links return `navigationLinks: []` (not null/absent).

**2 tests added:**
| Test | What it verifies |
|------|-----------------|
| `GetInstance_AfterLink_ShowsNavLink` | After link_instances, get_instance for both source and target shows the link with correct IDs |
| `GetInstance_WithoutLinks_NavsEmpty` | Freshly created instance has empty navigationLinks array |

## Files changed

- `Poly.Mcp/Tools/RuntimeTool.cs` — added `NavigationLinkData` record, nav population in `GetInstance`, empty navs in `BuildSnapshot`
- `Poly.Tests/Mcp/McpSmokeTests.cs` — 2 tests

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Total: 1628 passed (2 new).
