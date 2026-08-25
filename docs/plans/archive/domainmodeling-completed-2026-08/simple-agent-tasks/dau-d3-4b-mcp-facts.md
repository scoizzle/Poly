# Micro-Task: DAU.D3.4b — MCP structured facts from LatestAnalysis

**Suite:** [`dau-README.md`](dau-README.md) **#D3.4b**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §12  
**Difficulty:** Medium  
**Prereq:** Domain pipeline already produces OwnershipAggregate + Behavior + EffectTopology (true today). D3.2/D3.3 nice-to-have for storage/transport fields.  
**Status:** `[x]` — AnalysisData extended; MCP smoke test asserts structured fields

## Objective

Agents can read **structured** hierarchy / topology / behavior facts already on `LatestAnalysis` without re-parsing DSL. **No second store.** Prefer extending `get_domain_analysis` / `AnalysisData` over inventing many tools.

## Required Reading

1. `Poly.Mcp/Tools/DomainTools.cs` — `AnalysisData`, `get_domain_analysis` implementation (~89–260)  
2. Metadata types: `OwnershipAggregateMetadata`, `BehaviorMetadata`, `EffectTopologyMetadata` under `Poly/DomainModeling/Analysis/`  
3. Do **not** invent RestApi fields  

## Exact Steps

1. Extend `AnalysisData` (or nested records) with **optional** structured summaries, e.g.:
   - `IReadOnlyList<string> AggregateRoots` (or list of `{ name, isRoot, parent }`)
   - Topology: counts or short lists (create-in count, subscription count) — keep small  
   - Behavior: per-entity action name lists (cap size if needed, e.g. max 50 actions total)  
   - If D3.2/D3.3 done: optional flags `HasStorageMapping`, `HasTransport` booleans only (full models too large for MCP)
2. Populate from `state.LatestAnalysis.GetMetadata<...>(domain)` when non-null; omit/empty when missing.
3. Keep existing diagnostic fields (error/warning/hint counts, messages).
4. Update tool description string for `get_domain_analysis` to mention structured facts.
5. Test: unit or MCP smoke that after analyze of a two-entity hierarchy domain, roots/children or action names appear in the structured payload (not only diagnostics).

## Definition of Done

- [x] `get_domain_analysis` returns structured fields derived from LatestAnalysis metadata  
- [x] No parallel fact cache / no re-run of full domain analyze inside the tool beyond reading LatestAnalysis  
- [x] Build + test green  
- [x] `dau-README` D3.4b `[x]`; CURRENT → D3.5  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*domain_analysis*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*GetDomainAnalysis*'
```

## Out of Scope

- New MCP tool names unless extending AnalysisData is impossible (prefer extend)  
- Full StorageModel dump  
- RestApi  

## Review feedback (2026-07-25) — why reopened

**Claimed Done with DoD checkboxes still empty.** Code check:

- `AnalysisData` only has: error/warning/info/hint counts, structural failure, messages, **entityCount**, **relationshipCount**.
- `GetDomainAnalysis` still builds that payload only — **no** roots/parents, topology summary, or behavior action names from `LatestAnalysis.GetMetadata`.
- Tool description still says diagnostics-only.

**Required fix:** Implement Exact Steps for real. Check every DoD box only after code + test prove structured metadata projection.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN — not implemented