# COH-D1 — DomainExpression rewrites onto DomainExpressionDispatch

**Stream:** D  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** COH-0; **after R1 if both touch DomainEntityInstance**  

## Objective

Collapse near-duplicate full-tree expression rewrites (`BindPeerInExpression`, `PreprocessQuantifiers`) onto `DomainExpressionDispatch` (or shared rewrite base named by concern), leaf differences only at override points.

## Required reading

- `DomainExpressionDispatch.cs`  
- BindPeer / PreprocessQuantifiers in DomainEntityInstance  
- abstraction-gaps Finding 1 (partially shipped)  

## Exact steps

1. Identify shared composite reconstruction.  
2. Extract shared walker via dispatch base; overrides for peer bind vs quantifier preprocess leaves.  
3. Preserve fail-closed and empty-set semantics.  
4. Existing peer/quantifier tests green.

## Verification

- [ ] Behavior tests green  
- [ ] Single switch ownership for new DE subtypes in base  

## File ownership

- **Edit:** DomainEntityInstance rewrite methods + DomainExpressionDispatch (+ new helper file under Runtime/ if cleaner)  
- **Do not edit:** Evolution, EffectAnalyzer, parser  

## Status

**Status:** Not Started  
