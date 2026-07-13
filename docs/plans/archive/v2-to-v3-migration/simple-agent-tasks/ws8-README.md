# WS8 Micro-Task Suite — Analysis Unification & Lowering

**Parent workstream:** [`../workstreams/ws8-analysis-unification-and-lowering.md`](../workstreams/ws8-analysis-unification-and-lowering.md)  
**Slice ownership:** Phase B maps to **Slice 2–3** in [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md).  
**Simple agents:** prefer the ordered **`vs-*` suite** — [`vs-README.md`](vs-README.md) (starts at Slice 0 honesty).  
**Last Updated:** 2026-07-13  
**Status:** ✅ Phase B complete — all spike/invariant/MCP tasks Done  
**Context:** M1–M4 cutover complete. `DomainEntityInstance` now provides the runtime instance layer; MCP `evaluate_policy` uses it directly.

## Goal

```
DomainExpression / Policy  →  DomainExpressionLoweringPass  →  Syntax AST
  →  Interpreter.Compile (DirectVmAbiEmitter)  →  Execute with CLR record / sample subject
```

MCP: **add_policy** → **get_policy_expression** (inspect) → **evaluate_policy** (VM bool).

---

## Phase A — Foundation (Done)

| Pri | Task | Status |
|-----|------|--------|
| 1–5 | e2e policy, DE matrix, VM-primary, `get_policy_expression`, contract inventory | [x] Done |

---

## Phase B — A+ agent loop

### B0 — Spike + invariants (before / with implement)

| Pri | Task | Status | Why |
|-----|------|--------|-----|
| **6** | [`ws8-spike-policy-sample-subject.md`](ws8-spike-policy-sample-subject.md) | [x] Done | Records/StrictBag OK; Dict/Expando unsafe; null `int?` throws |
| **6b** | [`ws8-spike-harden-negative-subject-tests.md`](ws8-spike-harden-negative-subject-tests.md) | [x] Done | `MatchNumeric` + Age≥18 not `1L` |
| **6c** | [`ws8-spike-demote-emit-until-proven.md`](ws8-spike-demote-emit-until-proven.md) | [x] Done | Primary = non-nullable bag; Emit unproven |
| **6d** | [`ws8-invariant-policy-subject-types.md`](ws8-invariant-policy-subject-types.md) | [x] **Accurate** | Superceded by product `ClrTypeEntityMapping` + `PolicySubject.Validate` + `DomainValidatedEvaluationTests`. Dict/Expando rejected at evaluation boundary; test-only StrictBag/helpers in `PolicyTestSubjects`. |
| **6e** | [`ws8-spike-bool-abi-adult-assert.md`](ws8-spike-bool-abi-adult-assert.md) | [x] Done | Verified via `EvaluatePolicy_BooleanGuard_EqualsTrue_ReturnsTrue` — bool `==` comparison works on VM |
| **6f** | [`ws8-spike-matchnumeric-positive-control.md`](ws8-spike-matchnumeric-positive-control.md) | [x] Done | Verified via `EvaluatePolicy_GreaterThanOrEqual_MatchNumeric_ReturnsTrue` — `>= 100` with exact boundary |
| **6g** | [`ws8-invariant-policy-property-name-alignment.md`](ws8-invariant-policy-property-name-alignment.md) | [x] **Accurate** | Domain property names proven via `ClrTypeEntityMapping.ToDomainProperty` using CLR reflection — property names align by construction. |
| **6h** | [`ws8-invariant-no-dict-expando-subjects.md`](ws8-invariant-no-dict-expando-subjects.md) | [x] **Accurate** | `PolicySubject.Validate` rejects Dict/Expando at evaluation boundary. `Evaluator.Evaluate<T>` calls it. |
| **7a** | [`ws8-mcp-add-policy-expression-contract.md`](ws8-mcp-add-policy-expression-contract.md) | [x] Done | `PolicyExpressionContract` + `PolicyExpressionParser` in production; spike doc written |

### B1 — MCP implement

| Pri | Task | Status | Depends |
|-----|------|--------|---------|
| **7** | [`ws8-mcp-add-policy.md`](ws8-mcp-add-policy.md) | [x] Done — `add_policy` MCP tool in `V3PolicyTool` |
| **8** | [`ws8-mcp-evaluate-policy-vm.md`](ws8-mcp-evaluate-policy-vm.md) | [x] Done — `evaluate_policy` MCP tool uses `DomainEntityInstance` + VM |
| **9** | [`ws8-mcp-policy-e2e-smoke.md`](ws8-mcp-policy-e2e-smoke.md) | [x] Done — `V3McpSmokeTests` covers add + evaluate + boolean + numeric |
| **10** | [`ws8-a-plus-polish.md`](ws8-a-plus-polish.md) | [x] Done — `V3EvalTool`→`V3PolicyTool`, MCP README updated, honesty invariant documented |
| **11** | [`ws8-invariant-mcp-tool-honesty.md`](ws8-invariant-mcp-tool-honesty.md) | [x] Done — Invariant in `Poly.Mcp/README.md` |

**Suggested order:** **6d** (can parallel **6e**, **6f**) → **6g/6h** with or after 6d → **7a → 7 → 8 → 9 → 10/11**.

### Invariants (do not lose)

| # | Invariant |
|---|-----------|
| I1 | No `Dictionary` / `ExpandoObject` as PolicyEvaluator subjects |
| I2 | No null nullable value types on subjects (`int?` null throws) |
| I3 | Missing keys → non-null defaults (0, `""`), not null |
| I4 | MCP tool name/description/success match behavior |
| I5 | Policy PropertyAccess names align with subject (and domain) property names |
| I6 | Fail-closed tests cover int **and** long **and** bool-true ABI where relevant |

### A+ definition of done

- [x] MCP never claims eval without returning a VM bool
- [x] Agent can attach a policy without core test hacks
- [x] Agent can evaluate sample values true/false
- [x] Subject builder enforces I1–I3, I5 (via `DomainEntityInstance.Create`)
- [x] One MCP-only e2e smoke (V3McpSmokeTests)
- [x] Domain-attached core tests still green (1195 tests)

---

## Deferred

| Item | Why |
|------|-----|
| Full DE VM for DateOp / Owned / Rel | Documented gaps |
| Contract **codegen** | WP9 |
| Action/effect program lowering | Separate |
| Reflection.Emit subject gen | Unproven until optional green spike |
| Compile-once PolicyEvaluator cache | Perf unless measured |

## Rules

1. No domain-specific VM opcodes.  
2. Tool names/descriptions must match behavior.  
3. Constrained `add_policy` expression — no free-form AST bags.  
4. File `agent-summaries/ws8-*.md` on completion.

## Related

- `spikes/policy-sample-subject.md`
- `spikes/mcp-guiding-principles.md`
- `docs/decisions/2026-06-08-vm-as-canonical-semantics.md`
- `docs/decisions/2026-06-08-domain-lowering-boundary.md`
