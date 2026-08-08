# COH-D1 — DomainExpression rewrites onto DomainExpressionDispatch

**Stream:** D  
**Difficulty:** M  
**Status:** `[x]` — DONE 2026-08-06  
**Prereq:** COH-0; after R1 (R moved DomainEntityInstance first — done)

## Implementation notes

- New `Poly/DomainModeling/Runtime/DomainExpressionRewriteBase.cs` — shared full-tree
  rewrite: composite nodes recurse via `Route` (single switch ownership for the
  hierarchy), leaves identity by default, `Default()` throws fail-loud on unhandled
  new subtypes.
- `BindPeerInExpression` → `PeerBindingRewrite` (private nested class): single leaf
  override for `RelationshipNavigation` (peer-root match → literal via
  `EvaluateExprOnPeer`); composites handled by the base.
- `PreprocessQuantifiers` → `QuantifierPreprocessRewrite` (private nested class,
  lazy `QuantifierRewrite` field): leaf overrides for Any/All/None/Count (→ literal),
  `RelationshipNavigation` (store fail-closed to-one resolve — passes preprocessed
  `TargetProperty` to `EvaluateBodyOnTarget`, matching original semantics), and
  Exists/NotExists (relationship-presence → literal, else base recursion).
- Caught during verification: passing `base.RelationshipNavigation(r)` (whole nav)
  to `EvaluateBodyOnTarget` regressed 4 to-one path-prefix policy tests (nav node
  re-lowered against target bag → false). Fixed by passing `Route(r.TargetProperty)`
  only. Full suite green.
- Verified: build 0 errors, 1855/1855 tests green (behavior preserved).  

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
