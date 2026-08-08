# MCP Dogfood Orchestrator — Supervisory Agent Plan

**Date:** 2026-07-18 (superseded 2026-07-25)  
**Status:** **Historical** — two rounds complete; Wave 1 now uses [`mcp-dogfood-protocol.md`](mcp-dogfood-protocol.md) as single source of truth.

This document describes the methodology for the July 18 dogfood rounds. It is preserved for context but **superseded**. The protocol doc defines the unified taxonomy (section 4), report template, and synthesis process for all future rounds. New agents should read the protocol, not this file.

## Crosswalk to protocol

| This doc | Protocol equivalent |
|----------|---------------------|
| Pain taxonomy T/H/D/O/A/W/R/E/X | Unified taxonomy C/I/M/G/A/R/S/W (see protocol section 4.1 for mapping) |
| PainScore = 2xSeverity + Frequency + Blocker + CheapnessInverse | Score = Frequency + BlocksScenario + NoWorkaround (each 1-5) |
| Supervisor spawns child agents | Serial scenario execution per protocol section 2 + 7 |  

---

## 1. Why this program exists

Phase 2 (spawn-and-wire) and Phase 3 thin (oracle + suggestions + DSL guide) closed planned gaps. Expansion plan remaining items are **pull-only with pain**. This program **manufactures honest pain evidence** by having agents act as first customers of MCP.

```text
Supervisor
  → launch mission agents (parallel where independent)
  → each agent exercises a path of the MCP surface
  → structured Pain Findings
  → supervisor ranks, dedupes, maps to backlog buckets
  → human / planning agent updates expansion §0 pick order
```

**Success is a ranked pain list with repro steps**, not new tools.

---

## 2. Non-goals (supervisor enforces)

| Do **not** | Why |
|------------|-----|
| Implement product fixes mid-run | Contaminates pain evidence; save for after ranking |
| Expand lab DSL (`actor`, `value`, `invoke`, `require { expr }`) | Product guide forbids; false pain |
| Claim Runtime MCP (CallAction / store / instances) exists | **Model-only** session today — missing runtime is a valid **finding**, not a bug in a missing tool |
| Event authoring tools | Retired product path |
| “Complete the subsystem” explorations | Principles: shipped capability + smallest coherent slice |
| Infinite retries on parse failure without logging | Capture first failures — they are data |

---

## 3. Preconditions (supervisor checks before Wave 0)

- [ ] MCP server starts (`Poly.Mcp`) and tools list includes at least:  
  `create_domain_session`, `get_dsl_guide`, `apply_dsl`, `export_dsl`,  
  `add_*` / `remove_*` micro-tools, `add_policy` / `evaluate_policy` / `simulate_policy`,  
  `lower_expression`, `describe_expression`, `describe_domain_element`,  
  `get_domain_analysis`, `get_domain_suggestions`, query tools  
- [ ] `get_dsl_guide` returns product surface (mentions `domain`, `entity`, `require` **policy names**, not lab `actor` as supported)  
- [ ] Branch builds; suite green optional but preferred  
- [ ] Working directory / session isolation: each child uses **its own** `sessionId` (no shared mutation races)  
- [ ] Output dir for findings: `docs/plans/v2-to-v3/agent-summaries/dogfood/` (create if missing)

If preconditions fail → **stop**; report environment pain, do not invent domains.

---

## 4. Roles

### 4.1 Supervisor (orchestrator)

**Owns:** mission assignment, parallelism, timeouts, de-duplication, scoring, final report, plan write-back proposal.

**Does:**

1. Verify preconditions (§3).  
2. Assign Wave 0–N child missions with **frozen prompts** (§6–7).  
3. Collect `PainFinding` JSON/MD (§8).  
4. Run ranking rubric (§9).  
5. Produce `DOGFOOD-REPORT.md` + optional PR comment / plan diff proposal for expansion §0.  
6. Escalate only environment blockers to human.

**Does not:** author domains itself except a 60s connectivity smoke; does not implement Poly code.

**Budget defaults (tune per host):**

| Control | Default |
|---------|---------|
| Max wall-clock total | 2–4 hours human-equivalent agent time |
| Max tool calls per child | 80 |
| Max `apply_dsl` attempts per child | 12 |
| Child timeout | 25–40 min |
| Parallel children | 3–5 (Wave 1); serial if MCP session store is process-global and flaky under load |

