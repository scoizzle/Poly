# Micro-Task: Sever PolicyEvaluator from V2

**Parent**: WP1  
**Difficulty**: Small  
**Estimated Tokens**: ~4k  
**Status**: [x] **Done** — V2 import removed; grep gate clean (0 refs); dual-path Evaluate accepted per review note

## Objective

`Poly/DomainModeling/Lowering/PolicyEvaluator.cs` must not depend on `Poly.Data.Modeling`.

## Exact Steps (original — largely done)

1. Open `PolicyEvaluator.cs`; remove `using Poly.Data.Modeling`.
2. Ensure `Policy` / `DomainExpression` resolve to V3 types only.
3. Fix any compile breaks (ambiguous Policy, etc.).
4. Run existing DomainModeling lowering/VM tests; fix if needed.
5. Grep `Poly/DomainModeling` for `Poly.Data.Modeling` — zero hits.

## Code-review follow-ups (do these before marking Done)

1. **Re-run grep gate** — `Poly/DomainModeling/**` must have **zero** `Poly.Data.Modeling` references (confirm after other WP1 edits).
2. **Note only (no required code):** `Evaluate` still dual-runs LINQ + VM as a divergence oracle — acceptable for now; product path may prefer VM-only later (not blocking Done if documented in a one-line comment or left as-is).

## Verification

- [x] Build green; lowering tests pass (as of review)
- [ ] Grep gate re-confirmed clean after factory follow-ups
- [ ] Status → Done once grep clean

## Out of Scope

- New policy features; MCP
