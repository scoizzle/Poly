# Micro-Task: G6.5 — Optional: string Generate delegates to IR

**Suite:** [`ip-README.md`](ip-README.md) **#G6.5**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Low  
**Estimated Context:** ~8k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** G6.1–G6.3  

## Objective

Avoid dual maintenance: make `DbContextGenerator.Generate()` / `MinimalApiGenerator.Generate(...)` **thin wrappers** over IR + `CSharpGenerator`, so tests and production share one body.

## Required Reading

- Generator string + IR methods after G6.1/G6.2
- Tests that still call string `Generate()`

## Exact Steps

1. Implement string Generate as `return new CSharpGenerator().Generate(GenerateCompilationUnit(...));` (or shared private helper).
2. Re-run generator tests (string path may now match IR dialect — update asserts if they expected old string-only shapes).
3. If string path tests encode pre-IR oracle shapes that conflict with Bar A renorms, **prefer aligning tests to IR** over reintroducing dual bodies.
4. Document one line in NEXT: string Generate is IR-backed.

## Verification

- [ ] One implementation body for DbContext / MinimalApi emission
- [ ] Tests green
- [ ] No behavioral fork between production and tests  

## Output

- Generator edits + test updates if needed  

## Out of Scope

- HttpFile  
- Bar B anonymous objects  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