### 4.2 Child agents (mission specialists)

Each child is **stateless w.r.t. other children**, gets:

- Mission ID + goal story  
- Allowed tool classes  
- Forbidden actions  
- Required output schema  
- Success criteria for the *mission* (completed exercise), separate from product pain  

Children **must** call `get_dsl_guide` before first large `apply_dsl`.

---

## 5. Pain taxonomy (fixed labels)

Every finding uses exactly one primary `category` (and optional secondary):

| Code | Category | Typical evidence |
|------|----------|------------------|
| **T** | Tool gap | Needed capability missing from MCP (e.g. CallAction, remove_constraint) |
| **H** | Honesty / description | Tool description lied or overclaimed; silent ignore; wrong Success |
| **D** | DSL / guide | Parser vs guide mismatch; export drift; inventable syntax traps |
| **O** | Oracle / visibility | Could not inspect lower/describe/simulate enough to correct |
| **A** | Analysis / suggestions | Diagnostics useless, missing, or suggestions wrong/noisy |
| **W** | Workflow / affordances | Affordances wrong; replace vs merge surprise; session confusion |
| **R** | Runtime absence | Model OK but cannot exercise spawn-and-wire **inside** MCP |
| **E** | Ergonomics / tokens | Too many tools; huge payloads; guide too long; snapshot overload |
| **X** | External / env | Server crash, tool not registered — not product roadmap |

---

## 6. Scoring rubric (supervisor)

Score each finding **1–5** on each axis; **PainScore = 2×Severity + Frequency + Blocker + CheapnessInverse** (max 20):

| Axis | 1 | 3 | 5 |
|------|---|---|---|
| **Severity** | Annoyance | Wrong model / wasted loop | Cannot complete real task |
| **Frequency** | One-off | Every multi-step session | Every tool call |
| **Blocker** | Workaround exists | Painful workaround | No workaround in MCP |
| **CheapnessInverse** | Hard platform work | Medium slice | Likely small honesty/tool fix |

**Rank order:** PainScore desc, then Severity, then category priority for product:

```text
R/T (runtime or true tool gap) > H (honesty) > D (DSL) > O/A > W/E > X
```

Map top items to expansion buckets:

| Finding category | Backlog bucket (expansion §0) |
|------------------|-------------------------------|
| O | V1/S1 visibility / debug |
| A (structured) | Full suggestions / acceptTool |
| T effect edit | Effect micro-tools |
| T remove_constraint | Constraint remove |
| R | Runtime MCP (RT.*) |
| D | Guide/parser honesty (not new IR) |
| H | Tool description / fail-loud fixes |
| W | Affordance / dual-path docs |

---

## 7. Mission roster (children)

### Wave 0 — Connectivity (supervisor or one child)

| Mission | Goal | Tools | Exit |
|---------|------|-------|------|
| **W0.1 Smoke** | Create session, overview, guide, trivial apply | session, query, `get_dsl_guide`, `apply_dsl` | sessionId works; guide non-empty |

If W0 fails → halt program.

### Wave 1 — Parallel discovery (default 4 children)

#### C1 — Batch DSL author (`dsl-batch`)

**Story:** Build a small multi-entity domain (e.g. Order/Customer with stages, nav, policy, one action with `require` + `transition`) using **only** guide + `apply_dsl` / `export_dsl`.

**Must:**

1. `get_dsl_guide`  
2. Draft `.poly` from guide only (no experiment docs)  
3. `apply_dsl` → `get_domain_analysis` → fix up to budget  
4. `export_dsl` → compare mental model → re-apply if needed  
5. `get_domain_suggestions` once domain has props without stages (or inverse)

**Probe for pain:** parse errors, replace surprise, export round-trip drift, guide lies, missing constraints/effects syntax.

**Forbidden:** micro-tools for structure (use only if apply_dsl impossible after 3 failures — then file **W** finding).

#### C2 — Incremental micro-tools (`micro-incremental`)

**Story:** Same domain shape as C1, built **only** with `add_*` / `remove_*` / constraints / `add_policy` — no `apply_dsl` until optional export check at end.

**Must:** entity → properties → stages → stage actions → relationship → policy → analysis → suggestions.

