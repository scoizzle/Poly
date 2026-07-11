# Micro-Task: E2E policy evaluation on the VM

**Parent**: WS8 / WP5  
**Suite:** [`ws8-README.md`](ws8-README.md) **#1**  
**Difficulty**: Medium  
**Estimated Tokens**: ~8k  
**Status**: [x] **Done** — domain-attached tests added: `Policy_DomainAttached_EvaluatesFromDomainGraph` + `Policy_DomainAttached_ComplexGuardExtractedFromDomain`. Full Factory→evolve→AddPolicyToEntity→evaluate path proven. 11 total tests.

## Objective

One complete path: **V3 domain with a Policy on an entity** → lower guard → **VM execute** with a C# record → true/false.

## What landed (partial)

- `Poly.Tests/DomainModeling/Lowering/PolicyVmEvaluationTests.cs` (9 tests)
- Bare `new Policy(...)` + `CompileVMPredicate<T>` on records — **VM path works** (age, composite, Not, string equal, stock, etc.)
- Overlaps heavily with policy tests already in `DomainExpressionVmExecutionTests`

## Code review findings

| Severity | Finding |
|----------|---------|
| **High** | Tests **never** use `DomainFactory` / `DomainEvolution` / `AddPolicyToEntity`. Does not prove a policy that survives analysis is the one evaluated. Task objective said “real Policy on a domain entity.” |
| **Medium** | Duplicate coverage of bare-policy VM vs existing `DomainExpressionVmExecutionTests` policy section — limited unique value without domain attach. |
| **Low** | `CompileVMPredicate` called multiple times in some tests (recompile) — fine for tests. |

**VM + record evaluation is real and green.** Domain authoring path is **not** proven.

## Follow-ups (close before Done)

1. Add **at least one** test:
   - `DomainFactory.Create` → entity + property (or minimal shape) → `AddPolicyToEntity` via evolve
   - Analysis succeeds (not rolled back)
   - Resolve policy from **domain graph** (`entity.Policies`)
   - `CompileVMPredicate` / `Evaluate` with matching CLR record → true/false cases
2. Name clearly e.g. `Policy_DomainAttachedAgeGuard_EvaluatesOnVm`.
3. Optional: assert dual-oracle once on that path.

## Verification

- [x] Bare Policy → VM tests pass
- [ ] Domain-attached policy test exists and passes
- [ ] No `Poly.Data.Modeling`
- [ ] No domain opcodes

## Out of Scope

- MCP tool (#4)
- Contract interfaces
- Fixing DE gaps (#2)
