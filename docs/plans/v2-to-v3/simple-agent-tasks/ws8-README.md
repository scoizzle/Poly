# WS8 Micro-Task Suite — Analysis Unification & Lowering

**Parent workstream:** [`../workstreams/ws8-analysis-unification-and-lowering.md`](../workstreams/ws8-analysis-unification-and-lowering.md)  
**Last Updated:** 2026-07-10 (code review annotations)  
**Context:** M1–M4 cutover complete. WS8 = **WP5 runtime truth** for policies/DomainExpression.

## Goal

```
DomainExpression / Policy  →  DomainExpressionLoweringPass  →  Syntax AST
  →  Interpreter.Compile (DirectVmAbiEmitter)  →  Execute with CLR record args
```

## Code review (2026-07-10) — summary

| Area | Grade | Finding |
|------|-------|---------|
| `PolicyEvaluator` VM-primary | **B+** | Right API split; recompiles every `Evaluate` call (nit) |
| Policy VM tests | **B−** | Bare `Policy` + records work on VM; **no domain attach** (DomainFactory/evolve) |
| DE smoke matrix | **C+** | Inventory + lower-only gaps OK; several tests only assert `node != null` |
| MCP `evaluate_policy` | **D** | **Critical:** name/description claim VM true/false; code only looks up policy metadata |
| Contract rules spike | **A−** | Docs-only, appropriate |

**In Progress first** (do not start new work until closed):

~~1. **`wp5-optional-mcp-evaluate-policy`** — honesty fix (implement eval or rename tool)~~ ✅ **Done** — renamed to `get_policy_expression` with honest description
~~2. **`ws8-e2e-policy-vm-eval`** — add domain-attached policy test (Factory → evolve → evaluate)~~ ✅ **Done** — `Policy_DomainAttached_EvaluatesFromDomainGraph` + `Policy_DomainAttached_ComplexGuardExtractedFromDomain` added

---

## Foundation (do not re-litigate)

| Deliverable | Evidence |
|-------------|----------|
| DE → Syntax AST | `DomainExpressionLoweringPass.cs` |
| Raw DE → VM | `DomainExpressionVmExecutionTests.cs` |
| Lower unit tests | `DomainExpressionLoweringPassTests.cs` |
| Shared analysis / VM | Interpretation module |

## Queue status

| Pri | Task | Status | Review note |
|-----|------|--------|-------------|
| **1** | [`ws8-e2e-policy-vm-eval.md`](ws8-e2e-policy-vm-eval.md) | [x] **Done** | Domain-attached tests added (`Policy_DomainAttached_*`). Full Factory→evolve→evaluate path proven. |
| **2** | [`ws8-domainexpression-lower-smoke-matrix.md`](ws8-domainexpression-lower-smoke-matrix.md) | [x] **Done** | Lower inventory solid; Owned/Date/Rel = lower-only gaps documented |
| **3** | [`ws8-policyevaluator-vm-primary.md`](ws8-policyevaluator-vm-primary.md) | [x] **Done** | VM-primary `Evaluate`; dual-oracle isolated |
| **4** | [`wp5-optional-mcp-evaluate-policy.md`](wp5-optional-mcp-evaluate-policy.md) | [x] **Done** | **Honesty fix:** renamed to `get_policy_expression` — name/description match behavior (inspection only). VM eval deferred to WP5. |
| **5** | [`ws8-inventory-contract-interface-rules.md`](ws8-inventory-contract-interface-rules.md) | [x] **Done** | Spike OK; no codegen |
| Later | Full contract interface gen | — | WP9 when consumer pulls |

## Deferred (out of active queue)

| Item | Why |
|------|-----|
| Full action/effect program lowering | No consumer |
| Dictionary entity simulation | Interpretation owns later |
| New DE node kinds | Call site + test only |
| µop redesign | On demand |

## Rules for executors

1. **No domain-specific VM opcodes** — generic Syntax only.
2. **Tests with C# records** OK.
3. Prefer extending existing tests.
4. File `agent-summaries/ws8-*.md` on completion.
5. **MCP tool names/descriptions must match behavior** (no false “evaluates via VM”).
6. Finish **In Progress** before Not Started / WP9.

## Related

- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`
- `docs/plans/v2-to-v3/spikes/first-v3-consumer.md`
- `docs/plans/v2-to-v3/master-roadmap.md`
