# mcp-minify — Suite gate

**Difficulty:** S  
**Status:** `[ ]`  
**Prereq:** tasks 0–7 all `[x]`  

## Objective

Prove the suite is done. No new features.

## Exact steps

1. **Grep gates** (all must be empty / clean):

```bash
# No JSON expression parser
rg -n "DomainExpressionJsonParser" --glob '*.cs'

# No per-type evolve tools registered
rg -n 'McpServerTool\(Name = "add_entity"|McpServerTool\(Name = "add_property"|McpServerTool\(Name = "add_stage"|McpServerTool\(Name = "add_action"|McpServerTool\(Name = "add_policy"|McpServerTool\(Name = "remove_entity"|McpServerTool\(Name = "remove_property"' Poly.Mcp --glob '*.cs'

# Unified tools present
rg -n 'McpServerTool\(Name = "add"|McpServerTool\(Name = "remove"' Poly.Mcp --glob '*.cs'
```

2. **Build + full suite:**

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

3. **Manual path checklist** (document results in progress notes):

   - create_domain_session  
   - get_dsl_guide  
   - apply_dsl minimal domain **or** add kind=entity + property  
   - create_instance + invoke_action if domain has action  

4. **Pre-ship review** per  
   `docs/plans/v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`  
   on dirty files. Fix 🔴🟠 only if found.

5. Update suite README status to **DONE** + date.  
6. Tick parent plan §10 success definition checkboxes.  
7. Commit only if user asked; otherwise leave tree for human.

## Verification

- [ ] All greps pass  
- [ ] Full suite green  
- [ ] pr1 complete or no 🔴🟠  
- [ ] Suite README Done  

## File ownership

| Edit | Do not edit |
|------|-------------|
| Suite status docs, pr1 notes if required | New product features |

## Status

**Status:** Not Started  
