# mcp-minify-5 — Unified `remove`

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 3 `[x]` (task 4 recommended)  

## Objective

Register MCP tool **`remove`** with the same `kind` enum as `add`, payload = identity fields only.

## Required reading

1. Existing `remove_*` tools in `DomainTools.cs`  
2. Parent plan §3.3 remove column  

## Exact steps

1. Add tool **Name: `"remove"`**  
   Parameters: `sessionId`, `kind`, `payload` (JSON object string).

2. Dispatch:

| kind | Required payload fields | Evolve (match existing remove tool) |
|------|---------------------------|-------------------------------------|
| `entity` | `name` | `remove_entity` |
| `property` | `entityName`, `name` | `remove_property` |
| `stage` | `entityName`, `name` | `remove_stage` |
| `action` | `entityName`, `name` | `remove_action` |
| `stage_action` | `entityName`, `stageName`, `name` | `remove_action_from_stage` |
| `relationship` | `name` | `remove_relationship` |
| `policy` | `entityName`, `name` (+ optional scope fields if old tool had them) | `remove_policy` |
| `constraint` | **If no old remove_constraint exists:** fail closed with message `constraint remove not supported` **or** implement only if Evolution already supports it — do **not** invent evolution ops. Check inventory. |

3. Unknown kind / missing fields → Success false.  

4. Tests `Poly.Tests/Mcp/UnifiedRemoveTests.cs`:

| Test | Expect |
|------|--------|
| `Remove_Entity_Succeeds` | after add entity via `add` or bootstrap |
| `Remove_Property_Succeeds` | |
| `Remove_UnknownKind_Fails` | |
| `Remove_MissingSession_Fails` | |

5. Do **not** delete old remove_* tools yet (task 6).

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Tool `remove` registered  
- [ ] Core kinds green  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Tools/*` | Delete old tools (task 6) |
| `Poly.Tests/Mcp/*` | |

## Status

**Status:** Not Started  
