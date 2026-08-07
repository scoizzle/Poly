# COH-E1 — EffectAnalyzer onto EffectDispatch (slice)

**Stream:** E  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06  
**Prereq:** COH-0

## Implementation notes

- Migrated the **main per-effect validation walk** (`ValidateEffect`) in
  `EffectAnalyzer` onto `EffectDispatch<object?>`: new private nested class
  `EffectValidationDispatch` with methods named by effect type
  (CreateEntityInstance, CreateEntityInRelationship, StageTransition,
  InvokeAction, Assign, DeleteEntity, LinkRelationship, UnlinkRelationship,
  TransitionRelationship, Conditional, Composite).
- All 11 dispatch cases preserve the exact prior diagnostics (EffectBinding,
  EffectNotExecutable, NestedDirectEffectDropped, plus per-effect validators);
  Conditional/Composite recurse via `Route` → `ValidateEffects`.
- New effect subtypes now fail loud in the base `EffectDispatch.Route` switch
  (`_ => throw NotSupportedException`) instead of silently passing through an
  analyzer switch.
- Verified: build 0 errors, 1855/1855 tests green (behavior preserved).  

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
