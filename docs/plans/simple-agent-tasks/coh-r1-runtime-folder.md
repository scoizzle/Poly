# COH-R1 — Extract Runtime/ folder

**Stream:** R  
**Difficulty:** M  
**Status:** `[ ]`  
**Prereq:** COH-0  

## Objective

Move `DomainEntityInstance`, `DomainInstanceStore`, and `InvocationResult` into `Poly/DomainModeling/Runtime/` (same assembly). Fix namespaces/usings. Behavior-preserving.

## Required reading

- Decomposition proposal Option C step 2  
- Current root files  

## Exact steps

1. Create `Runtime/` directory; move three files.  
2. Namespace `Poly.DomainModeling.Runtime` **or** keep `Poly.DomainModeling` with folder only — pick one, document in notes (prefer keep namespace to minimize churn unless clean).  
3. Fix project includes if explicit; update any path-based docs.  
4. Update DomainModeling README directory table + CORE placement one line if needed.  
5. Full build + tests.

## Verification

- [ ] Build green  
- [ ] Tests green (no intentional behavior change)  
- [ ] README lists Runtime/  

## File ownership

- **Move/edit:** the three runtime types + usings fallout  
- **Do not edit:** EffectAnalyzer logic, Evolution ApplyTo shapes, parser  

## Status

**Status:** Not Started  
