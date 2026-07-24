# Micro-Task: G6.2 — MinimalApi production path uses IR

**Suite:** [`ip-README.md`](ip-README.md) **#G6.2**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Medium  
**Estimated Context:** ~14k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** G6.1 preferred (same pattern)  

## Objective

`DslCompiler` emits **Program.cs** via  
`CSharpGenerator().Generate(apiGen.GenerateCompilationUnit(dbContextName))`  
instead of `apiGen.Generate(dbContextName)`.

## Required Reading

- `src/Poly.DslCompiler/DslCompiler.cs` — MinimalApi `files.Add` (~246–251)
- `src/Poly.DslCompiler/MinimalApiGenerator.cs` — `Generate` + `GenerateCompilationUnit`
- `Poly.Tests/DomainModeling/Lowering/MinimalApiGeneratorTests.cs` — Bar A renorms
- Renorm table in task-list

## Exact Steps

1. Wire production path to IR for Program.cs only.
2. Accept Bar A renorms (bare error strings, if/else vs switch, Concat Created, StatusCode(500)).
3. Keep `HttpFileGenerator.Generate()` on string path.
4. Run MinimalApi generator tests.
5. Do not expand to RestApiSurfacePass.

## Verification

- [ ] Production Program.cs from IR
- [ ] MinimalApi tests green
- [ ] Build green
- [ ] HttpFile still string

## Output

- Code change in `DslCompiler`
- Status Done  

## Out of Scope

- Dual-oracle Bar B  
- Http IR  
- New endpoints / RestApiSurfacePass  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
