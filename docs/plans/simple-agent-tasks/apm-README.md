# Analysis Pipeline Merge — Simple-Agent Queue (`apm-*`)

**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md)  
**Inventory:** [`../../domainmodeling-capability-inventory.md`](../../domainmodeling-capability-inventory.md)  
**CORE:** [`../../CORE.md`](../../CORE.md)  
**Gate process:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Rules

1. **One micro-task at a time** when fixing residuals.  
2. **Phase A before treating Phase B as Done** — B needs diagnostic tests (§14).  
3. Do **not** merge StoragePass into the domain pipeline.  
4. Do **not** drop context metadata bridging.  
5. Pre-ship gate before claiming suite complete.

---

## Agent pick

```text
DONE:    APM product bar (A+B) — uncommitted; 1609 green; 11 PipelineMerge fixtures
CURRENT: C′.0 commit; optional C′.1 Dependencies (parent §15)
THEN:    C′.2 inventory §5; dogfood DMDEP001 bidir (C′.4)
PULL:    Transport keep/drop; cycle refinement
```

---

## Phase A — Merge

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **A1** | Metadata bridge | [`apm-a1-metadata-bridge.md`](apm-a1-metadata-bridge.md) | `[x]` code | Medium |
| **A2** | Register 3 passes | [`apm-a2-register-domain-pipeline.md`](apm-a2-register-domain-pipeline.md) | `[x]` code | Small |
| **A3** | Slim DslCompiler | [`apm-a3-dslcompiler-slim.md`](apm-a3-dslcompiler-slim.md) | `[x]` code | Small |
| **A4** | Domain metadata tests | [`apm-a4-domain-metadata-tests.md`](apm-a4-domain-metadata-tests.md) | `[x]` 4 green | Medium |
| **A5** | Codegen regression | [`apm-a5-codegen-regression.md`](apm-a5-codegen-regression.md) | `[x]` green | Small |
| **A′** | Review residuals (Dependencies, Transport msg) | parent §13 | `[x]` | Small |
| **Gate A** | Pre-ship Phase A | [`apm-gate-phase-a.md`](apm-gate-phase-a.md) | `[x]` | Process |

**Exit A:** Metadata on domain analysis + slim codegen **committed**.

---

## Phase B — Diagnostics

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **B1** | DMAGG001 (orphan warning) | [`apm-b1-aggregate-diagnostics.md`](apm-b1-aggregate-diagnostics.md) | `[x]` | Medium |
| **B2** | CrossReference + DMDEP001 | [`apm-b2-cycle-diagnostics.md`](apm-b2-cycle-diagnostics.md) | `[x]` | Medium |
| **B3** | DMBEH001 hint (narrowed) | [`apm-b3-behavior-hint.md`](apm-b3-behavior-hint.md) | `[x]` | Small |
| **B′** | Review residuals (B′.1–B′.3 done) | parent §14 | `[x]` product | Small |
| **C′** | Post-review residuals | parent **§15** | `[ ]` commit + deps | Small |

**Exit B (product):** 7 diagnostic fixtures green; DMAGG002 removed; DMBEH001 narrowed.  
**Exit suite (ops):** Clean tree after **§15 C′.0** commit; inventory sync **C′.2**.

---

## Do not pick

| Item | Why |
|------|-----|
| Move StoragePass to domain pipeline | Needs packs on every evolve |
| Claim Phase B Done without B′.1 | Exit criteria unmet |
| DMBEH001 as Error | Noisy for valid void actions |
| Second cycle algorithm | CrossReference already wired |

---

## Principles

- Domain fidelity + CORE seams  
- Tests more specific; production more generic  
- Fail-closed codegen retained  
- Diagnostics without tests are not “Done”  
