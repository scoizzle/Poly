# Micro-Task: G6.3 — DslCompiler IR smoke (Db + All)

**Suite:** [`ip-README.md`](ip-README.md) **#G6.3**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Small  
**Estimated Context:** ~10k tokens  
**Status:** `[x]` Done — `DslCompiler_AllMode_EmitsDbContextAndProgramViaIr`  
**Prereq:** G6.1 + G6.2  

## Objective

One (or two) **compiler-level** tests prove production IR wire-up: `DslCompiler.Compile` with `CompileMode.Db` and **`CompileMode.All`** yields non-empty files with **structural markers** — not full string oracle.

**Shipped:** All-mode smoke in `SqlitePackTests` asserts domain-named DbContext, Program.cs, demo.http, structural markers (re-review G6′).

## Required Reading

- Existing compiler/pack tests: `Poly.Tests/DomainModeling/Lowering/SqlitePackTests.cs` (pattern)
- `src/Poly.DslCompiler/DslCompiler.cs` — public `Compile` API
- G6.1/G6.2 changes

## Exact Steps

1. Minimal domain (1–2 entities, one relationship if needed for API routes).
2. `Compile(..., CompileMode.Db)` → assert DbContext file present; contains `DbSet` / entity type name.
3. `Compile(..., CompileMode.All)` → assert Program.cs present; contains `Map` or route-ish marker already used in MinimalApi tests; demo.http still present.
4. Prefer TUnit next to existing DslCompiler/pack tests.
5. Fail loud if Success false — surface Errors.

## Verification

- [ ] Db + All smokes green
- [ ] Asserts structure, not full oracle equality
- [ ] Related generator suite still green

## Output

- New or extended test(s)
- Status Done  

## Out of Scope

- SequenceEqual vs old string Generate  
- Pack-specific SQL nuances beyond existing suite  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
