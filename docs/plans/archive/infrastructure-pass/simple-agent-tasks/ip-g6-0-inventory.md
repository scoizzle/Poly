# Micro-Task: G6.0 — Production IR inventory

**Suite:** [`ip-README.md`](ip-README.md) **#G6.0**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Small  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  

## Objective

Produce a short inventory of **where production still uses string `Generate()`** vs **where IR already exists**, so G6.1–G6.2 can wire without hunting.

## Required Reading

- `src/Poly.DslCompiler/DslCompiler.cs` — `GenerateAllFiles`
- `src/Poly.DslCompiler/DbContextGenerator.cs` — `Generate` + `GenerateCompilationUnit`
- `src/Poly.DslCompiler/MinimalApiGenerator.cs` — same
- `Poly.Tests/DomainModeling/Lowering/DbContextGeneratorTests.cs` — IR helper pattern
- `Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs` — IR helper pattern
- Renorm table: [`../infrastructure-pass-task-list.md`](../infrastructure-pass-task-list.md) (suite-wide renorm)

## Exact Steps

1. Table every production emit path in `GenerateAllFiles` (entities, DbContext, Program, http).
2. For each: string method vs `GenerateCompilationUnit` availability.
3. Note which tests already exercise IR (`CSharpGenerator().Generate(unit)`).
4. List **known renorm deltas** that production will inherit (BadRequest shape, switch → if, etc.).
5. Write findings into **Notes** below (or 10-line section on NEXT under G6.0). **No code change required.**

## Verification

- [ ] Table covers all `files.Add` paths in `GenerateAllFiles`
- [ ] G6.1/G6.2 targets identified (DbContext + MinimalApi)
- [ ] Explicit: HttpFile stays string unless later task

## Output

- Notes in this file Status tracking  
- Optional: one bullet list paste into NEXT if useful  

## Out of Scope

- Wiring production  
- Bar B  
- New Syntax nodes  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
