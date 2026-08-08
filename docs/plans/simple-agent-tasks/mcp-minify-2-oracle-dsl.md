# mcp-minify-2 — Oracle tools use DSL fragments only

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** task 1 `[x]`  

## Objective

Remove **all** `DomainExpressionJsonParser` usage from MCP **oracle** tools. Expression inputs are DSL fragments via task-1 API.

## Required reading

1. Inventory notes §A from task 0  
2. `Poly.Mcp/Tools/OracleTool.cs`  
3. Parent plan §3.1  

## Exact steps

1. In `OracleTool`, replace `TryParseExpression` / every `DomainExpressionJsonParser.ParseJson` with:

```csharp
DomainExpression expr = PolyDslParser.ParseExpressionFragment(expressionDsl, inputs: null);
// or helper name from task 1
```

2. Rename parameters from `expressionJson` → `expression` (or `expressionDsl`) in **public** MCP tool signatures.  
3. Update every `[Description(...)]` that says JSON / `{"property":...}` to say **DSL expression** e.g. `Age >= 18`, `(Total > 100) and (Status is Active)`.  
4. Tools in scope (all that currently take JSON expr):

   - `analyze_expression`  
   - `lower_expression`  
   - `lower_expression_to_csharp`  
   - `describe_expression`  
   - `simulate_policy`  

5. **Optional diet now (allowed):** delete any of the above tools entirely if no test references them after grep — prefer rewrite first unless tool is unused.  
   - **Must keep for later tasks:** none of these block `add` — deleting unused is OK.  
   - **Keep at least zero or one** expression oracle; if you keep only one, prefer `describe_expression` **or** `simulate_policy`.

6. Update all tests under `Poly.Tests` that call these tools with JSON strings → DSL strings.  
7. Do **not** implement unified `add`/`remove` yet.  
8. Do **not** delete `DomainExpressionJsonParser` if `add_policy` still calls it (task 4/6 will finish). If **no** callers remain after this task, you **may** delete the class + tests early.

```bash
rg -n "DomainExpressionJsonParser" --glob '*.cs'
```

## Verification

```bash
rg -n "DomainExpressionJsonParser|expressionJson|JSON policy expression|JSON expression string" Poly.Mcp --glob '*.cs'
# Expect: no JSON expression claims; DomainExpressionJsonParser only if add_policy still uses it
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj --no-build
```

- [ ] Oracle tools do not call `DomainExpressionJsonParser`  
- [ ] Descriptions do not document JSON IR  
- [ ] Suite green  

## File ownership

| Edit | Do not edit |
|------|-------------|
| `Poly.Mcp/Tools/OracleTool.cs` | Unified add/remove |
| `Poly.Tests/**` for oracle/smoke JSON expr | Delete all add_entity tools (task 6) |
| Optionally delete JsonParser if zero callers | |

## Status

**Status:** Not Started  
