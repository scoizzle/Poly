# Micro-Task: Spike — policy sample subject from property bag

**Parent**: WS8 / WP5 (A+ package)  
**Suite:** [`ws8-README.md`](ws8-README.md) **#6**  
**Difficulty**: Small–Medium  
**Estimated Tokens**: ~6k  
**Status**: [x] **Done** — spike in `PolicySampleSubjectSpikeTests.cs` (8 tests, 2 with fail-closed negative assertions). Doc: `spikes/policy-sample-subject.md`.  
**Proven:** sealed/anonymous records, non-null bags. **Unsafe:** Dictionary, Expando. **Null `int?` throws.**  
**Follow-ups:** `#6b` ✅ **Done** · `#6c` ✅ **Done** · `#6d` ✅ **Done**

## Objective

Decide **how** MCP (and tests) build a CLR subject for `PolicyEvaluator` from a property name → value bag, so `evaluate_policy` can call the VM without inventing domain opcodes.

## Why

Core tests use hand-written records (`Person`, `Order`). Agents will pass JSON-like bags. Member access on the subject must work with the existing DE → Syntax → VM path.

## Exact Steps

1. Write a short spike test file under `Poly.Tests/DomainModeling/Lowering/` (or `Poly.Tests/Mcp/`) that tries **in order** until one works for `Property("Age")` vs sample Age:
   - `Dictionary<string, object>` / `IReadOnlyDictionary<string, object>`
   - `ExpandoObject` / `IDictionary<string, object>`
   - Anonymous type (baseline — known working)
   - Optional: simple custom type with public properties set via reflection
2. For each approach: lower/evaluate `Age >= 18` (or reuse `CompileVMPredicate`) with values 25 (true) and 15 (false).
3. Record results in the agent-summary **or** `docs/plans/v2-to-v3/spikes/policy-sample-subject.md`:
   - Approach | VM works? | Notes
4. Recommend **one** approach for MCP `evaluate_policy`.
5. If **none** of the bag types work: document failure and propose the smallest custom builder (e.g. reflection onto a generated type, or limited fixed templates) — **do not** implement full codegen in this task unless trivial.

## Verification

- [ ] Spike tests exist (pass or explicit fail with documented reason)
- [ ] Written recommendation for the evaluate_policy task
- [ ] No domain-specific VM opcodes

## Out of Scope

- MCP tools
- Full Dictionary entity simulation platform
- Changing DE lowering for every gap node
