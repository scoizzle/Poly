# COH-V1 — Evolution mutation helpers

**Stream:** V  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** COH-0  

## Objective

Deduplicate near-identical list replace patterns in `DomainMutationContext` / `DomainChange.ApplyTo` via shared helpers (e.g. `ReplaceInList`, shared entity nested update shapes). No new evolution framework.

## Required reading

- abstraction-gaps Finding 2  
- `DomainMutationContext.cs`, `DomainChange.cs`  

## Exact steps

1. Identify 3+ near-identical Update* patterns.  
2. Extract private/shared helpers.  
3. Route existing methods through helpers.  
4. Evolution/apply tests green; fail-loud zero-match preserved.

## Verification

- [ ] Evolution tests green  
- [ ] No API break for public fluent surface (or intentional + tests)  

## File ownership

- **Edit:** Evolution/* mutation types only  
- **Do not edit:** Analysis, Runtime, Parsing  

## Status

**Status:** Not Started  
