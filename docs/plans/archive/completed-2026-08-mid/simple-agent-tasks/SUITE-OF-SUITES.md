# Suite of suites — DomainModeling backlog pipeline

**Date:** 2026-08-06  
**Audience:** GitHub **Copilot CLI** (primary), Grok plan-orchestrator (secondary)  
**Status:** Historical pipeline **complete** 2026-08-06 (dogfood → amu → p4 → coh). Later: p3 · p2 · GI/E1 landed.  
**Next ready suites:** [`READY-TO-TASK.md`](./READY-TO-TASK.md) — mcp-minify · mut-safety · p1  

**CURRENT today:** see master-roadmap (likely none until admit).

---

## 1. Pipeline (strict order) — historical

| Stage | Suite | README | Mode | Parallelism |
|-------|--------|--------|------|-------------|
| **1** | Dogfood wave 2 | [`../v2-to-v3/simple-agent-tasks/dogfood-README.md`](../v2-to-v3/simple-agent-tasks/dogfood-README.md) | Discovery **report-only** (MCP); then fix tasks | S4/S5/S6 discovery may parallel; fixes serial if shared core |
| **2** | **amu** | [`amu-README.md`](./amu-README.md) | Until all tasks + gate | W1.1–W1.3 parallel; W3.1–W3.2 parallel |
| **3** | **p4** | [`p4-README.md`](./p4-README.md) | Until all tasks + gate | Sequential |
| **4** | **coh** | [`coh-README.md`](./coh-README.md) | Until all tasks + gate | After COH-0: R/E/V parallel; **R before D** |

**Do not** start stage N+1 until stage N suite README and gate are complete (or human waives in master-roadmap).

**Never** open grammar, P1 temporal, actors, or archived suites as part of this pipeline unless master-roadmap is updated.

---

## 2. Copilot CLI entry points

| Invoke | Agent file | Job |
|--------|------------|-----|
| Full pipeline | [`.github/agents/domainmodeling-backlog.agent.md`](../../../.github/agents/domainmodeling-backlog.agent.md) | Run stages 1→4 until all Done |
| One suite only | [`.github/agents/plan-suite-until-done.agent.md`](../../../.github/agents/plan-suite-until-done.agent.md) | Loop one README until complete |

### Full backlog (recommended)

```bash
# From repo root
copilot --agent domainmodeling-backlog -p "Execute SUITE-OF-SUITES until all stages complete. Start at first incomplete stage."
```

Interactive:

```text
/agent domainmodeling-backlog
Execute the DomainModeling suite-of-suites from docs/plans/simple-agent-tasks/SUITE-OF-SUITES.md until all stages are Done.
```

### Single suite (ready next)

```bash
copilot --agent plan-suite-until-done -p "Suite: mcp-minify. Mode: until-done."
copilot --agent plan-suite-until-done -p "Suite: mut-safety. Mode: until-done."
copilot --agent plan-suite-until-done -p "Suite: p1. Mode: until-done."
```

Historical:

```bash
copilot --agent plan-suite-until-done -p "Suite: amu. Mode: until-done."
copilot --agent plan-suite-until-done -p "Suite: p4. Mode: until-done."
```

---

## 3. Per-task loop (every implement stage)

1. **Orient** — Read suite README; pick first `[ ]` (respect soft prereqs and file ownership).  
2. **Implement** — Only files listed in the task; follow AGENTS.md + CORE.md for DomainModeling.  
3. **Verify** — Build: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`  
   Tests: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (filter if task names tests).  
4. **Record** — Mark task `[x]`; progress notes; update suite README status lines.  
5. **Pre-ship** — On gate / last task: [`pr1-uncommitted-review-gate.md`](../v2-to-v3/simple-agent-tasks/pr1-uncommitted-review-gate.md).  
6. **Loop** until suite complete or hard blocker (write blocker to suite README + stop stage).

### Dogfood special rules

- Discovery turns: **MCP only**, **no Poly/ source fixes**, report under `docs/plans/v2-to-v3/agent-summaries/dogfood/`.  
- After all S4–S6 have reports: create/fix `dogfood-fix-*` if needed, then re-run scenarios.  
- Then update master-roadmap CURRENT to next suite (amu).

---

## 4. Master-roadmap write-back

When a stage completes:

| Stage done | Set CURRENT to |
|------------|----------------|
| Dogfood wave 2 | `amu` |
| amu gate | `p4` |
| p4 gate | `coh` |
| coh gate | `(none)` — pipeline complete |

File: [`../v2-to-v3/master-roadmap.md`](../v2-to-v3/master-roadmap.md) Agent pick block.

---

## 5. Stop / escalate

Stop the pipeline and leave CURRENT unchanged if:

- Build/tests red after two fix attempts on the same task  
- Dogfood finds a **C** (missing concept) that needs product design (do not invent P1 mid-pipeline)  
- Task file ownership conflicts with dirty tree from another agent  

Write `docs/plans/simple-agent-tasks/PIPELINE-STATUS.md` (create/update) with last stage, last task, blocker.

---

## 6. Grok parity

Same suite READMEs work with:

```text
/plan-orchestrator suite=docs/plans/simple-agent-tasks/amu-README.md mode=until-done
```

Short keys (after plan-orchestrator update): `amu`, `p4`, `coh`, `dogfood`.

---

## 7. Completion definition (entire pipeline)

- [ ] Dogfood S4–S6 reports on disk; critical fixes green or waived  
- [ ] amu gate G1–G7 `[x]`  
- [ ] p4 gate G1–G5 `[x]`  
- [ ] coh gate G1–G6 `[x]`  
- [ ] Master-roadmap CURRENT = `(none)`  
- [ ] Build + full test suite green

## 8. Gate DoD (every suite gate — review P1)

A suite gate may only mark a check `[x]` when the underlying behavior is real.
In particular:

- **“Fail-closed” may never be redefined as “skip validation.”** If a check
  claims fail-closed semantics, the code must fail loud (diagnostic / throw /
  structural failure) when required inputs are unavailable — not silently pass.
- **Bag-unavailable behavior must be decided and tested.** For every pass that
  reads analysis bags, note and test what happens when the bag is missing:
  fail closed (diagnostic), skip with documented no-op, or throw — never
  vacuous success on unvalidated input.
- **Dependencies edge to publisher.** Any pass that reads a bag published by
  another pass must declare that publisher in `Dependencies` (and the pipeline
  builder must honor it). No accidental pipeline-order reliance.
- Gate notes must state the actual contract (e.g. “no false unknown-rel on
  stripped bags”), not hand-wave around it.  
