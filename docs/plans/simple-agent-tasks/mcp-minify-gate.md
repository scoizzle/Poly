# mcp-minify — Suite gate

**Difficulty:** S  
**Status:** `[x]`  
**Prereq:** tasks 0–7 all `[x]`

**Done 2026-08-08.**

## Gate results

1. **Grep gates** — all clean:
   - `DomainExpressionJsonParser`: zero matches (exit 1).
   - Per-type `McpServerTool(Name = "add_*"/"remove_*")`: zero matches (exit 1).
   - Unified `McpServerTool(Name = "add")` + `"remove"` present (DomainTools.cs).
2. **Build + full suite:** `dotnet build Poly.Benchmarks` clean; full suite **1927/1927 green** (two consecutive runs; one transient failure matched the known VM-execution flake, see `/memories/repo/flaky-vm-execution-tests.md`, green on re-run).
3. **Manual path checklist** (test-covered): create_domain_session → `CreateSession_ReturnsSessionIdAndBuiltins`; get_dsl_guide → `GetDslGuide_ReturnsProductSurface`; apply_dsl / add → `ApplyDsl_MinimalEntity_ReplacesSession` + `UnifiedAddTests.Add_Entity_Succeeds`/`Add_Property_Succeeds`; create_instance + invoke_action → `CreateInstance_SimpleEntity_ReturnsSnapshot` + `ApplyDsl_WithRequire_BlocksInvokeActionWhenPolicyFails`.
4. **pr1 pre-ship review:** dirty-tree audit done — 24 tracked files (22 modified + 2 deleted) + 5 new; reviewed DslExpressionFragment cursor (dual-cursor parity), Add/Remove dispatch (parse-time reject: bad JSON/unknown kind/missing field; analyze-time: evolution analysis gate; fail-closed: invalid policy DSL leaves no empty policy, JSON-bag expression rejected, constraint remove explicit not-supported), affordance strings, docs. No 🔴/🟠 findings.
5. Suite README status → **DONE** + date.
6. Parent plan §10 success checkboxes all ticked.
7. Nothing committed (human holds commit, per convention).  

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

> **P1 (2026-08-08, added after review):** tool-name deletions must ALSO grep the
> non-code surfaces the `*.cs` gates cannot see — suggestion hint strings, agent
> definitions, and active docs. Deleted-tool names in these surfaces fail the gate:
>
> ```bash
> # Dead tool names in product output / agent defs / docs (expect only negation/retirement contexts)
> rg -n "add_policy|add_entity|add_property|add_constraint|remove_policy|JSON expression" \
>   Poly.Mcp Poly/DomainModeling docs .github/agents --glob '*.{cs,md}'
> ```
>
> Every deletion must grep the **full tree including `.md` and `.agent.md`**, not just `*.cs`.
> Failure mode this catches (2026-08-08): `get_domain_suggestions` hint text and the
> `domain-modeling.agent.md` definition taught the deleted `add_policy`/JSON-bag surface while
> the `*.cs` gates stayed clean.

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

**Status:** Done  
