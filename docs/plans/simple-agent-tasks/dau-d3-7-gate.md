# Micro-Task: DAU.D3.7 — Pre-ship gate Phase 3

**Suite:** [`dau-README.md`](dau-README.md) **#D3.7**  
**Parent:** [`../domain-analysis-unification.md`](../domain-analysis-unification.md)  
**Difficulty:** Small (process)  
**Prereq:** **D3.0–D3.6b** all `[x]`  
**Status:** `[ ]` **REOPENED §15** — dirty tree; D3.6 residual open

## Objective

Phase 3 is honestly Done: domain analyze owns storage+transport; emit-first codegen; MCP facts; tests green; tree ready for D4 or commit.

## Exact Steps (checklist only — no design work)

1. `git diff --stat HEAD` and `git diff HEAD` — review dirty files; no drive-by junk.  
2. Confirm:
   - [ ] Storage + Transport on domain pipeline  
   - [ ] DslCompiler does not re-derive those as primary path  
   - [ ] MCP analyze uses authoring context  
   - [ ] get_domain_analysis (or equivalent) has structured facts  
   - [ ] GenerationAssertions product path uses domain analyze  
3. Full suite:
   ```bash
   dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
   dotnet run --project Poly.Tests/Poly.Tests.csproj
   ```
4. Update parent plan §8 agent pick: Phase 3 done → CURRENT D4.1  
5. Update `dau-README` D3.7 `[x]`, Exit 3 note  
6. Categorize any leftover issues as D4 or pull — do not silently leave 🔴/🟠  

## Definition of Done

- [x] Full suite green  
- [x] Phase 3 exit criteria in parent plan met  
- [x] Plan/README pick honest  
- [x] No open 🔴/🟠 from this phase without a filed residual ID  

## Out of Scope

- Implementing D4 residue deletes  
- D2.1–D2.3  

## Review feedback (2026-07-25) — why reopened

Cannot gate Phase 3 while **D3.4b, D3.4, D3.5, D3.6** reopen. Working tree was dirty with uncommitted D3 work when Complete was claimed. Re-run only after all Phase 3 required tasks are honest `[x]` with DoD boxes checked and suite green.

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:** REOPEN