**Probe for pain:** missing effect micro-tools, cannot set entry/exit/create-in without DSL, remove_* honesty, batch tools, affordances.

**Forbidden:** `apply_dsl` until final optional `export_dsl` snapshot (if export fails, file **T/D**).

#### C3 — Oracle / policy loop (`oracle-policy`)

**Story:** Author or reuse a policy; use **oracle tools** to understand and verify **before** trusting evaluate.

**Must:**

1. Session + entity with numeric/bool props  
2. Compose expression JSON for `simulate_policy` (pass + fail bags)  
3. `lower_expression` + `describe_expression` on same JSON  
4. `add_policy` + `evaluate_policy` vs `simulate_policy` consistency  
5. `describe_domain_element` for entity, stage, policy (try ambiguous names if multi-entity)

**Probe for pain:** JSON expression ergonomics, simulate vs evaluate mismatch, describe insufficient, multi-match describe, no debug step-through.

#### C4 — Repair / adversarial (`repair-adversarial`)

**Story:** Start from a **broken** domain (deliberate mistakes) and repair using analysis + suggestions + dual path.

**Inject (examples):**

- Entity with properties, no stages  
- Policy JSON that fails parse  
- Duplicate stage names across entities  
- `apply_dsl` with one lab construct (`actor` or `require { x > 0 }`) to confirm fail-loud  
- Remove something referenced (relationship target)

**Must:** log first error messages; attempt repair via documented tools only.

**Probe for pain:** diagnostic quality, suggestion usefulness (DMAS001-only), fail-loud gaps, recovery affordances.

### Wave 2 — Targeted follow-ups (supervisor picks 1–3 after Wave 1)

Spawn only if Wave 1 ranked a cluster without enough evidence:

| Mission | When | Goal |
|---------|------|------|
| **C5 Dual-path switch** | C1 and C2 both pain on handoff | export from micro → edit → apply_dsl replace risk |
| **C6 Constraint stress** | constraint churn | add/list constraints; attempt remove (expect gap) |
| **C7 Effect depth** | create-in / entry/exit via DSL | apply guide-level effects; note no MCP runtime exercise |
| **C8 Token pressure** | E findings | snapshot + guide size; count tools; note overload |

### Wave 3 — Runtime gap confirmation (single child, explicit)

#### C9 — Runtime absence (`runtime-gap`)

**Story:** Domain models spawn-and-wire (create in + when). Attempt to **run** CallAction / create instance / observe subscription **via MCP**.

**Expected:** **cannot** — file high-severity **R** finding with exact “tools I looked for / what I tried.”

**Must not** implement runtime MCP; only document the hole.

---

## 8. Finding schema (required output per child)

Each child writes one file:

`docs/plans/v2-to-v3/agent-summaries/dogfood/{missionId}-{utcDate}.md`

And appends one JSON object to a shared rollup if the host supports it (else supervisor copies):

```json
{
  "missionId": "C1-dsl-batch",
  "agentId": "optional",
  "sessionIds": ["..."],
  "completedMission": true,
  "toolCallCount": 0,
  "findings": [
    {
      "id": "C1-F1",
      "title": "Short name",
      "category": "T|H|D|O|A|W|R|E|X",
      "severity": 1,
      "frequency": 1,
      "blocker": 1,
      "cheapnessInverse": 1,
      "painScore": 0,
      "repro": [
        "1. create_domain_session",
        "2. ..."
      ],
      "toolsInvolved": ["apply_dsl", "get_domain_analysis"],
      "expected": "what agent needed",
      "actual": "what happened (quote messages)",
      "workaround": "none | description",
      "suggestedBacklogBucket": "V1|S1|effect-micro|remove_constraint|Runtime-MCP|guide-honesty|affordance|other",
      "quotes": ["raw tool message snippets"]
    }
  ],
  "notes": "freeform; no product patches"
}
```

**Markdown body** must include: mission summary, what worked, findings table, raw interesting tool transcripts (trimmed).

---

## 9. Supervisor algorithm

