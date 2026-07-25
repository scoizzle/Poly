# Analysis Pipeline Merge — Simple-Agent Queue (`apm-*`)

**Parent:** [`../analysis-pipeline-merge.md`](../analysis-pipeline-merge.md)  
**Inventory:** [`../../domainmodeling-capability-inventory.md`](../../domainmodeling-capability-inventory.md)  
**CORE:** [`../../CORE.md`](../../CORE.md)  
**Gate process:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)

---

## Rules

1. **One micro-task at a time.** Do not skip A1 (metadata bridge).
2. Read only the **Required Reading** on the task file + parent § referenced.
3. **Phase A before Phase B.** No new diagnostic codes in Phase A.
4. Do **not** merge StoragePass into the domain pipeline.
5. Do **not** drop `_analysis` / context bridging — silent root/policy regression risk (parent §4).
6. Pre-ship gate before marking Phase A Done.
7. Prefer smallest coherent PR: A1→A5 in one commit only if green; otherwise A1 alone first.

---

## Agent pick

```text
DONE:    (none)
CURRENT: APM.A1 — metadata bridge (AnalysisContext)
THEN:    A2 → A3 → A4 → A5 → Gate
PULL:    Phase B diagnostics; CrossReferencePass consumer; Transport keep/drop
```

---

## Phase A — Merge (ship first)

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **A1** | Metadata bridge for Aggregate/Behavior | [`apm-a1-metadata-bridge.md`](apm-a1-metadata-bridge.md) | `[ ]` | Medium |
| **A2** | Register 3 passes on domain pipeline | [`apm-a2-register-domain-pipeline.md`](apm-a2-register-domain-pipeline.md) | `[ ]` | Small |
| **A3** | Slim DslCompiler codegen pipeline | [`apm-a3-dslcompiler-slim.md`](apm-a3-dslcompiler-slim.md) | `[ ]` | Small |
| **A4** | Domain analysis metadata tests | [`apm-a4-domain-metadata-tests.md`](apm-a4-domain-metadata-tests.md) | `[ ]` | Medium |
| **A5** | Codegen regression (AllMode + generators) | [`apm-a5-codegen-regression.md`](apm-a5-codegen-regression.md) | `[ ]` | Small |
| **Gate** | Pre-ship review Phase A | [`apm-gate-phase-a.md`](apm-gate-phase-a.md) | `[ ]` | Process |

**Exit A:** `DomainModelAnalyzer.Analyze` exposes Topology + Aggregate + Behavior metadata; DslCompiler only runs Storage (+ Transport/packs); codegen green; no new diagnostic codes.

---

## Phase B — Diagnostics (optional; after Gate A)

| # | Task | File | Status | Difficulty |
|---|------|------|--------|------------|
| **B1** | DMAGG001 / DMAGG002 on OwnershipAggregatePass | [`apm-b1-aggregate-diagnostics.md`](apm-b1-aggregate-diagnostics.md) | `[ ]` | Medium |
| **B2** | Cycle story: CrossReferencePass **or** topology | [`apm-b2-cycle-diagnostics.md`](apm-b2-cycle-diagnostics.md) | `[ ]` | Medium |
| **B3** | Unconditional-action hint (suggestions, not error) | [`apm-b3-behavior-hint.md`](apm-b3-behavior-hint.md) | `[ ]` | Small |

**Exit B:** Crafted fixtures show codes via analysis/MCP; noise acceptable; suite green.

---

## Do not pick

| Item | Why |
|------|-----|
| Move StoragePass to domain pipeline | Needs packs/type maps on every evolve |
| Bar B / RestApiSurfacePass | Infra pull — different track |
| Skip A1 “just register passes” | Silent Aggregate/Behavior regression |
| DMBEH001 as Error | Noisy for valid void actions |
| Second cycle algorithm if CrossReferencePass can wire | Prefer one cycle story |

---

## Session sketch

| Session | Tasks | Outcome |
|---------|-------|---------|
| **1 — Bridge** | A1 | Context metadata; tests that fallback path unchanged |
| **2 — Wire** | A2 + A3 | Domain registers three passes; DslCompiler slim |
| **3 — Prove** | A4 + A5 + Gate | Metadata on domain result; codegen green |
| **4 — Optional** | B1–B3 | Early diagnostics if dogfood wants them |

---

## Principles

- Domain fidelity + CORE seams  
- Tests more specific; production more generic  
- Smallest coherent slice (Phase A without diagnostics)  
- Fail-closed: keep DslCompiler throws for missing storage/behavior/aggregate  
