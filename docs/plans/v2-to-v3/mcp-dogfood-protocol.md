# MCP Dogfood Protocol -- Domain Capability Discovery

**Date:** 2026-07-25 (revised)  
**Status:** Active -- Wave 1 (S1-S3) open  
**Purpose:** Drive **direct domain modeling** product direction from structured MCP agent sessions -- not free-form thrash, not codegen.  
**Queue:** [`simple-agent-tasks/dogfood-README.md`](simple-agent-tasks/dogfood-README.md)  
**Report folder:** [`agent-summaries/dogfood/`](agent-summaries/dogfood/)  
**Product guide:** [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../../Poly.Mcp/Docs/poly-dsl-guide.md) (or embedded `get_dsl_guide`)

---

## 0. Historical context

Two dogfood rounds completed 2026-07-18:

- [**Report 1**](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) -- Found **R** gap (runtime missing from MCP). Product response: shipped `create_instance`, `invoke_action`, `get_instance`, `list_instances`, `link_instances`. Score 18/20.
- [**Report 2**](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) -- Post-RT audit found **API honesty** issues (`AddActionToStage` creates empty copies, entity-level policies gate all actions). Score 15/20.

**Wave 1** (S1-S3 in queue) tests concepts that were *pull* during earlier rounds: lifecycle, link/unlink, owned/nested data. Uses the unified taxonomy in section 4. Prior round findings files (C1-C9 under `agent-summaries/dogfood/`) use an older taxonomy -- see section 4.1 for mapping.

---

## 1. Why this exists

Codegen / DAU improved analysis and emit. Remaining product value is **author + evolve + run domains via MCP**. Plans under-specify **core domain concepts** (owned/value, link graph, time, identity, contracts). Dogfood surfaces which concepts actually block agents.

**Non-goals of a dogfood session**

- Fix the platform mid-run (unless session is explicitly a *fix* pass)
- Codegen / DslCompiler / packs / RestApi
- Invent new MCP micro-tools "because tools are missing" before classifying the gap
- Open-ended "model anything"

---

## 2. Session rules (mandatory)

1. **One scenario per session** -- use the scenario task file from [the queue](simple-agent-tasks/dogfood-README.md); do not invent a fourth mid-run.
2. **MCP only** for authoring and runtime (tools below). No direct C# domain builders unless the scenario says "library escape hatch OK for diagnosis."
3. **Guide first:** call `get_dsl_guide` (or read the product guide) before writing DSL.
4. **Prefer `apply_dsl`** for structure + effects; use evolve tools when incremental is clearer.
5. **Runtime path:** `create_instance` -> (`link_instances` if needed) -> `invoke_action` -> `evaluate_policy` / `get_instance` as required.
6. **Stop at first hard blocker** that prevents the scenario success criteria. Do not silently redesign the domain to avoid the concept under test.
7. **Capture** using the report template (section 5). One findings section per blocker.
8. **Classify** every blocker (section 4). Only **C/I/M/G** buckets drive backlog.

### 2.1 Prior findings

The folder `agent-summaries/dogfood/` contains findings JSON and per-child reports from previous dogfood rounds (C1-C9 files use an older taxonomy). Reference these when a blocker repeats; do not re-report known issues without new evidence.

### 2.2 Allowed MCP tools (typical)

| Phase | Tools |
|-------|--------|
| Session | `create_domain_session`, `list_sessions` |
| Author | `apply_dsl`, `export_dsl`, `get_dsl_guide`, evolve add/remove as needed |
| Inspect | `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_suggestions`, `get_relationships` |
| Policy | `add_policy`, `get_policy_expression`, `evaluate_policy` |
| Runtime | `create_instance`, `link_instances`, `get_instance`, `list_instances`, `invoke_action` |
| Oracle (optional) | `simulate_policy`, `describe_*`, `lower_*` |

**Out of band:** codegen CLI, editing `Poly/` source (unless fix pass).

---

## 3. Scenario success criteria

Each scenario task defines:

- **Goal** -- one sentence
- **Must demonstrate** -- concept under test
- **Success checklist** -- binary pass/fail
- **Forbidden workarounds** -- redesigns that dodge the concept

A session **passes** only if every success checkbox is true **without** forbidden workarounds.
A session **fails usefully** if it stops on a classified blocker with a filled template.

---

## 4. Blocker taxonomy (unified)

Every blocker gets exactly one primary bucket. Prior round findings used an older taxonomy (T/H/D/O/A/W/R/E/X); map per section 4.1.

