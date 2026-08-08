# mcp-minify-3 — Unified `add` for core kinds

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1 `[x]` (task 2 preferred but not hard-required)  

## Objective

Register MCP tool **`add`** that creates domain elements for kinds:  
`entity`, `property`, `stage`, `action`, `stage_action`, `relationship`.

## Required reading

1. Parent plan §3.3 payload table  
2. Existing micro-tools in `Poly.Mcp/Tools/DomainTools.cs` — **copy Evolve patterns**, do not invent new evolution ops  
3. How `McpSessionStore.Evolve` is used by `AddEntity` / `AddProperty` / etc.  

## Exact steps

1. Add a new public static MCP tool in `DomainTools.cs` (or new `EvolveTool.cs` registered like other tool types — match existing registration in `Program.cs` / host):

```text
Name: "add"
Parameters:
  sessionId: string
  kind: string   // case-sensitive exact values below
  payload: string  // JSON object (structure fields only). Prefer string for MCP simplicity.
```

2. Parse `payload` with `System.Text.Json.JsonDocument` / `JsonSerializer`.  
   - Invalid JSON → Success: false, message contains `payload`.  
   - Unknown `kind` → Success: false, message lists allowed kinds for this task.

3. Dispatch (use **exact** existing EvolutionBuilder methods the old tools used):

| kind | Required JSON fields | Evolve (match existing tool) |
|------|----------------------|------------------------------|
| `entity` | `name` | `AddEntity(name)` |
| `property` | `entityName`, `name`, `typeName` | `AddPropertyToEntity(...)` same as `add_property` |
| `stage` | `entityName`, `name` | same as `add_stage` |
| `action` | `entityName`, `name` | same as `add_action` |
| `stage_action` | `entityName`, `stageName`, `name` | same as `add_action_to_stage` |
| `relationship` | `name`, `source`/`sourceEntityName`, `target`/`targetEntityName`, `cardinality` | same as `add_relationship` — accept either source naming; document one in Description |

4. Session missing → same error pattern as other tools.  
5. Evolution rollback → Success false + FailureSummary.  
6. **Do not** implement `policy` / `constraint` yet (task 4).  
7. **Do not** remove old tools yet (task 6).  
8. Tests: new class `Poly.Tests/Mcp/UnifiedAddTests.cs` (or under existing Mcp folder):

| Test | Expect |
|------|--------|
| `Add_Entity_Succeeds` | entity appears in overview/detail |
| `Add_Property_Succeeds` | property on entity |
| `Add_UnknownKind_Fails` | Success false |
| `Add_Property_MissingEntityName_Fails` | Success false |
| `Add_MissingSession_Fails` | Success false |

Use real session create helper patterns from `McpSmokeTests`.

9. Tool Description **must** include the kind list for this task and example payloads.

## Verification

```bash
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Tool name exactly `add` registered  
- [ ] Six kinds work; unknown kind fails  
- [ ] Old `add_entity` still registered (until task 6)  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Tools/DomainTools.cs` and/or new evolve tool file + host registration | Delete micro-tools |
| `Poly.Tests/Mcp/*` new tests | JsonParser delete unless zero callers |

## Status

**Status:** Not Started  
