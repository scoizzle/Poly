# mcp-minify-5 — Unified `remove`

**Difficulty:** M  
**Status:** `[x]`  
**Prereq:** task 3 `[x]` (task 4 recommended)

**Done 2026-08-08:** `EvolveTool.Remove` (Name = "remove") — same kind enum as `add` (entity/property/stage/action/stage_action/relationship/policy) with identity-only payloads, dispatching to the exact existing remove EvolutionBuilder methods. `constraint` fails closed (`constraint remove not supported` — no remove_constraint evolution path; task inventory confirms no old tool). Unknown kind / missing field / bad JSON / missing session all fail closed. 8 new tests in `Poly.Tests/Mcp/UnifiedRemoveTests.cs`. Old remove_* tools still registered (task 6). Suite 1937 green.  

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
| `policy` | `entityName`, `name` (+ optional `stageName`/`actionName` scope, at most one) | `remove_policy` — scope wired 2026-08-08 follow-up B3 |
| `constraint` | **Fail closed**: `constraint remove not implemented in unified remove` — core removal is instance-identity-based (`ReferenceEquals`); use `apply_dsl` (B2). | no old remove_constraint |

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

**Status:** Done  
