# Micro-Task: APM.A1 — Metadata bridge for Aggregate/Behavior

**Suite:** [`apm-README.md`](apm-README.md) **#A1**  
**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md) §4  
**Difficulty:** Medium  
**Estimated Context:** ~14k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** none (must be first)

## Objective

Teach `AggregateAnalyzer` and `BehaviorAnalyzer` to read analysis metadata from a live **`AnalysisContext`** (or equivalent facade) so in-pipeline passes do not need a frozen `AnalysisResult` ctor — **without** silent root/policy regression when context is null.

## Required Reading

- Parent §4 (critical bridge) — only that section if short on context  
- `Poly/DomainModeling/Lowering/AggregateAnalyzer.cs` — `_analysis` / `IsRootEntity`  
- `Poly/DomainModeling/Lowering/BehaviorAnalyzer.cs` — effective policies, capability, type resolve  
- `Poly/DomainModeling/Analysis/OwnershipAggregatePass.cs`  
- `Poly/DomainModeling/Analysis/BehaviorPass.cs`  
- `Poly/Syntax/Analysis/AnalysisContext.cs` — `GetMetadata<T>`

## Exact Steps

1. Prefer **Option A**: analyzers accept `AnalysisContext?` (keep optional `AnalysisResult?` for tests/codegen callers if needed).
2. Resolve metadata with context first, then legacy `AnalysisResult`, then existing heuristics.
3. Metadata to preserve:
   - Aggregate: `DomainTypeLookupMetadata`, `EntityStructureMetadata`
   - Behavior: type lookup, `EffectivePoliciesMetadata`, `ActionCapabilityMetadata`, `ResolvedTypeReferenceMetadata`
4. Update `OwnershipAggregatePass` / `BehaviorPass` to pass `context` into analyzers (ctors can stay empty for now if only used from passes with context).
5. Existing external callers that pass `AnalysisResult` only must still work (codegen path before A3).
6. Add or extend unit tests that root detection with `EntityStructureMetadata` still wins over heuristics when only context is supplied.

## Verification

- [ ] Build green  
- [ ] Existing Aggregate/Behavior/Storage tests green (or equivalent)  
- [ ] New/adjusted test: context-supplied `EntityStructureMetadata.IsRoot` is honored  
- [ ] No silent “always heuristic root” when structure metadata exists  

## Output

- Analyzer + pass edits  
- Test(s)  
- Status Done  

## Out of Scope

- Registering passes on domain pipeline (A2)  
- DslCompiler slim (A3)  
- New diagnostic codes  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
