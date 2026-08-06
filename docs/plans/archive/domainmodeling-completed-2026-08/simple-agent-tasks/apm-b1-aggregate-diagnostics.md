# Micro-Task: APM.B1 — Aggregate diagnostics (DMAGG001/002)

**Suite:** [`apm-README.md`](apm-README.md) **#B1**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §5 Phase B  
**Difficulty:** Medium  
**Estimated Context:** ~12k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** Phase A Gate complete  

## Objective

Add **warnings** on `OwnershipAggregatePass`:

- **DMAGG001** — non-root with no aggregate parent (possible orphan)  
- **DMAGG002** — `EntityStructureMetadata.IsRoot` ≠ aggregate root  

## Required Reading

- Parent Phase B table  
- `OwnershipAggregatePass.cs`  
- `DomainModelDiagnosticCodes.cs` (add constants)  
- How other passes `ReportWarning`  

## Exact Steps

1. Register codes in `DomainModelDiagnosticCodes`.  
2. After aggregate model built, emit warnings (not errors).  
3. Skip when `context.HasStructuralFailure`.  
4. Tests: fixture domains that trigger each code; assert diagnostic code present.  
5. Spot-check noise on a normal library-like domain (should be clean or justified).  

## Verification

- [ ] Codes appear in analysis result  
- [ ] Do not fail evolution (warnings only)  
- [ ] Suite green  

## Out of Scope

- Cycle diagnostics (B2)  
- Behavior hints (B3)  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
