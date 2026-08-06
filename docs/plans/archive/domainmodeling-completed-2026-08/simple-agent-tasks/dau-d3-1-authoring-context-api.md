# Micro-Task: DAU.D3.1 — Analyze + DomainAuthoringContext API

**Suite:** [`dau-README.md`](dau-README.md) **#D3.1**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.0 done** (StoragePass fail-closed)  
**Status:** `[x]` — completed 2026-07-25

## Objective

Add a first-class way to run domain analysis with an optional `DomainAuthoringContext` so later tasks can register pack-aware Storage without a second pipeline design. **This task does not yet register Storage/Transport on the domain pipeline** (D3.2/D3.3).

## Required Reading (only these)

1. `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs` — full file  
2. `Poly/DomainModeling/DomainAuthoringContext.cs` — TypeMaps, StorageConventions, Passes  
3. Parent plan §4 target shape (optional one skim)

## Exact Steps

1. Introduce a **pipeline factory** (private or internal) that can build the domain analyzer:
   - Prefer something like `BuildDomainAnalyzer(DomainAuthoringContext? authoring = null)` used by all public entry points.
   - Today: ignore authoring for pass list (same passes as now). Keep a single default cached analyzer when `authoring is null`.
   - When `authoring is not null`: either (a) build a non-cached analyzer per call, or (b) cache by a simple key if easy — **do not invent complex fingerprints**. Non-cached per call is OK for this task.
2. Add overloads:
   ```csharp
   public static AnalysisResult Analyze(Domain domain, DomainAuthoringContext? authoring);
   public static AnalysisResult Analyze(Domain domain, DomainAuthoringContext? authoring,
       AnalysisResult priorAnalysis, IEnumerable<Node> invalidatedNodes);
   ```
   Existing two overloads must keep working (delegate to `authoring: null` or keep cached path).
3. Do **not** change `DomainEvolution` or MCP in this task (D3.4).
4. Do **not** add StoragePass/TransportPass to the domain pipeline yet (D3.2/D3.3).
5. Add one test in `Poly.Tests/DomainModeling/Analysis/` (new or existing file):
   - `DomainModelAnalyzer_Analyze_WithNullAuthoring_MatchesParameterless` (or equal diagnostic count + non-null result on a tiny domain).
   - `DomainModelAnalyzer_Analyze_WithAuthoringContext_DoesNotThrow` using `DomainAuthoringContext.CreateWithSqlPack()` on a minimal domain.

## Definition of Done

- [x] Public overloads with `DomainAuthoringContext?` exist and compile  
- [x] Existing `Analyze(domain)` / incremental `Analyze` still work  
- [x] `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` green  
- [x] New tests green  
- [x] No Storage/Transport on domain pipeline yet  
- [x] No MCP/Evolution wiring yet  
- [x] Status `[x]` in `dau-README.md` Phase 3 table; agent pick CURRENT → D3.2  

## Verification commands

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DomainModelAnalyzer*'
# or filter your new test method names
```

## Out of Scope

- Registering Storage/Transport  
- Moving `StorageAnalyzer` file  
- MCP tools  
- DslCompiler changes  

## Files expected

- `Poly/DomainModeling/Analysis/DomainModelAnalyzer.cs`  
- `Poly.Tests/DomainModeling/Analysis/*` (one small test file or append)

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
