# Micro-Task: G6.1 — DbContext production path uses IR

**Suite:** [`ip-README.md`](ip-README.md) **#G6.1**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** G6.0 recommended  

## Objective

`DslCompiler` emits **LibraryDbContext.cs** (or domain-named DbContext file) via  
`CSharpGenerator().Generate(dbGen.GenerateCompilationUnit(...))`  
instead of `dbGen.Generate()`.

## Required Reading

- `src/Poly.DslCompiler/DslCompiler.cs` — DbContext `files.Add` (~239–243)
- `src/Poly.DslCompiler/DbContextGenerator.cs` — both entry points
- `Poly.Tests/DomainModeling/Lowering/DbContextGeneratorTests.cs` — IR helper + renorm expectations
- G6.0 notes if present

## Exact Steps

1. In `GenerateAllFiles`, replace string `dbGen.Generate()` with IR path matching tests.
2. Keep namespace / file naming behavior stable unless broken (match existing file name).
3. Run DbContext generator tests (string + IR if both exist).
4. Spot-check one `CompileMode.Db` result contains expected markers (`DbSet`, entity names) — formal smoke is G6.3.
5. Do **not** delete string `Generate()` yet (G6.5 optional) unless it becomes a pure wrapper in this PR.

## Verification

- [ ] Production path calls `GenerateCompilationUnit` + `CSharpGenerator`
- [ ] `DbContextGeneratorTests` green
- [ ] Build green
- [ ] No new analyzer fallbacks

## Output

- Code change in `DslCompiler` (+ generator only if needed)
- Status Done here  

## Out of Scope

- MinimalApi wire-up (G6.2)  
- Bar B parity  
- HttpFile IR  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
