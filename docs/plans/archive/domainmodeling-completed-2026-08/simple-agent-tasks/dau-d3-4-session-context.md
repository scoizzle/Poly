# Micro-Task: DAU.D3.4 — Thread authoring context into MCP + evolution analyze

**Suite:** [`dau-README.md`](dau-README.md) **#D3.4**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.1** (API exists). Prefer **D3.2** done so session analyze actually gets Storage maps.  
**Status:** `[x]` — §14 verified Create path passes context

## Objective

Session analyze and evolution use the same `DomainAuthoringContext` as parse (MCP already has `McpSessionStore.Context = CreateWithSqlPack()`), so pack maps/conventions affect domain analysis when Storage is on the pipeline.

## Required Reading

1. `Poly.Mcp/Sessions/McpSessionStore.cs` — `Context`, `Analyze` call sites  
2. `Poly/DomainModeling/Evolution/DomainEvolution.cs` — where `DomainModelAnalyzer.Analyze` is called  
3. `DomainModelAnalyzer` overloads from D3.1  

## Exact Steps

1. **MCP:** Every `DomainModelAnalyzer.Analyze(...)` in session create/evolve paths passes `McpSessionStore.Context` (or session-level context if per-session exists — use the static Context if that is the product design today).
2. **DomainEvolution:** Add optional `DomainAuthoringContext? authoring = null` to the type or Analyze call sites:
   - Prefer constructor or property `AuthoringContext` default null.
   - When non-null, pass into both full and incremental Analyze overloads.
3. MCP evolution helpers that construct `DomainEvolution` set authoring from session Context.
4. Do **not** change MCP tool response shapes (D3.4b).
5. Smoke: existing MCP/domain tests still green; if no MCP tests, at least DomainEvolution with `CreateWithSqlPack()` Analyze does not throw.

## Definition of Done

- [ ] MCP analyze paths pass `DomainAuthoringContext` into `DomainModelAnalyzer`  
- [ ] DomainEvolution can carry/pass authoring into Analyze  
- [ ] Build green; relevant tests green  
- [ ] `dau-README` D3.4 `[x]`; CURRENT → D3.4b  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainEvolution*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*Mcp*'
```

## Out of Scope

- Extending `get_domain_analysis` payload (D3.4b)  
- DslCompiler  
- New MCP tools  

## Files expected

- `McpSessionStore.cs`  
- `DomainEvolution.cs` (and any Evolution applicator that analyzes)  
- Possibly MCP tool files that construct evolution  

## Review feedback (2026-07-25) — why reopened

**Partial only:**

| Path | Status |
|------|--------|
| `DomainEvolution(domain, McpAuthoring.Context)` on evolve/apply_dsl | ✅ |
| `McpSessionStore.Create` → `DomainModelAnalyzer.Analyze(domain)` | ❌ **no authoring context** |

**Required fix:** Session create (and any other bare `Analyze(domain)` in MCP) must use `DomainModelAnalyzer.Analyze(domain, McpAuthoring.Context)`. Re-check DoD box 1.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN — Create path missing context