# Vertical Slice Micro-Tasks (Simple Agents)

**Parent plan:** [`../vertical-slice-finish-plan.md`](../vertical-slice-finish-plan.md)  
**Last Updated:** 2026-07-11  
**Audience:** Smaller / cheaper agents — one file per claim, tiny reading list.

## Operating rules (mandatory)

1. **One task at a time.** Claim it (Status → In Progress) before coding.
2. **Pick the first `[ ] Not Started` in the ordered table below.** Do not skip ahead unless the task says “parallel OK.”
3. **Do not start Slice 2** until Slice 0 tasks marked **blocks Slice 2** are Done.
4. **Do not start Slice 3** until Slice 2 is Done.
5. **Do not pick Slice 4/5** unless an orchestrator reopens them (deferred).
6. After Done: write `../agent-summaries/vs-<task-id>-summary.md` using [`TEMPLATE-task-summary.md`](../agent-summaries/TEMPLATE-task-summary.md). Update only the Status line on the task file.
7. Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`  
   Tests: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (or filter to tests you added).
8. Principles: AGENTS.md — domain fidelity, thin slice, no domain VM opcodes, MCP honesty.

### Status marks

| Mark | Meaning |
|------|---------|
| `[ ]` | Not Started — pickable when previous required tasks Done |
| `[~]` | In Progress |
| `[x]` | Done |
| **Skip** | Do not execute (deferred / pull-only) |

---

## Pick order (do in sequence)

### Slice 0 — Honesty foundation

| # | Task | File | Parallel | Blocks |
|---|------|------|----------|--------|
| **0.1** | Fail-loud evolution | [`vs-s0-fail-loud-evolution.md`](vs-s0-fail-loud-evolution.md) | ∥ 0.4, 0.5 | Slice 2–3 evolve honesty |
| **0.2** | `add_action_to_stage` honesty | [`vs-s0-add-action-to-stage-honesty.md`](vs-s0-add-action-to-stage-honesty.md) | after/with 0.1 | MCP structure truth |
| **0.3** | Wire PolicySubject into PolicyEvaluator | [`vs-s0-wire-policy-subject-validate.md`](vs-s0-wire-policy-subject-validate.md) | ∥ 0.1 | **Slice 2** |
| **0.4** | Fix instance EmitInvoke | [`vs-s0-fix-emit-invoke-instance.md`](vs-s0-fix-emit-invoke-instance.md) | ∥ 0.1 | method-backed DE |
| **0.5** | MCP README V3-only | [`vs-s0-mcp-readme-honesty.md`](vs-s0-mcp-readme-honesty.md) | anytime in Slice 0 | docs honesty |

**Slice 0 done when:** 0.1–0.5 all `[x]`.

### Slice 1 — Structure path (verify + pin)

| # | Task | File |
|---|------|------|
| **1.1** | Verify structure e2e coverage | [`vs-s1-verify-structure-path.md`](vs-s1-verify-structure-path.md) |
| **1.2** | Pin canonical entity (Person **or** Order) | [`vs-s1-pin-canonical-entity.md`](vs-s1-pin-canonical-entity.md) |

**Slice 1 done when:** 1.1–1.2 `[x]`. Prefer after 0.2 so stage-action story is honest.

### Slice 2 — Policy runtime (direct API only)

| # | Task | File | Depends |
|---|------|------|---------|
| **2.1** | Subject helper + reject Dict/Expando at evaluate | [`vs-s2-subject-helper-and-reject.md`](vs-s2-subject-helper-and-reject.md) | **0.3** |
| **2.2** | Bool ABI adult assert | [`vs-s2-bool-abi-adult-assert.md`](vs-s2-bool-abi-adult-assert.md) | 2.1 or parallel after 0.3 |
| **2.3** | Age/numeric policy true **and** false e2e | [`vs-s2-policy-true-false-e2e.md`](vs-s2-policy-true-false-e2e.md) | 2.1, 2.2 |
| **2.4** | Property name alignment test | [`vs-s2-property-name-alignment.md`](vs-s2-property-name-alignment.md) | 2.1 |
| **2.5** | Domain-attached policy e2e on **canonical** entity | [`vs-s2-domain-attached-policy-e2e.md`](vs-s2-domain-attached-policy-e2e.md) | 1.2, 2.3 |

**Slice 2 done when:** 2.1–2.5 `[x]`. No MCP tools in this slice.

### Slice 3 — Policy MCP product loop

| # | Task | File | Depends |
|---|------|------|---------|
| **3.1** | Constrained expression contract for add_policy | [`vs-s3-add-policy-expression-contract.md`](vs-s3-add-policy-expression-contract.md) | Slice 2 |
| **3.2** | `add_policy` MCP tool | [`vs-s3-add-policy-tool.md`](vs-s3-add-policy-tool.md) | 3.1 |
| **3.3** | `evaluate_policy` MCP tool (VM bool) | [`vs-s3-evaluate-policy-tool.md`](vs-s3-evaluate-policy-tool.md) | 3.2, Slice 2 |
| **3.4** | MCP e2e smoke structure + policy + eval | [`vs-s3-mcp-policy-e2e-smoke.md`](vs-s3-mcp-policy-e2e-smoke.md) | 3.2, 3.3 |
| **3.5** | Polish affordances + MCP README policy section | [`vs-s3-policy-mcp-polish.md`](vs-s3-policy-mcp-polish.md) | 3.4 |

**Slice 3 done when:** 3.1–3.5 `[x]` → run checkpoint:

| # | Task | File |
|---|------|------|
| **M2** | Mark M2 product-complete in plans | [`vs-checkpoint-m2-close.md`](vs-checkpoint-m2-close.md) |

### Deferred (do not pick)

| Slice | Why |
|-------|-----|
| 4 First effect | After M2; orchestrator only |
| 5 Relationship | Pull-only |

---

## Map from older ws8-* tasks

If both exist, **prefer `vs-*`** (this suite owns order). Older files are specs/history:

| vs task | Related ws8 (optional reading) |
|---------|--------------------------------|
| 0.3, 2.1 | `ws8-invariant-policy-subject-types.md`, `ws8-invariant-no-dict-expando-subjects.md` |
| 2.2 | `ws8-spike-bool-abi-adult-assert.md` |
| 2.3 | `ws8-spike-matchnumeric-positive-control.md` |
| 2.4 | `ws8-invariant-policy-property-name-alignment.md` |
| 3.1–3.5 | `ws8-mcp-add-policy*.md`, `ws8-mcp-evaluate-policy-vm.md`, etc. |

---

## Next task right now

**Start at `vs-s0-fail-loud-evolution.md` (0.1)** unless something is already `[~] In Progress`.
