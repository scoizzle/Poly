# Micro-Task: DAU.D3.5 — DslCompiler emit-first

**Suite:** [`dau-README.md`](dau-README.md) **#D3.5**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md) §5 Phase 3  
**Difficulty:** Medium  
**Prereq:** **D3.2** and **D3.3** (domain analyze already has Storage + Transport metadata)  
**Status:** `[x]` — §14 happy-path emit-first accepted; optional residual below

## Objective

`GenerateAllFiles` **reads** storage (and transport if needed) from the **domain** `analysis` argument. It does **not** re-run StoragePass/TransportPass as a second fact world. Pack `PassRegistry` only for true refinement passes that still need a small builder — default path is zero re-analyze.

## Required Reading

1. `src/Poly.DslCompiler/DslCompiler.cs` — `GenerateAllFiles` (~196–260)  
2. Confirm domain `analysis` already has StorageMappingMetadata after D3.2  

## Exact Steps

1. Prefer:
   ```csharp
   var storageModel = analysis.GetMetadata<StorageMappingMetadata>(domain)?.Storage;
   // optional: transport from analysis if any generator needs it later
   ```
2. Remove the second pipeline that only re-runs StoragePass + TransportPass **when** metadata is already present on `analysis`.
3. If pack `authoring.Passes` is non-empty and those passes **must** re-analyze: allow a **narrow** builder with `priorAnalysis: analysis` **only** for pack passes — document in a one-line comment. If pack passes currently only enrich storage and domain already ran Storage with same maps, skip re-run.
4. Fail-closed: still throw if db/all and storage null — message: domain analysis must include StoragePass.
5. Keep behavior/aggregate from domain `analysis` (already true).
6. Run AllMode / DbContext / MinimalApi tests (or leave explicit list to D3.6 if suite is long — **minimum**: build + one AllMode test).

## Definition of Done

- [ ] Happy path GenerateAllFiles does not construct StoragePass+TransportPass only to re-derive facts already on `analysis`  
- [ ] Fail-closed still throws when storage missing  
- [ ] Build green; `DslCompiler_AllMode_*` or equivalent green  
- [ ] `dau-README` D3.5 `[x]`; CURRENT → D3.6  

## Verification

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DslCompiler_AllMode*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*DbContext*'
```

## Out of Scope

- Changing IR shape of generators  
- HttpFile string → IR  
- RestApiSurfacePass  

## Review feedback (2026-07-25) — why reopened

**Partial only.** Current `GenerateAllFiles`:

1. Reads storage from domain `analysis` first ✅  
2. Still builds **second** `AnalyzerBuilder` with StoragePass + TransportPass when:
   - storage is null, **or**
   - `authoring.StorageConventions.Count > 0` (even if domain analysis already used the same conventions)
3. Stale comments: “TransportPass unused”, “CrossReference deferred” — wrong after DAU  
4. Fail-closed message still says “Infrastructure pipeline did not produce storage…”

**Required fix:**

- Happy path with complete domain analysis + null/empty pack refinements: **zero** second pipeline.  
- Re-run Storage **only** when maps/conventions differ from what domain analyze used (or document: callers must Analyze with same authoring first, then never re-run). Preferred: if `analysis` already has StorageMappingMetadata and authoring is null or already applied at analyze time, skip infra pipeline.  
- Prefer domain analysis run **with authoring** before GenerateAllFiles so re-run is unnecessary.  
- Delete stale Transport/CrossReference comments.  
- Fail-closed message: domain analysis missing StoragePass.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN — second pipeline still default for conventions