```text
1. Preflight (§3)
2. Run W0.1
3. Launch Wave 1 (C1–C4) in parallel if safe; else sequential C1 → C2 → C3 → C4
4. For each child:
     - enforce budget / timeout
     - require schema-valid findings (even empty findings[] with completedMission)
5. Normalize:
     - merge duplicate titles (same category + same tools + similar repro)
     - compute painScore if missing
6. Optional Wave 2 from top clusters lacking evidence
7. Always run C9 if no R finding yet (confirm runtime gap is explicit)
8. Emit DOGFOOD-REPORT.md:
     - Top 10 ranked pains
     - Map to expansion §0 buckets
     - “Do not build” list (X, one-offs, already fixed)
     - Recommended single next product slice (one line)
9. Propose plan edits (do not auto-merge product code):
     - mcp-tool-surface-expansion.md pick order
     - master-roadmap “What next” one-liner
```

### 9.1 Recommended next-slice rule

Supervisor’s final recommendation must pick **exactly one**:

```text
IF top pain is R and dogfood needs instances in MCP → Runtime MCP thin vertical
ELSE IF top is O and policies still blind after V0/S0 → V1/S1
ELSE IF top is T and agents cannot edit effects without full apply_dsl → effect micro-tools
ELSE IF top is H/D with clear small fix → honesty/guide/parser fix slice
ELSE IF top is A and text hints useless → structured suggestions
ELSE → stop; more dogfood or human product choice
```

Never recommend more than one primary slice.

---

## 10. Frozen child prompt skeleton

Supervisor pastes this into each child (fill `{...}`):

```text
You are a Poly.Mcp dogfood agent. Mission: {missionId} — {story}

Rules:
1. Use ONLY MCP tools from the connected Poly.Mcp server.
2. Before first large apply_dsl, call get_dsl_guide. Obey it. No lab DSL (actor, value, invoke, require { expr }).
3. Each structural domain uses your own sessionId. Do not assume other agents' sessions.
4. Do NOT modify Poly source code or plans except writing your finding file under
   docs/plans/v2-to-v3/agent-summaries/dogfood/
5. On failure: log exact tool name, args summary, Success, Message. Retry at most twice per distinct error class.
6. Budget: max {N} tool calls. Prefer finishing with findings over perfect domain.
7. Output the JSON schema findings + markdown report (§8 of mcp-dogfood-orchestrator.md).

Mission checklist:
{bullet checklist from §7}

Success for YOU = checklist attempted + findings filed, not “domain is beautiful.”
```

---

## 11. Supervisor final report template

Write: `docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-{date}.md`

```markdown
# MCP Dogfood Report — {date}

## Executive recommendation
**Next product slice:** …
**Why (top finding):** …
**Not next:** …

## Ranked pains (Top 10)
| Rank | ID | Score | Cat | Title | Backlog bucket |
|------|-----|-------|-----|-------|----------------|

## What worked (keep)
- …

## Coverage matrix
| Mission | Completed | Finding count | Notes |
|---------|-----------|---------------|-------|

## Evidence links
- C1: path…
```

---

## 12. Human / planning handoff

After report exists:

1. Human accepts or overrides **one** next slice.  
2. Open or update execution tasks in:  
   - [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0 pick order  
   - and the matching detail plan (`mcp-phase3-oracle-surface` for V1/S1; new thin plan for Runtime MCP if chosen)  
3. File individual vs-* microtasks only **after** slice chosen (don’t explode dogfood into 20 tasks up front).

---

## 13. Exit criteria for this program

- [x] W0 + Wave 1 complete (or documented environment abort)  
- [x] ≥1 finding per completed mission (or explicit "no pain" with coverage notes)  
- [x] C9 runtime absence either confirmed as **R** or waived with reason  
- [x] `DOGFOOD-REPORT` with single recommended next slice  
- [x] Expansion §0 pick order updated **or** explicit "no change — more dogfood"  

**Program fails** if supervisor ships code fixes instead of a report, or if children invent lab DSL as “success.”

---

## 14. Suggested spawn order (copy-paste)

```text
Supervisor start
  → W0.1 smoke
  → parallel: C1 dsl-batch | C2 micro-incremental | C3 oracle-policy | C4 repair-adversarial
  → score + dedupe
  → optional C5–C8
  → C9 runtime-gap (if no R yet)
  → DOGFOOD-REPORT + plan write-back proposal
  → STOP (human picks implementation)
```

---

## 15. Notes for hosts without multi-agent

If only one agent: run missions **sequentially** as persona switches (C1 then C2…), same schemas and budgets. Supervisor checklist still applies to the single agent’s “phases.”
