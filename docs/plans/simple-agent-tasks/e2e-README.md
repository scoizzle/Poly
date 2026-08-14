# DomainModeling e2e representation — Fleet queue (`e2e-*`)

**Parent:** [`../domainmodeling-e2e-representation-2026-08-13.md`](../domainmodeling-e2e-representation-2026-08-13.md)  
**Probe IDs:** [`../fleet-eval-fixes-2026-08-12.md`](../fleet-eval-fixes-2026-08-12.md) — reuse those IDs; do not invent a second numbering.  
**Pre-ship:** [`../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md)  
**Guide:** `Poly.Mcp/Docs/poly-dsl-guide.md`  
**CURRENT:** this pack is **parked**. A human admits **one slice README** (or one wave) at a time via [`PIPELINE-STATUS.md`](./PIPELINE-STATUS.md). This file exists so a fleet can be assigned without two agents editing the same hot file.

**Status:** Tasked 2026-08-13. No slice `[x]`.

---

## Copy-paste agent prompt

```text
Read docs/plans/simple-agent-tasks/e2e-README.md and the slice README you were assigned.
Claim the first Status: [ ] task file (write Claimed by) before editing code.
Follow Exact steps. File ownership is exclusive. One failing TUnit test before production edits.
Do not start p1 temporal. Do not re-add Link/Unlink/Delete Effect IR.
Verify with: dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
             dotnet run --project Poly.Tests/Poly.Tests.csproj
Mark the task [x] and the slice README table. Stop at the slice gate for pr1.
```

| Agent | Assign |
|-------|--------|
| A | [`e2e-0-README.md`](./e2e-0-README.md) — wave 1 |
| B | [`e2e-p-README.md`](./e2e-p-README.md) — wave 1 |
| C | [`e2e-g0-README.md`](./e2e-g0-README.md) — wave 1 |
| D | [`e2e-r-README.md`](./e2e-r-README.md) — wave 2 after A–C *or* immediately (no file overlap) |
| E | [`e2e-1-README.md`](./e2e-1-README.md) — wave 3 after D; pause before 1-2 if exporter contended |
| F | [`e2e-s-README.md`](./e2e-s-README.md) — wave 3 after D; do s-1/s-2/s-4 now, **s-3 in wave 4** |
| G | [`e2e-4-README.md`](./e2e-4-README.md) — wave 3 after C (g0) |
| H | wave 4 serial: F’s s-3 → [`e2e-2-README.md`](./e2e-2-README.md) → [`e2e-x-README.md`](./e2e-x-README.md) |
| I | [`e2e-3-README.md`](./e2e-3-README.md) — wave 5 after C |
| J | [`e2e-5-README.md`](./e2e-5-README.md) — wave 6 after B + D |
| K | [`e2e-6-README.md`](./e2e-6-README.md) — wave 7 after J |

## How to dispatch a fleet

1. Assign **one agent per slice README**, never two agents to the same slice.
2. Start only slices in the **current wave** whose prereq wave is fully `[x]`.
3. The agent opens the slice README, claims the first `[ ]` task (write `Claimed by` on that task file **before** editing code), walks tasks in order, runs that task’s verify, marks `[x]`, then the slice gate.
4. **Edit only the File ownership table** on the claimed task. If you need a file you do not own, stop and note the blocker.
5. `poly-dsl-guide.md`: slice **0** owns the honesty sweep. Later slices may **append** one “now shipped” bullet for their construct. Do not rewrite unrelated sections.
6. After a slice gate: pr1 on that slice’s dirty files. Do not mark PIPELINE-STATUS unless the human asked.

### Wave DAG

```text
Wave 1 (3 agents, no code-path overlap)
  e2e-0 honesty          docs + delete-grammar + Domain.cs XML
  e2e-p printer          DomainDslPrinter only
  e2e-g0 probe gate      scripts/run-probe.sh + probe-check

Wave 2 (1 agent — hot analysis/runtime)
  e2e-r params           L3 + P1 + P2  (after wave 1 optional; does not need 0/P/G0)

Wave 3 (3 agents — after R; G0 required for 3 and 4)
  e2e-1 unique           instance write + exporter Create  (not DbContext)
  e2e-s subs             DomainInstanceStore + SubscriptionAnalyzer
                         (S-3 exporter order waits for wave 4)
  e2e-4 api              MinimalApiGenerator + HttpFileGenerator   [needs G0]

Wave 4 (1 agent — DomainToCSharpExporter exclusive)
  e2e-s-3 export order → e2e-2 q3 guards → e2e-x entity-export

Wave 5 (1 agent — after G0; can start once wave 1 G0 is done, in parallel with 4 if different files)
  e2e-3 relmap + pack SQL + unique indexes     DbContextGenerator

Wave 6 (1 agent — after P and R; parser/printer free)
  e2e-5 valuetype

Wave 7 (1 agent — after 5)
  e2e-6 contracts
```

If you only have one agent: walk slices in plan order `0 → p → g0 → r → 1 → s → 2 → 4 → x → 3 → 5 → 6` (g0 before 3/4; r before 1/2/s-export/x).

---

## Hot-file exclusive owners

| File | Owner slice | After that slice |
|------|-------------|------------------|
| `Poly.Mcp/Docs/poly-dsl-guide.md` | **e2e-0** (honesty) | later slices append one bullet |
| `docs/interpretation/domain-execution-model.md` | e2e-0 | — |
| `docs/domainmodeling-capability-inventory.md` | e2e-0 | — |
| `Poly/DomainModeling/Parsing/DslGrammar.cs` | e2e-0 (delete pattern) | — |
| `Poly/DomainModeling/Parsing/DomainDslPrinter.cs` | **e2e-p** | e2e-5, then e2e-6 |
| `scripts/run-probe.sh`, `scripts/probe-check/**`, `scripts/discovery-round.sh` | **e2e-g0** | — |
| `Poly/DomainModeling/Parsing/DslExpressionParser.cs` | **e2e-r** (decimals) | e2e-5, e2e-6 |
| `Poly/DomainModeling/Analysis/ExpressionTypeAnalyzer.cs` | **e2e-r** | — |
| `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` | **e2e-r** then **e2e-1** | — |
| `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs` | **e2e-r** then **e2e-2** | — |
| `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` | **e2e-s** | e2e-1 may *call* public API only |
| `Poly/DomainModeling/Analysis/SubscriptionAnalyzer.cs` | **e2e-s** | — |
| `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` | **wave 4 serial** (1-export → s-3 → 2 → x) | — |
| `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` | **e2e-x** | — |
| `src/Poly.DslCompiler/MinimalApiGenerator.cs` | **e2e-4** | — |
| `src/Poly.DslCompiler/DbContextGenerator.cs` | **e2e-3** | — |
| `src/Poly.DslCompiler/DslCompiler.cs` (`CompileMode` XML) | e2e-0 | — |
| `Poly/DomainModeling/Domain.cs` (XML only) | e2e-0 | — |

**L3 implementation lock (do not re-pick):** treat an in-scope action-parameter name on `PropertyAccess` as a parameter everywhere `paramEnv` is consulted. **Do not** change `ParsePrimary` to emit `ParameterAccess`. Parser change in e2e-r is **decimals only**.

**Uniqueness lock:** enforce on `DomainEntityInstance` write path (query store if attached). Do **not** edit `NotifyTransition`. Unique **EF index** is e2e-3, not e2e-1.

---

## Slice index

| Slice | README | Wave | Fleet IDs absorbed |
|-------|--------|------|--------------------|
| 0 Honesty | [`e2e-0-README.md`](./e2e-0-README.md) | 1 | P4-5, P7-1, P7-4 |
| P Printer | [`e2e-p-README.md`](./e2e-p-README.md) | 1 | P4-1…P4-4 |
| G0 Probe gate | [`e2e-g0-README.md`](./e2e-g0-README.md) | 1 | P0-0 |
| R Params | [`e2e-r-README.md`](./e2e-r-README.md) | 2 | P1-1…3, P2-1…10 |
| 1 Unique | [`e2e-1-README.md`](./e2e-1-README.md) | 3 | (research unique) |
| S Subs | [`e2e-s-README.md`](./e2e-s-README.md) | 3 + 4 | P6-1…4 |
| 4 API | [`e2e-4-README.md`](./e2e-4-README.md) | 3 | P3-1…8 |
| 2 Q3 export | [`e2e-2-README.md`](./e2e-2-README.md) | 4 | L5, 05-F5, 12-F10 orders |
| X Entity export | [`e2e-x-README.md`](./e2e-x-README.md) | 4 | P3-10…20 |
| 3 Relmap | [`e2e-3-README.md`](./e2e-3-README.md) | 5 | P7-5…7 + unique index |
| 5 ValueType | [`e2e-5-README.md`](./e2e-5-README.md) | 6 | complexity #6 |
| 6 Contracts | [`e2e-6-README.md`](./e2e-6-README.md) | 7 | complexity #3 |

---

## Hard rules (every agent)

| Rule | Why |
|------|-----|
| One failing TUnit test (or probe) **before** production edit | AGENTS §4 |
| Names `Method_Condition_ExpectedResult` | AGENTS |
| No `#region`, no new abstraction without a second consumer | AGENTS |
| Fail-closed — no vacuous success | pr1 |
| Do not re-add Link/Unlink/Delete Effect IR | parent non-goal |
| Do not start p1 temporal (`Now`, `days`) | L4 |
| Do not implement fleet P5 / P7-2/3/8–11 / 12-F10 | stays in fleet-eval |
| Full suite green before slice gate `[x]` | |

```bash
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

---

## Done (whole pack)

Every slice gate `[x]`, parent exit criteria met, guide honest, `run-probe.sh` 0/0 full-solution on 09/13 probes, pr1 clean on the last wave.