| Code | Bucket | Meaning | Typical product response |
|------|--------|---------|---------------------------|
| **C** | Missing concept | Domain cannot express the intent at all | Design + implement concept (DSL/runtime) |
| **I** | IR-only / half-alive | IR or library exists; product path missing or dead | Finish path, hide IR, or document non-goal |
| **M** | MCP gap | Core can do it; no honest tool surface | Thin MCP adapter (not new domain semantics) |
| **G** | Guide wrong | Product works; guide/tools lie or omit | Guide / description fix |
| **A** | Analysis noise | False fail/pass, bad diagnostics | Analyzer honesty |
| **R** | Runtime surprise | Unexpected execute semantics | Runtime + test + guide |
| **S** | Agent skill | Agent misread tools/DSL; product OK | No feature; maybe guide example |
| **W** | Workaround only | Possible via ugly path | Optional ergonomics later |

### 4.1 Taxonomy crosswalk (prior round findings)

Prior reports used the orchestrator's pain taxonomy. Map to unified buckets like this:

| Old (orchestrator) | Unified (section 4) | Notes |
|--------------------|-------------|-------|
| **T** Tool gap | **M** MCP gap or **C** Missing concept | If core can do it -> M; if core can't -> C |
| **H** Honesty | **G** Guide wrong | Tool description lied or overclaimed |
| **D** DSL/guide | **G** Guide wrong | Parser vs guide mismatch |
| **O** Oracle | **M** MCP gap | Missing visibility tool |
| **A** Analysis | **A** Analysis noise | Diagnostics useless or missing |
| **W** Workflow | **W** Workaround only | Affordances or ergonomics |
| **R** Runtime | **R** Runtime surprise | Missing runtime or unexpected behavior |
| **E** Ergonomics | **W** Workaround only | Token/too-many-tools issues |
| **X** External | **S** Agent skill | Environment issues not product |

### 4.2 Scoring

Each blocker gets 1-5 on three axes. Sum = priority score. Higher = sooner.

| Axis | 1 | 3 | 5 |
|------|---|---|---|
| **Frequency** | One-off | Every multi-step session | Every tool call |
| **Blocks scenario** | Workaround exists | Painful workaround | No workaround |
| **No workaround** | Cheap product fix | Medium slice | Hard platform work |

Load-bearing findings should also include **F+B+N = ?** sum for compatibility with prior report format.

---

## 5. Report template

Write to `docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-{ID}-{YYYYMMDD}.md`

```
# Dogfood -- {Scenario ID}: {Title}

**Date:** YYYY-MM-DD
**Agent / session id:**
**Scenario file:** `simple-agent-tasks/dogfood-{id}.md`
**Result:** PASS | FAIL (blocker) | PARTIAL

## Executive (3 lines max)

- What worked:
- First hard blocker:
- Recommended product bucket (C/I/M/G/A/R/S/W):

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | ... | yes/no |
| ... | | |

## Timeline (short)

1. get_dsl_guide -- ...
2. apply_dsl / evolve -- ...
3. runtime -- ...
4. stopped because -- ...

## Blockers (one section each)

### B1 -- {title}

| Field | Value |
|-------|--------|
| Bucket | C / I / M / G / A / R / S / W |
| Score | F{?}+B{?}+N{?} = {?} |
| Goal step | |
| Tried | |
| Error / behavior | |
| Smallest product fix | |
| Workaround | none / describe |

## What worked (keep)

- ...

## Suggested backlog rows

| Priority | Bucket | One-line work item |
|----------|--------|--------------------|
| 1 | | |

## Out of scope observed (do not act this session)

- codegen / packs / RestApi / ...
```

Machine-readable companion: same basename `.json` with array of `{id, title, bucket, score, goalStep, tried, error, productFix, workaround}`.

---

## 6. Synthesis (after 2-4 scenario runs)

After Wave 1 scenarios have reports, a lead agent produces:

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-SYNTHESIS-{YYYYMMDD}.md`

Contents:
1. Ranked blockers (by score x scenario impact)
2. Cross-cutting concept clusters hit (owned/value, link graph, time, identity, contracts, honesty)
3. **Next build slice** -- one vertical from the backlog with estimated size
4. Explicit non-actions (what will *not* be built next)

---

## 7. Parallelism

| Mode | When |
|------|------|
| **Serial** | First pass on a new scenario (cheaper, cleaner signal) |
| **Parallel** | Same scenario, 2-3 agents, independent sessions -- merge blockers by title/bucket |
| **Not parallel** | Different scenarios in one agent brain without templates |

Cap: **3 scenarios x <=2 agents** per round unless a human is synthesizing the same day.

---

## 8. Relationship to other tracks

| Track | Dogfood role |
|-------|----------------|
| Effect surface | Link/unlink, invoke, TransitionRelationship honesty |
| Query surface | Q4/dates only if a scenario fails without them |
| DAU / codegen | **Parked** -- out of dogfood scope |
| MCP expansion | Micro-tools only after **M** bucket repeats |

---

## 9. Success of the *program* (not one session)

A synthesis cycle is successful when it produces:

1. A ranked list of blockers with unified taxonomy and scores
2. A recommendation for the **thinnest vertical** that unblocks the top-scoring scenario
3. Explicit identification of what is **not** being built next
4. Archived evidence in the reports folder for future reference
