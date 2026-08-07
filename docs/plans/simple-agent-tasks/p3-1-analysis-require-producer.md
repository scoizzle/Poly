# P3-1 — Analysis: declared return requires producer

**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** P3-0  

## Objective

When an action declares `-> T` (non-void InvocationResult), analysis reports an **error** if no effect path can produce that result (e.g. no create / no assign to result channel — use inventory’s product rule). Fail closed.

## Required reading

- EffectAnalyzer / related codes  
- Export “return type” messaging if any  
- P3-0 chosen rule  

## Exact steps

1. Implement diagnostic (new or existing code).  
2. Tests: `-> Entity` with only `transition` → error; happy create/return path clean.  
3. Do not invent “last expression is return” without explicit product rule from inventory.

## Verification

- [ ] Fail-closed test green  
- [ ] Happy path no false error  

## File ownership

- Analysis + tests  
- **Do not edit:** multi-hop preprocess, temporal  

## Status

**Status:** Not Started  
