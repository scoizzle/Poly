# Micro-Task: APM.A5 — Codegen regression after merge

**Suite:** [`apm-README.md`](apm-README.md) **#A5**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §5 A5, §8  
**Difficulty:** Small  
**Estimated Context:** ~10k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** **A3** + **A4**

## Objective

Confirm file codegen still works fail-closed after the three passes leave the DslCompiler builder: AllMode smoke + generator structural suites green.

## Required Reading

- `Poly.Tests/DomainModeling/Lowering/SqlitePackTests.cs` — `DslCompiler_AllMode_EmitsDbContextAndProgramViaIr`  
- `DbContextGeneratorTests` / `MinimalApiGeneratorTests` filters  
- Parent §8 verification commands  

## Exact Steps

1. Run:
   ```bash
   dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*/*AllMode*'
   dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*DbContextGeneratorTests/*'
   dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*MinimalApiGeneratorTests/*'
   ```
2. Fix any fail-closed path that still reads behavior/aggregate only from `infraResult`.
3. Optional: one `CompileMode.Db` still produces storage without requiring MinimalApi behavior if that was prior behavior — do not change product contracts.
4. Do not expand Bar B oracles.

## Verification

- [ ] AllMode green  
- [ ] Generator suites green  
- [ ] Build green  

## Output

- Fixes if any  
- Note test counts/filters in Status notes  
- Status Done  

## Out of Scope

- Phase B diagnostics  
- HttpFile IR  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
