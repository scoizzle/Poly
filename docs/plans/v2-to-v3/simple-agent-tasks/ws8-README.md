# WS8 Micro-Task Suite — Analysis Unification & Lowering

**Parent workstream:** [`../workstreams/ws8-analysis-unification-and-lowering.md`](../workstreams/ws8-analysis-unification-and-lowering.md)  
**Slice ownership:** Phase B maps to **Slice 2–3** in [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md).  
**Simple agents:** prefer the ordered **`vs-*` suite** — [`vs-README.md`](vs-README.md) (starts at Slice 0 honesty).  
**Last Updated:** 2026-07-11  
**Context:** M1–M4 cutover complete. Phase A Done; Phase B A+ — spike + 6b/6c done; remaining work executed via `vs-*` where possible.

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
| **6d** | [`ws8-invariant-policy-subject-types.md`](ws8-invariant-policy-subject-types.md) | [ ] **Next** | Product subject helper + defaults |
| **6e** | [`ws8-spike-bool-abi-adult-assert.md`](ws8-spike-bool-abi-adult-assert.md) | [ ] Not Started | Adult assert must catch **`bool true`**, not only `1L` |
| **6f** | [`ws8-spike-matchnumeric-positive-control.md`](ws8-spike-matchnumeric-positive-control.md) | [ ] Not Started | Prove `MatchNumeric` true on working subject |
| **6g** | [`ws8-invariant-policy-property-name-alignment.md`](ws8-invariant-policy-property-name-alignment.md) | [ ] Not Started | Property names: domain / expression / subject align |
| **6h** | [`ws8-invariant-no-dict-expando-subjects.md`](ws8-invariant-no-dict-expando-subjects.md) | [ ] Not Started | Reject Dict/Expando at factory or Evaluate boundary |
| **7a** | [`ws8-mcp-add-policy-expression-contract.md`](ws8-mcp-add-policy-expression-contract.md) | [ ] Not Started | Constrained expression JSON for agents |

### B1 — MCP implement

| Pri | Task | Status | Depends |
|-----|------|--------|---------|
| **7** | [`ws8-mcp-add-policy.md`](ws8-mcp-add-policy.md) | [ ] Not Started | Prefer 7a |
| **8** | [`ws8-mcp-evaluate-policy-vm.md`](ws8-mcp-evaluate-policy-vm.md) | [ ] Not Started | 6d (+ 6e–6h as ready); prefer 7 |
| **9** | [`ws8-mcp-policy-e2e-smoke.md`](ws8-mcp-policy-e2e-smoke.md) | [ ] Not Started | 7 + 8 |
| **10** | [`ws8-a-plus-polish.md`](ws8-a-plus-polish.md) | [ ] Not Started | After 8–9 |
| **11** | [`ws8-invariant-mcp-tool-honesty.md`](ws8-invariant-mcp-tool-honesty.md) | [ ] Not Started | After 8 |

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

- [ ] MCP never claims eval without returning a VM bool
- [ ] Agent can attach a policy without core test hacks
- [ ] Agent can evaluate sample values true/false
- [ ] Subject builder enforces I1–I3, I5
- [ ] One MCP-only e2e smoke
- [ ] Domain-attached core tests still green

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
