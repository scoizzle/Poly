# COH-E1 — EffectAnalyzer onto EffectDispatch (slice)

**Stream:** E  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** COH-0  

## Objective

Migrate **at least one major EffectAnalyzer walk** (or the main effect switch cluster) onto `EffectDispatch` so new effect subtypes fail at compile in the base Route. Full EffectAnalyzer rewrite not required — measurable reduction of multi-site switches.

## Required reading

- `EffectDispatch.cs`  
- `EffectAnalyzer.cs` switch density  
- Existing EffectLoweringPass as dispatch consumer example  

## Exact steps

1. Choose one coherent concern (e.g. validate create effects, or top-level effect routing).  
2. Subclass EffectDispatch; methods named by effect type.  
3. Wire analyzer to dispatch; preserve diagnostics.  
4. Tests for that concern green.

## Verification

- [ ] Chosen concern uses dispatch base  
- [ ] Effect binding tests green  

## File ownership

- **Edit:** EffectAnalyzer (+ EffectDispatch if abstract methods need expand) + tests  
- **Do not edit:** DomainEntityInstance runtime execute switch (unless trivial), Evolution  

## Status

**Status:** Not Started  
