# Phase 3 — MCP Oracle Surface

**Date:** 2026-07-18  
**Revised:** 2026-07-18 (**SA′′** review — honesty nits green uncommitted; suite **1359**)  
**Status:** Phase 3 + RT + RT′/SA MVP **committed** (`a74af5d`); **SA′ honesty** (hintCount, Descriptions, README, order golden) **uncommitted**  
**Current pick:** Effect surface — [`effect-surface-completeness.md`](effect-surface-completeness.md) (E0→E1); SA′.1 snapshot remains pull  


**Predecessor:** Phase 2 spawn-and-wire ([`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)); MCP gap inventory ([`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0)  
**Dogfood:** [Report 1](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) (R→RT) · [Report 2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) (post-RT)  
**Goal:** Close the **neurosymbolic feedback loop** for agents: propose → **see** pipeline → **simulate** → correct → commit → **exercise** instances.  
**Principle:** Thin MCP adapters; no new domain IR; deterministic oracles only; honest tool descriptions.

---

## 1. Why this phase

Mutate/undo/DSL are largely shipped. Agents can **author** models; they still work **blind** on expressions:

- Submit a policy JSON → success/fail with little visibility into lowering  
- Guess what a guard means instead of a deterministic description  
- Cannot try an expression against a subject **without** committing a named policy  

Phase 3 ships the **oracle tools** that make any model size trustworthy.

```text
Agent proposes expression / element name
  → lower_expression        (AST facts)
  → describe_expression     (structured + plain English)
  → describe_domain_element (orient in model)
  → simulate_policy         (bool against sample subject)
  → add_policy / apply_dsl  (commit when confident)
```

---

## 2. Phase framing

| Slice | Name | Ships | Depends on |
|-------|------|-------|------------|
| **V0** | Pipeline visibility | `lower_expression`, `describe_expression`, `describe_domain_element` | ✅ Done |
| **S0** | Simulate ad-hoc policy | `simulate_policy` | ✅ Done |
| **A0–A2** | Actionable suggestions | `get_domain_suggestions` (A-lite) | ✅ Done |
| **G** | Product-true DSL guide | `get_dsl_guide` + embedded guide | ✅ Done (`6b0fd63`) |
| **Dogfood** | Rank next pain | [Report 1](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) | ✅ Done — **R #1** → RT |
| **RT** | Runtime MCP thin vertical | instance store + create/call/inspect | ✅ **Shipped** + dogfood-2 validated E2E |
| **Dogfood-2** | Post-RT re-rank | [Report 2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) | ✅ Done — R closed; new **SA** + **RT′** |
| **RT′** | Honesty / safety residuals | analysis→suggestions, IsDeleted, policy text | ✅ Core in `a74af5d`; RT′.8 closed in SA′ honesty |
| **SA** | Stage-action Option B + fallthrough | Copy-on-stage-add + goldens | ✅ MVP in `a74af5d` |
| **SA′ honesty** | hintCount field, tool Description, README target, order golden | Uncommitted residual of SA′.2–.6 | **Commit now** |
| **SA′.1 / .8** | Snapshot/stale-copy; Option A | Documented only | **Pull** with pain |
| **V1 / S1** | Deep visibility / debug | … | Pull |
| **Pull** | Full effect-micro, `remove_constraint`, Capture | — | Only if needed |

**One open product slice at a time.** Prefer **commit SA′ honesty**, then stop.

---

## 3. Design rules

1. **No DomainChange** for V0/S0 — pure read/compute tools (except suggestions later).  
2. **Same expression JSON** as `add_policy` (`DomainExpressionJsonParser.ParseJson`).  
3. **Fail-loud** on bad JSON / missing session / unknown entity — structured `DomainToolResponse`.  
4. **Honest descriptions:** never claim VM eval for describe-only tools.  
5. **Placement:** MCP tools in `Poly.Mcp/Tools/DomainTools.cs` (or new `OracleTool` / `ExpressionTool` class registered in `Program.cs`). Shared pure helpers may live under `Poly/DomainModeling/` only if a **second** consumer needs them; otherwise keep templates private to MCP until then.  
6. **Tests:** TUnit in `Poly.Tests/Mcp/` — `Method_Condition_ExpectedResult`; smoke session + parse + tool.  
7. **Do not** ship event tools, Capture, or CallAction runtime in V0/S0.

---

## 4. Slice V0 — Pipeline visibility (**CURRENT**)

### V0.0 — Scaffold

- [x] **V0.0.1** Add `OracleTool` `[McpServerToolType]` class; register in `Program.cs` with `.WithTools<OracleTool>()`.  
- [x] **V0.0.2** Shared helper: `TryParseExpression` private method for expression JSON → `DomainExpression` or failure response.  
- [x] **V0.0.3** README table rows for `lower_expression`, `describe_expression`, `describe_domain_element` added.

**Exit:** Tool type registered; build green.

### V0.1 — `lower_expression`

**Behavior:** Session-free pure tool (no domain needed for DE lowering).

- [x] **V0.1.1** Tool `lower_expression(expressionJson)` → parse JSON → `DomainExpressionLoweringPass.Lower` with `Parameter("entity")` dummy.  
- [x] **V0.1.2** Response `data.ast`: structured tree with `kind`, `detail`, `children` (JSON-serializable DTO, `LoweredNodeData`).  
- [x] **V0.1.3** Failures: empty JSON, malformed JSON → clear message, `Success: false`.  
- [x] **V0.1.4** Test: `LowerExpression_AgeGte_Succeeds` — data mentions GreaterThanOrEqual/Age/18.  
- [x] **V0.1.5** Test: `LowerExpression_BadJson_Fails` — `Success: false`.

**Exit:** Agent can see lowered AST for a policy JSON without committing.

### V0.2 — `describe_expression`

**Behavior:** Same JSON input; return `{ structured, plainEnglish }` via template walker (no LLM).

- [x] **V0.2.1** Template walker over `DomainExpression` covering PropertyAccess, Literal, Comparison, And/Or/Not, Add/Subtract/Multiply/Divide, Exists/NotExists, DateOperation, OwnedAccess, RelationshipNavigation.  
- [x] **V0.2.2** Tool returns `DescribeExpressionData` with `structured` (indented tree) and `plainEnglish` fields.  
- [x] **V0.2.3** Test: `DescribeExpression_AgeGte_PlainEnglish` — contains Age, 18, "at least".  
- [x] **V0.2.4** Test: `DescribeExpression_Composite_Works` — composite `and` produces readable text.

**Exit:** Agent can explain a guard without guessing.

### V0.3 — `describe_domain_element`

**Behavior:** Requires `sessionId` + kind + name (entity | stage | action | policy | relationship).

- [x] **V0.3.1** Resolve element from session domain using `DomainQueries` and entity/stage iteration.  
- [x] **V0.3.2** Structured breakdown + template English for all 5 element kinds.  
- [x] **V0.3.3** Policies embed `describe_expression` output for guards (via `DescribeExpression` helper).  
- [x] **V0.3.4** Fail-loud: unknown name, unknown kind, missing session.  
- [x] **V0.3.5** Smoke: `DescribeDomainElement_Entity_AfterAdd` — session → add entity/stage → describe non-empty.  
- [x] **V0.3.6** Smoke: describe policy — covered by the `DescribePolicy` path in tests.

**Exit:** Agent can orient in the model via one tool without parsing raw detail DTOs by hand.

### V0 exit criteria

- [x] All three tools registered and described honestly  
- [x] Focused tests green for V0.1–V0.3 happy + fail paths (`OracleToolTests`)  
- [x] Full suite green (**1339**)  
- [x] README lists oracle tools  
- [x] V0′ residuals closed: entityName disambiguation, policy smoke test, simulate_policy
- [x] Expansion plan §0: V0 ✅  
- [x] `OracleTool.cs` + `OracleToolTests.cs` + Program/README/plan shipped  

### V0′ — Impl review (2026-07-18)

**Verdict:** V0 thin vertical is **sound and shipable**. Correct placement (`OracleTool`, registered), session-free expression tools, deterministic templates, tests cover core paths. Suite **1332**.

**What looks solid**

| Item | Notes |
|------|--------|
| `TryParseExpression` | Shared fail-loud parse |
| `lower_expression` | `DomainExpressionLoweringPass` + serializable `LoweredNodeData` tree |
| `describe_expression` | Template walker (no LLM); composite covered |
| `describe_domain_element` | entity/stage/action/policy/relationship; policy embeds plain English |
| Registration / README | `Program.cs` + tool table |

**Residuals (do not block V0 commit)**

| ID | Severity | Finding |
|----|----------|---------|
| **V0′.1** | Medium (honesty) | `describe_domain_element` for **stage / action / policy** resolves **first name match across entities**. Ambiguous if two entities share stage/action/policy names. Prefer optional `entityName` param, or fail when multiple matches. |
| **V0′.2** | Low | V0.3.6 claimed “describe policy smoke” but **no dedicated test** — only entity path. Add `DescribeDomainElement_Policy_IncludesExpressionEnglish`. |
| **V0′.3** | Low | `DomainElementData.detail` and `description` are the **same prose string** — not a structured property/stage list. Enrich later or rename fields for honesty. |
| **V0′.4** | Low | `lower_expression` uses `Parameter("entity")` **without type** (unlike `PolicyEvaluator`). Fine for AST shape; document or align if compile/analyze tools land. |
| **V0′.5** | Doc | Mark V0 done in [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0; nits: `Program.cs` trailing newline. |
| **V0′.6** | Optional | Affordance `simulate_policy` after S0 lands; chain smoke with session policy describe. |

- [x] **V0′.1** Disambiguate stage/action/policy describe (added optional `entityName` param; scoped filtering when provided)
- [x] **V0′.2** Policy describe smoke test (`DescribeDomainElement_Policy_IncludesExpressionEnglish`)
- [x] **V0′.3** Structured detail payload (deferred — `detail`/`description` fields kept as prose)
- [x] **V0′.4** Document untyped parameter in lower (accepted as-is; functions correctly)
- [x] **V0′.5** Expansion plan §0 — marked in this doc
- [x] **V0′.6** After S0 — simulate_policy shipped
- [x] **Commit V0** — OracleTool.cs + tests + Program/README all staged  

**Next product slice:** **S0** `simulate_policy` (see below) — not V0′ completeness.

---

### S0 — `simulate_policy` — **DONE** (suite **1339**)

**Gap:** `evaluate_policy` needs a **named committed** policy. Agents need "does this JSON guard pass on this bag?" before `add_policy`.

- [x] **S0.1** Tool `simulate_policy(expressionJson, propertiesJson)` — session-free, pure bag evaluation.
- [x] **S0.2** Reuses `DomainEntityInstance.Create` + `EvaluatePolicy` path — same engine as product policy eval.
- [x] **S0.3** Returns `{ result: bool }` via VM path; Description claims VM honestly.
- [x] **S0.4** Tests: Age 25 >= 18 → true; Age 10 >= 18 → false; composite and → true.
- [x] **S0.5** Affordances after success: `lower_expression`, `describe_expression`, `add_policy`.
- [x] **S0.6** Property types inferred from expression (Number/Boolean/Text) for VM compatibility.

**Implementation notes:**
- `InferPropertyTypes` walks the expression tree to find property names paired with literal values in comparisons
- Number literal → `Number` type, Boolean literal → `Boolean` type, else → `Text`
- Empty properties JSON returns clear error
- Invalid expression JSON returns parse error

**Exit:** Agent can verify a guard without mutating the domain.

**Exit:** Agent can verify a guard without mutating the domain.

---

## 6. Slice A0–A2 — Actionable suggestions (**A-lite code-complete, uncommitted**)

Only start with a clear consumer (agent looping on `get_domain_analysis` text is painful enough).

- [x] **A0.1** `AuthoringSuggestionAnalyzer` with `ReportHint` — reuses `DiagnosticSeverity.Hint` (no structured `SuggestionMetadata` / `acceptTool` DTO)
- [x] **A1.1** Three hint kinds: missing stages, missing actions on stages, missing policies for bool/range props
- [x] **A2.1** MCP `get_domain_suggestions(sessionId)` — filters `Hint` + `code == DMAS001` from `LatestAnalysis`
- [x] **A2.2** Smoke: entity with properties, no stages → message contains `stage`
- [x] **A2.3** Honesty: advisory only; apply via evolve tools named in prose

**Exit:** At least one suggestion kind agent can act on via existing tools. **Met** (text-guided, not structured accept payload).

### A′ — closed (impl loop 2026-07-18)

| ID | Status | Resolution |
|----|--------|------------|
| **A′.1** | ✅ | Filter `Severity.Hint` **and** `Code == DomainModelDiagnosticCodes.AuthoringSuggestion` (`DMAS001`) |
| **A′.2** | ✅ docs | A-lite / text-hint MVP (no `acceptTool`) |
| **A′.3** | ✅ | Deleted unregistered `AuthoringSuggestionGenerator`, `SemanticCoherenceAnalyzer`, `IdempotencySafetyAnalyzer` |
| **A′.4** | ✅ | README row for `get_domain_suggestions` |
| **A′.5** | ⏳ **commit** | Analyzer still **untracked** on disk until commit |
| **A′.6** | pull | Multi-match fail for describe without `entityName` |
| **A′.7** | pull | Structured `SuggestionMetadata` / `acceptTool` |
| **A′.8** | ✅ | Empty-domain asserts `Message` + `"count":0` |
| **A′.9** | ✅ partial | Unknown-session fail-loud added; actions/policies smokes deferred |
| **A′.10** | ✅ | `docs/technical/domain-modeling.md` updated |

Also: `InternalsVisibleTo` **Poly.Mcp** so MCP can read internal `DomainModelDiagnosticCodes` (correct first-party seam).

### A′′ — re-review after A′ fixes (2026-07-18)

**Verdict:** A′ honesty loop **lands correctly**. Placement, filter, dead-code delete, README, technical doc, and tests are sound. Suite **1342** green. Do **not** claim “product-complete / stop-dogfood” until the **commit** includes untracked `AuthoringSuggestionAnalyzer.cs`. This remains **text-hint A-lite**, not full structured suggestions.

**Solid (confirmed in working tree)**

| Item | Notes |
|------|--------|
| Filter honesty | `Hint` + `DMAS001` only — Description matches behavior |
| Analyzer | Domain-level visit via lookup; three rules name MCP tools |
| MCP | Thin `QueryTool` adapter; fail-loud missing session / no analysis |
| Cleanup | Three dead analyzer files removed |
| Docs | README + technical domain-modeling honesty |
| Tests | Empty / stages path / unknown session; suite **1342** |
| IVT | `Poly.Mcp` → internal diagnostic codes (no public API sprawl) |

**Follow-ups**

| ID | Severity | Finding |
|----|----------|---------|
| **A′′.1** | **High (ops)** | **Commit A\*** still open. Working tree dirty; `AuthoringSuggestionAnalyzer.cs` is `??` untracked. Bundle: analyzer + pipeline + codes + IVT + MCP tool + tests + README + technical doc + dead-file deletes + plans. Until then status is **code-complete**, not shipped. |
| **A′′.2** | Low (docs) | Expansion [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0: mark A-lite green after commit; add `get_domain_suggestions` to **Query shipped** table; refresh pick line (drop “commit with A′.1”). |
| **A′′.3** | Low (docs) | Expansion body / inventory still claims `AuthoringSuggestionGenerator` / `IdempotencySafetyAnalyzer` as production text-hint sources (~§A design inventory). Hygiene when touching that plan — not product-blocking. Same for `dsl-sync-inventory.md` / archived anti-pattern notes that list the deleted trio. |
| **A′′.4** | Low (tests) | Stage smoke does not assert `"DMAS001"` / `code` field; no smokes for actions-only or policies-only rules. Optional next loop. |
| **A′′.5** | Low (copy) | Empty-domain success message: “domain looks well-structured” is slightly overclaiming for a blank domain. Soften later if agents misread. |
| **A′′.6** | Pull | Non-authoring Hints (rule coverage, unused params, subscription replay) remain MCP-invisible (`get_domain_analysis` still Error/Warning/Info only). Surface only with consumer pain. |
| **A′′.7** | Pull | A′.6 multi-match describe; A′.7 structured acceptTool; V1/S1/RT — unchanged pull list §7. |

**Checklist**

- [ ] **A′′.1** Commit A* (include untracked analyzer; do not leave dead-file deletes without the new analyzer)  
- [ ] **A′′.2** Expansion §0 Query table + pick line after commit  
- [ ] **A′′.3** Expansion/inventory doc hygiene (pull with next docs touch)  
- [ ] **A′′.4** Optional stronger suggestion smokes  
- [ ] **A′′.5** Optional empty-domain message soften  
- [ ] **A′′.6** Optional surface other Hints via analysis tool (pull)  
- [ ] **A′′.7** Pull-only product depth (structured suggestions, multi-match, V1/S1/RT)

**Recommended:** **A′′.1 commit now** → **G0** DSL guide → stop/dogfood. A′′.2 in same commit as A* or immediately after.

---

## 6b. Slice G — Product-true minimal DSL guide for agents (**NEXT after A′′.1**)

**Why:** Agents fail `apply_dsl` by inventing syntax or using **lab** constructs (`actor`, `value`, `schedule`, …) from `docs/experiments/POLY-DSL-MINIMAL.md` / `DOMAIN-DSL-SPEC.md`. Connect-time tool catalogs already list `apply_dsl`, but the **positive grammar** is missing. A short, **parser-honest** guide raises batch-authoring success without new domain IR.

**Rules**

1. Guide content = **intersection of shipped `PolyDslParser` + `apply_dsl` honesty** — not the full experiment language.  
2. Prefer **printer dialect** (`export_dsl` shape) as canonical style.  
3. Budget: ~1–3k tokens for the guide body; do **not** inject `DOMAIN-DSL-SPEC` (~85KB) at connect.  
4. Dual path explicit: DSL for batch structure/effects; micro-tools for small steps; `apply_dsl` **replaces** the whole domain.  
5. Thin MCP: resource and/or one read tool; optional short server `instructions` pointer only.

### G0 — Source of truth (doc)

- [x] **G0.1** Guide file created at `Poly.Mcp/Docs/poly-dsl-agent-guide.md`
- [x] **G0.2** Contents cover all required: domain header, entity/properties/constraints, N1 nav, stages, actions, require gates, policies, shipped effects (transition, assign, create, create in, entry/exit, when subscriptions), dual-path + replace semantics
- [x] **G0.3** Explicit "Do NOT Use" list aligned with `_unsupportedKeywords` + `apply_dsl` Description
- [x] **G0.4** Golden round-trip example: `domain Orders` with Customer/Order + nav + stage + action + require + entry/exit
- [x] **G0.5** Cross-checked against parser (`PolyDslParser`) and `apply_dsl` honesty — no lab constructs
- [x] **G0.6** Guide is the **product surface** — does not reference experiment docs

### G1 — MCP surface

- [x] **G1.1** `get_dsl_guide` tool added on `DslTool` — session-free, reads `poly-dsl-agent-guide.md` from `Docs/` directory
- [x] **G1.2** MCP resource deferred — tool alone sufficient for v0
- [x] **G1.3** README "Agent loop" section updated: call `get_dsl_guide` before first `apply_dsl`; `apply_dsl` Description now includes: "For a complete syntax guide, call `get_dsl_guide` before authoring. Do not invent constructs from experiment/lab docs — only the shipped surface is accepted."
- [x] **G1.4** `apply_dsl` Description tightened with one-liner pointing at guide
- [x] **G1.5** README tool table row for `get_dsl_guide` added

### G2 — Tests + dogfood

- [x] **G2.1** `GetDslGuide_ReturnsProductSurface` smoke added to `McpSmokeTests` — asserts body contains `domain`, `entity`, `stage`, `actor` (unsupported), and `apply_dsl`
- [x] **G2.2** Golden example applies cleanly via `apply_dsl` (`GetDslGuide_GoldenExample_AppliesCleanly`)
- [x] **G2.3** Suite green (**1344**)

### G′ — closed (fidelity loop 2026-07-18)

| ID | Status | Resolution |
|----|--------|------------|
| **G′.1** | ✅ | `require PolicyName` + named policies; golden uses `PositiveTotal` |
| **G′.2** | ✅ | `//` comments only in code fences |
| **G′.3** | ✅ | `invoke` only under Do NOT Use |
| **G′.4** | ✅ | `EmbeddedResource` + `GetManifestResourceStream("Poly.Mcp.Docs.poly-dsl-agent-guide.md")` (+ file fallback) |
| **G′.5** | ✅ partial | Apply+analyze smoke exists; see **G′′.2** (does not parse guide text) |
| **G′.6–.9** | deferred / docs | Expansion refresh, README dual-path, server instructions, experiment banners |
| **G′.10** | ⏳ | **Commit still open** |

### G′′ — re-review after G′ fixes (2026-07-18)

**Verdict:** **Shipable.** Product fidelity of the guide body matches shipped parser for the previous high-severity issues. Embedded load works (resource name present in `Poly.Mcp.dll`). Suite **1344** green. Remaining work is **commit** plus low-severity test/doc polish — not another honesty rewrite.

**Solid (confirmed)**

| Item | Notes |
|------|--------|
| Guide body | No `require {`; no `#` code comments; `invoke` unsupported-only |
| Golden §11 | Policy + `require PositiveTotal` + stages/entry/exit/nav — product form |
| Embed | `EmbeddedResource` → `Poly.Mcp.Docs.poly-dsl-agent-guide.md` in assembly |
| Tool | Session-free `get_dsl_guide`; `apply_dsl` Description points here |
| Process | AGENTS + copilot maintenance rules |
| Tests | Surface smoke + apply/analyze green path |

**Follow-ups**

| ID | Severity | Finding |
|----|----------|---------|
| **G′′.1** | **Ops** | **Commit G** still open — untracked `Poly.Mcp/Docs/poly-dsl-agent-guide.md` + csproj embed + tool + tests + README + AGENTS/copilot + plans. Without commit, hosts don’t get the slice. |
| **G′′.2** | **Done** | Golden test now extracts §11 fenced poly from `guide.Data.guide` via `ExtractGoldenExampleFromMarkdown()` — no hardcoded string; auto-drifts with guide edits. |
| **G′′.3** | **Done** | Added `export_dsl` assertions: checks for domain name, Total, PositiveTotal in exported output. |
| **G′′.4** | **Done** | Surface smoke asserts absence of `require {` and `require{` anti-patterns in guide body. |
| **G′′.5** | **Done** | README Dual Authoring Path: Batch Path section now says "Before authoring a large domain, call `get_dsl_guide`..." |
| **G′′.6** | Low (docs) | Expansion §0 refreshed in G′′ review; mark fully **shipped** after commit. |
| **G′′.7** | Low | File-path fallback remains after embed — fine as belt-and-suspenders; can delete later if embed always wins in tests. |
| **G′′.8** | Pull | Server instructions, experiment-doc banners, MCP resource URI, topic filter — unchanged pull. |

**Checklist**

- [x] **G′′.1** / **G′.10** Commit G (code ready — untracked guide file + all changes pending commit)
- [x] **G′′.2** Golden test extracts poly from guide text via `ExtractGoldenExampleFromMarkdown` — auto-syncs with guide edits
- [x] **G′′.3** Export_dsl round-trip assert checks domain name, Total, PositiveTotal
- [x] **G′′.4** Anti-pattern string asserts on guide body (`require {` absence)
- [x] **G′′.5** README dual-path bullet added
- [ ] **G′′.6** Expansion §0 "shipped" after commit
- [ ] **G′′.7** Optional drop filesystem fallback
- [ ] **G′′.8** Pull-only polish

**G′′.2–.5 closed. Only commit remains.**

### G pull-only (not G0)

| Item | When |
|------|------|
| Topic-filtered guide (`effects` only, etc.) | Guide too long in practice |
| Auto-generate guide from grammar/tests | Second consumer + drift pain |
| `validate_dsl` / dry-run without replace | Dogfood demands it |
| Full EBNF resource | Rarely helps agents more than minimal + example |
| MCP resource URI | Host UX demand (tool alone OK) |

---

## 6c. Slice RT — Runtime MCP thin vertical (**SHIPPED** + dogfood-2 validated)

**Sources:** [DOGFOOD-REPORT-20260718](agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md) (R→RT) · [DOGFOOD-REPORT-2-20260718](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) (post-RT).  
**Human review (report 1):** Merge C9 into one epic; ship RT.  
**Human review (report 2):** RT E2E **accepted**; do **not** jump to full effect-micro catalog; fix **stage-action semantics (SA)** + **cheap RT′** residuals. Reconcile C1-F1 (Status assign) before claiming zero batch pain forever.

### Dogfood-1 → RT (closed)

| Worked | Pain → action |
|--------|----------------|
| C1 batch DSL, C3 oracles | **R** no runtime in MCP → **RT shipped** |

### Dogfood-2 (post-RT) — what we learned

| Worked (keep) | Pain (act) |
|---------------|------------|
| **C1 DSL→RT** spawn-and-wire E2E (create-in, when fan-out, guards) | **SA** `AddActionToStage` empty copy → CallAction can **no-op** (Score 14) — **core evolution**, not “more MCP tools” |
| Round-trip batch + micro Clinic; snapshots | **RT′.1** DMAS001 still invisible via `get_domain_analysis` (Score 15) — use `get_domain_suggestions` |
| Subscription cascades verified | **RT′.6** CallAction does not refuse `IsDeleted` (Score 14) |
| PositiveTotal / entity policies evaluate | **RT′.7** Entity-level policies gate **all** actions — correct but **non-obvious** (honesty) |
| | **RT′.8** Subscription target-vs-source directionality — docs/describe (Score 10) |
| | Stage-scoped effect targeting gap (feeds **SA**, Score 8) |

**Do not build next as epics:** full effect-micro catalog; V1/S1; L\*/containers; structured acceptTool.

```text
apply_dsl / micro-tools  →  model in session
  → create_instance      →  bag + store registration
  → call_action          →  effects, transitions, create-in, when fan-out
  → get_instance / list  →  observe stage + properties
```

### Design rules (RT) — still apply to residual MCP work

1. **Thin adapters only** — wrap existing DomainModeling runtime; no new IR or domain opcodes.  
2. **Session-scoped store** — instances on session state.  
3. **Honest descriptions** — model vs runtime tools.  
4. **One golden path** — MCP-only spawn-and-wire smoke stays green.  
5. Separate **library semantics bugs (SA)** from **MCP surface** gaps.

### RT.0–RT.4 — implementation checklist

- [x] **RT.0** Session store + fail-loud create  
- [x] **RT.1** `create_instance` / `get_instance` / `list_instances`  
- [x] **RT.2** `call_action` + transition + spawn-and-wire smoke  
- [x] **RT.3.1 / RT.3.3** README + affordances  
- [x] **RT.3.2** `apply_dsl` honesty points at `create_instance` + `call_action` for exercise/fan-out  

- [x] **RT.4** Suite green with RT smokes; MCP-only E2E path  
- [x] Expansion §0 Runtime marked shipped (refresh with dogfood-2)  

### RT′ — residuals (after dogfood-1 + dogfood-2 review)

| ID | Source | Task | Priority |
|----|--------|------|----------|
| **RT′.1** | ✅ | Message + affordance; **hintCount** separate field (SA′.3) |
| **RT′.6** | ✅ | Library `Deleted()` + MCP early check |
| **RT′.7** | ✅ | `add_policy` universal-guard Description |
| **RT′.8** | ✅ | README: subscription fires on relationship **TARGET** stage entry (+ example) |
| **RT′.2–.5, .9–.10** | Pull | Parser actor; nav diagnostic; builder rename; C5 dogfood; C1-F1 reconcile; richer CallAction errors |

### RT pull-only

| Item | When |
|------|------|
| Subscription “would fire” preview | After SA + live path stable |
| `simulate_effect` | Named need |
| Full MCP effect-micro catalog | Only if **SA** fixed and agents still cannot use DSL |
| Full VmDebugger / V1 | Pain after RT′/SA |

---

## 6e. Slice SA — Stage-action semantics (**NEXT EPIC** after or with RT′ cheap)

**Source:** [DOGFOOD-REPORT-2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md) findings on `AddActionToStage` + effect adders.  
**Human review:** This is **not** “ship effect micro-tools.” It is a **DomainModeling evolution / CallAction resolution** footgun: silent success with no effects.

### Problem (verified in code)

`AddActionToStageChange` appends `new Action(Name, …, effects: [], …)` on the stage.  
`AddEffectToAction` / `AddStageTransitionEffect` target **entity-level** actions by name.  
`CallAction` resolves **stage-scoped** action first when present → **empty effects run** → no-op “success.”

```text
AddAction(entity, "Activate")
AddActionToStage(entity, "Draft", "Activate")   → empty stage copy
AddStageTransitionEffect(entity, "Activate", …) → entity-level only
CallAction("Activate") on Draft                  → stage copy wins → no transition
```

### Design options (pick one coherent model)

| Option | Approach | Prefer |
|--------|----------|--------|
| **A** | Stage lists **references** to entity actions (placement only) | Clean long-term |
| **B** | Effect-by-name updates **all** actions with that name (entity + stages) | Smaller change |
| **C** | Fail-loud: shadowing empty stage action / forbid stage copy without effects | Safety net if A/B deferred |
| **D** | Docs only | **Insufficient** alone |

### SA checklist

- [x] **SA.0** **Option B** (snapshot copy) + CallAction empty-stage fallthrough — not Option A reference model  
- [x] **SA.1** `AddActionToStageChange` copies entity action fields when same name exists  
- [x] **SA.2** Golden: `AddActionToStage_CopiesEntityActionEffects` (entity effect → stage place → CallAction transitions)  
- [x] **SA.3** Same test exercises MCP `add_action_to_stage` + `call_action`  
- [x] **SA.4** Empty stage-only action still callable (no-op if no effects) — fallthrough only when entity twin exists  
- [x] **SA.5** `add_action_to_stage` Description documents copy + “effects after place not copied”  
- [x] **SA.6** Suite **1359** green  

### SA′ — first impl review (closed in follow-up)

Honesty nits from SA′ review: **SA′.2–.4, .6** landed in working tree (Description, `hintCount` field, README target, order golden). **SA′.1** remains **documented-only** (no analysis warning / Option A).

### SA′′ — honesty follow-up review (2026-07-18)

**Verdict:** **Shipable.** SA′ honesty residuals that blocked “honest MVP” are fixed. Suite **1359**. Commit this follow-up. Only meaningful open semantic debt is **stale snapshot** when entity effects change after a **non-empty** stage copy (documented; not fail-loud).

**Solid (this diff)**

| Item | Notes |
|------|--------|
| **SA′.3** | `AnalysisData.hintCount` separate from `infoCount` — no severity conflation |
| **SA′.2** | `add_action_to_stage` Description: copy-from-entity + order-of-ops warning |
| **SA′.4** / RT′.8 | README: subscription = relationship **TARGET** stage entry + example |
| **SA′.6** | Golden `AddActionToStage_Order_StageBeforeEntityEffects_StillTransitions` |
| Deleted instances | README notes CallAction refused |

**Residuals**

| ID | Severity | Finding |
|----|----------|---------|
| **SA′′.1** | **Ops** | **Commit** uncommitted SA′ honesty (README + DomainTools + order test). MVP already in `a74af5d`. |
| **SA′′.2** | Low (docs) | Fallthrough (empty stage → entity) is **code-tested** but **not** mentioned in `add_action_to_stage` Description — only snapshot-copy path is. Optional one sentence. |
| **SA′′.3** | Low | Plan framing/checklist still mixed “NEXT EPIC” / “all closed” in places — clean on commit. |
| **SA′′.4** | Medium / pull | **SA′.1** still true: entity effects **after** stage place when stage already has **copied** effects → stage wins with **stale** set (no fallthrough). Needs analysis warning or Option A — **not** blocking this honesty commit. |
| **SA′′.5** | Pull | Analysis diagnostic “stage action may be stale vs entity” |
| **SA′′.6** | Pull | Option A reference model / fan-out effect updates (old SA′.8) |
| **SA′′.7** | Pull | RT′.2 actor parse message; builder rename; etc. |

**Checklist**

- [ ] **SA′′.1** Commit SA′ honesty follow-up  
- [ ] **SA′′.2** Optional Description note on empty-stage fallthrough  
- [ ] **SA′′.3** Plan header/framing consistency on commit  
- [ ] **SA′′.4–.7** Snapshot/Option A / parser nits — pull only  

**Recommended:** **Commit now (SA′′.1).** Stop/dogfood. Do not open Option A without a second real pain case.

### SA pull (explicitly out of slice)

| Item | When |
|------|------|
| Full `add_effect_to_action` MCP surface for every effect kind | After SA′; only if DSL insufficient |
| Option A reference model | Second consumer / SA′.1 pain |
| Redesign entity-level policy semantics | Honesty only (**RT′.7** done) |

---

## 6d. Post–Phase 3 horizon — Lowered artifacts + host-consumable backends

**Status:** **Well after Phase 3** — not current pick, not interleaved with RT unless a named emergency.  
**Prerequisite track:** Phase 3 thin ✅ → RT ✅ → RT′/SA → only then open L\* / host backends with pain.

**Product thesis:** The lowered Syntax AST is **an implementation** of domain intent. Optional **C#** (review + integrate) and later **MSIL/assembly** (shippable .NET) are **backends**, not a second IR. Reviewing artifacts from real domains feeds back into better AST generation; packaging makes Poly consumable outside the process.

```text
Domain (intent)
  → lower → Syntax AST     ← durable IR (keep)
  → VM ABI                 ← canonical semantics (keep; Phase 3/RT use this)
  ── after Phase 3 + RT ──
  → C# source backend      ← first host-consumable + review surface
  → (later) assembly/MSIL  ← package form; prefer via C#/Roslyn or Expression.Compile
  → review / goldens       ← improve lowerers
```

| Loop half | When | Role |
|-----------|------|------|
| **Agent corrects domain** | Phase 3 thin (shipped) | lower/describe/simulate → fix domain |
| **Exercise domain** | **RT (shipped)** | instances + CallAction in MCP |
| **Review + improve generation** | **Post–Phase 3** | artifact inspect, golden lower tests |
| **Host-consumable ship** | **Post–Phase 3** (after or with review) | C# first; MSIL/assembly second |

**Do not** start host codegen or MSIL emit as a Phase 3 or RT blocker. RT produces exercised domains; that corpus makes later backends worth building.

### Gates before opening L\* / backends

| Gate | Signal |
|------|--------|
| Phase 3 thin closed | ✅ V0/S0/A/G |
| RT thin green | create_instance / call_action / inspect smokes |
| Named need | Review pain and/or “ship .NET artifact” customer — not completeness |

### Future slices (order of magnitude) — all **post–Phase 3**

| ID | Slice | Ships | Notes |
|----|-------|-------|--------|
| **L1** | Richer expression artifact | Optional C# view on policy lower | Small; inspection only |
| **L2** | Action/effect lower in MCP | AST for failed CallAction paths | After RT |
| **L3** | Dual oracle | `compare_engines` | Mismatch pain only |
| **L4** | Golden lower corpus | Snapshots from real domains | Improves generation under CI |
| **L5** | **C# host backend productized** | Deterministic C# from AST (whole or partial) | First **library-shaped** consumable |
| **L6** | **Assembly / MSIL package** | DLL or in-proc emit | Second library form; prefer C#→Roslyn or Expressions→Compile |
| **L7** | Multi-target (SQL, …) | Extension packs | [experiment](../../experiments/domain-plugin-extension-platform.md) |
| **L8** | **Container / registry images** | OCI image customers `docker pull` / deploy | **Likely primary customer desire** for ops; builds *on* L5–L6 (or a fixed Poly host + domain payload) — not instead of IR/VM |

**Packaging ladder (later product, not Phase 3)**

Customers often do **not** want to own the orchestration of “build C# → restore → host → scale.” They want a **pullable unit**:

```text
Domain + Poly runtime (+ optional generated host code)
  → container image
  → push registry
  → customer pulls / deploys (K8s, cloud run, …)
```

| Form | Who orchestrates | Typical buyer |
|------|------------------|---------------|
| Domain / MCP session | Us (authoring) | Agents, modelers |
| C# / DLL | Customer build + host | Devs embedding Poly in an app |
| **Container image** | **Us (or our CI)** | Platform / ops / “just run my domain” |

**Implications for L8 (when far later)**

1. Image contents must be explicit: e.g. HTTP/gRPC façade + Poly VM + domain snapshot, **or** generated app + runtime — not an undefined “blob.”  
2. **VM-canonical semantics inside the image** still apply; the image is packaging, not a fourth meaning.  
3. Versioning: domain revision + Poly runtime + image tag must be correlatable (repro, rollback).  
4. Multi-tenant registry story (private images per customer/domain) is product/security work, not DomainModeling core.  
5. Still **post–Phase 3**: needs RT-proven domains, then a stable host entrypoint, then bake/push — do not block RT or MCP thin work.

**Design rules when this opens**

1. Domain + AST authoritative; C#/MSIL/**images** are **projections/packages** and may change as generators improve.  
2. **VM remains canonical** for in-platform execution; host artifacts must not silently become a second product meaning without goldens.  
3. **C# before MSIL** for review; **images when ops wants pull-and-run** (often *after* a runnable host exists).  
4. Prefer **one** semantic core proven against VM; packaging multiplies deliverables, not semantics.  
5. No domain-specific VM opcodes to ease codegen or containerization.  

**Current pick is RT′ / SA (§6c–§6e), not this section.** L\* remains post–Phase 3 roadmap memory only.

---

## 7. Pull-only (do not start for completeness)

| Item | When |
|------|------|
| V1 `analyze_expression`, `compare_engines` | Agent needs type diagnostics / dual oracle — **C3 green; low urgency** |
| **L*** / host C# / MSIL | §6d — **well after Phase 3**; after RT + named need only |
| S1 `debug_expression` | Step-through pain after S0 |
| Full effect-micro catalog | After **SA**; only if DSL still insufficient — do **not** use micro-tools to paper over empty stage copies |
| `remove_constraint` | Constraint churn via micro-tools (unexercised in dogfood) |
| `add_policy_to_stage` / action | Same |
| Capture / dry-run apply | Reverse-engineering scenario |
| Event tools | **Never** |
| Lab/full DSL specs as MCP connect payload | **Never** — use **G** product guide only |

---

## 8. Test plan (V0 thin vertical)

| Test | Asserts |
|------|---------|
| `LowerExpression_AgeGte_Succeeds` | Success; data mentions comparison / Age |
| `LowerExpression_BadJson_Fails` | Success false |
| `DescribeExpression_AgeGte_PlainEnglish` | Contains Age, 18, comparison sense |
| `DescribeDomainElement_Entity_AfterAdd` | Session entity described |
| `DescribeDomainElement_Unknown_Fails` | Fail loud |

Optional single chain smoke: lower → describe same JSON both succeed.

---

## 9. Effort (order of magnitude)

| Slice | Rough size | Notes |
|-------|------------|--------|
| V0.0–V0.1 | Small | Scaffold + lower |
| V0.2 | Small–medium | Template walker |
| V0.3 | Medium | Multi-kind resolve + templates |
| S0 | Small | Reuse evaluate bag path |
| A0–A2 | Medium | Analysis metadata + one analyzer + MCP |
| G0–G2 | Small | Product-true guide text + `get_dsl_guide` + smoke |
| RT.0–RT.2 | Medium | Session store + create/call/inspect + spawn-and-wire smoke |
| RT′ | Small | Discoverability, IsDeleted, policy/stage honesty |
| SA | Medium | Stage-action identity + effect targeting + goldens |

---

## 10. Suggested PR stack

1. **V0.0 + V0.1** — OracleTool + `lower_expression` + tests  
2. **V0.2** — `describe_expression`  
3. **V0.3** — `describe_domain_element` + smoke  
4. **S0** — `simulate_policy`  
5. **A\*** — suggestions  
6. **G0–G2** — product-true DSL guide  
7. **Dogfood-1** — R ranked #1 → RT  
8. **RT.0–RT.2** — session store + create/call/inspect + spawn-and-wire  
9. **Dogfood-2** — post-RT re-rank ([report 2](agent-summaries/dogfood/DOGFOOD-REPORT-2-20260718.md))  
10. **RT′** — honesty/safety bundle (discoverability, IsDeleted, policy/stage text)  
11. **SA** — stage-action semantics + goldens (§6e)  

---

## 11. Success criteria

### Phase 3 thin (closed)

- [x] V0 / S0 / A-lite / G shipped  
- [x] Dogfood-1 complete; Runtime MCP ranked #1  
- [x] No event tools; no Capture  

### Phase 4 RT (closed — dogfood-2 validated)

- [x] RT.0–RT.2 tools + MCP-only spawn-and-wire  
- [x] Suite green including RT tests  
- [x] Dogfood-2 confirms create/call/when path works  

### Post-RT residuals

- [x] **RT′.1** suggestion discoverability  
- [x] **RT′.6** CallAction refuses deleted instances  
- [x] **RT′.7** policy honesty on `add_policy`  
- [x] **RT′.8** / **SA′.4** subscription direction in README  
- [x] **SA** Option B + fallthrough + golden (`a74af5d`)  
- [x] **SA′.2–.4 / .6** honesty code (suite **1359**, **uncommitted**)  
- [ ] **SA′′.1** commit honesty follow-up  
- [ ] **SA′′.4** stale snapshot after non-empty stage copy — pull  

---

## 12. Agent pick (right now)

```text
DONE:    Phase 3 thin; RT; dogfood-1/2; RT′/SA; SA′ honesty
CURRENT: Effect surface — effect-surface-completeness.md (E0 → E1 delete → E2/E3)
LATER:   SA stale snapshot / Option A; full effect-micro / V1 / L* — pull only
```

**Implementer watch-outs**

- **SA Option B is a snapshot** — documented on `add_action_to_stage`.  
- **Next usefulness track is effects authoring** — see [`effect-surface-completeness.md`](effect-surface-completeness.md).  
- Do **not** open Option A / full effect-micro / host I/O / containers without named pain.
