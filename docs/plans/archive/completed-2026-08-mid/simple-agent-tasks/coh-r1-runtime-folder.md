# COH-R1 — Extract Runtime/ folder

**Stream:** R  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06  
**Prereq:** COH-0

## Implementation notes

- `git mv` three files into `Poly/DomainModeling/Runtime/`: `DomainEntityInstance.cs`,
  `DomainInstanceStore.cs`, `InvocationResult.cs` (history preserved).
- **Namespace decision:** kept `Poly.DomainModeling` (folder-only move — coh-0 lock #2).
  Zero usings fallout; build clean with no edits to consumers (MCP tools, tests, exporters).
- Project includes are implicit (SDK-style globbing) — no csproj change needed.
- Docs updated (path-based references): `docs/interpretation/domain-execution-model.md`
  (2 refs) + `docs/PROJECT-SUMMARY-FOR-AGENTS.md` (1 ref). Archived review/agent docs left
  as historical snapshots. `Poly/DomainModeling/README.md` directory table now lists `Runtime/`.
  AGENTS.md placement table unchanged (coarse `Poly/DomainModeling/` row still holds).
- Verified: build 0 errors, 1855/1855 tests green (no behavior change).  

## Objective

Move `DomainEntityInstance`, `DomainInstanceStore`, and `InvocationResult` into `Poly/DomainModeling/Runtime/` (same assembly). Fix namespaces/usings. Behavior-preserving.

## Required reading

- Decomposition proposal Option C step 2  
- Current root files  

## Exact steps

1. Create `Runtime/` directory; move three files.  
2. Namespace `Poly.DomainModeling.Runtime` **or** keep `Poly.DomainModeling` with folder only — pick one, document in notes (prefer keep namespace to minimize churn unless clean).  
3. Fix project includes if explicit; update any path-based docs.  
4. Update DomainModeling README directory table + CORE placement one line if needed.  
5. Full build + tests.

## Verification

- [ ] Build green  
- [ ] Tests green (no intentional behavior change)  
- [ ] README lists Runtime/  

## File ownership

- **Move/edit:** the three runtime types + usings fallout  
- **Do not edit:** EffectAnalyzer logic, Evolution ApplyTo shapes, parser  

## Status

**Status:** Not Started  
