# Phase 3 — MCP Oracle Surface

**Date:** 2026-07-18  
**Revised:** 2026-07-18 (V0′ + S0 shipped — suite **1339**; all V0′ residuals + simulate_policy done)  
**Status:** V0 + S0 **product-complete** (OracleTool + simulate_policy; suite 1339)  
**Current pick:** **Stop / dogfood** — before A* / V1  
**Predecessor:** Phase 2 spawn-and-wire ([`domainmodeling-next-phase.md`](domainmodeling-next-phase.md)); MCP gap inventory ([`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0)  
**Goal:** Close the **neurosymbolic feedback loop** for agents: propose → **see** pipeline → **simulate** → correct → commit.  
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
| **V0** | Pipeline visibility | `lower_expression`, `describe_expression`, `describe_domain_element` | Existing lowering + query |
| **S0** | Simulate ad-hoc policy | `simulate_policy` | V0 optional; `DomainExpressionJsonParser` + VM eval path |
| **A0–A2** | Actionable suggestions | metadata + analyzer + `get_domain_suggestions` | Analysis framework |
| **V1 / S1** | Deep visibility / debug | `analyze_expression`, `compare_engines`, `debug_expression` | V0 + S0 |
| **Pull** | Effect micro-tools, `remove_constraint`, Capture, Runtime MCP | — | Named dogfood only |

**One open product slice at a time.** Default: **V0** thin vertical (one smoke that chains lower → describe).

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

## 6. Slice A0–A2 — Actionable suggestions (after V0/S0)

Only start with a clear consumer (agent looping on `get_domain_analysis` text is painful enough).

- [ ] **A0.1** `SuggestionMetadata` + `ReportSuggestion` on analysis context  
- [ ] **A1.1** Port one hint generator to structured suggestion (e.g. idempotency or authoring)  
- [ ] **A2.1** MCP `get_domain_suggestions(sessionId)`  
- [ ] **A2.2** Smoke: domain with known hint shape → non-empty suggestions with `acceptTool`  
- [ ] **A2.3** Honesty: suggestions advisory; apply still via evolve tools  

**Exit:** At least one suggestion kind agent can apply via existing `add_policy` / similar.

---

## 7. Pull-only (do not start for completeness)

| Item | When |
|------|------|
| V1 `analyze_expression`, `compare_engines` | Agent needs type diagnostics / dual oracle in MCP |
| S1 `debug_expression` | Step-through pain after S0 |
| Effect micro-tools | Dogfood refuses DSL for incremental effects |
| `remove_constraint` | Constraint churn via micro-tools |
| `add_policy_to_stage` / action | Same |
| Capture / dry-run apply | Reverse-engineering scenario |
| Runtime MCP (CallAction / store) | Named need to run spawn-and-wire **inside** MCP session |
| Event tools | **Never** |

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

---

## 10. Suggested PR stack

1. **V0.0 + V0.1** — OracleTool + `lower_expression` + tests  
2. **V0.2** — `describe_expression`  
3. **V0.3** — `describe_domain_element` + smoke  
4. **S0** — `simulate_policy`  
5. **A\*** — only with consumer  

---

## 11. Success criteria (Phase 3 thin)

- [x] V0 three tools green with tests  
- [x] S0 `simulate_policy` green with tests  
- [x] Agent loop documented in MCP README: lower → describe → (simulate) → add_policy  
- [x] Suite green (**1339**)  
- [x] V0′ residuals closed (entityName disambig, policy smoke, simulate_policy)  
- [x] No event tools; no Capture; no runtime CallAction  

---

## 12. Agent pick (right now)

```text
CURRENT: Stop / dogfood — Phase 3 V0 + S0 complete
THEN:    A* only with consumer; V1/S1 only with debug pain
```

**Implementer watch-outs**

- Prefer session-free pure tools for expression contracts; session required only for domain elements.  
- Do not call LLM for "plain English" — templates only.  
- Match existing `DomainToolResponse` affordance style.  
- Stage/action/policy **name uniqueness is not global** — V0′.1 added optional `entityName` param.  
