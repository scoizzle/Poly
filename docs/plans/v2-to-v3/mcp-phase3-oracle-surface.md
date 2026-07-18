# Phase 3 — MCP Oracle Surface

**Date:** 2026-07-18  
**Status:** Active — **current pick V0**  
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

- [ ] **V0.0.1** Add `OracleTool` (or `ExpressionTool`) `[McpServerToolType]` class; register in `Program.cs` with `.WithTools<OracleTool>()`.  
- [ ] **V0.0.2** Shared helper: parse expression JSON → `DomainExpression` or failure response (reuse patterns from `PolicyTool.AddPolicy`).  
- [ ] **V0.0.3** README table row: “Oracle tools (visibility)” section.

**Exit:** Tool type registered; build green.

### V0.1 — `lower_expression`

**Behavior:** Input: session optional? Prefer **session-free pure** for expression-only tools **or** require session for consistency — **prefer session-free** for pure lower/describe of a contract (no domain needed). Domain not required for DE lower.

- [ ] **V0.1.1** Tool `lower_expression(expressionJson)` → parse JSON → `DomainExpressionLoweringPass.Lower` with dummy/entity parameter type (match PolicyEvaluator: `Parameter("entity", …)`).  
- [ ] **V0.1.2** Response `data`: structured tree (node type names + children) **and/or** `ToString` / compact dump agents can read. Prefer JSON-serializable DTO of node kind + summary, not raw CLR dumps.  
- [ ] **V0.1.3** Failures: empty JSON, malformed op, unsupported shape — clear message.  
- [ ] **V0.1.4** Test: `LowerExpression_Comparison_ReturnsAstShape` (Age >= 18).  
- [ ] **V0.1.5** Test: `LowerExpression_InvalidJson_Fails`.

**Exit:** Agent can see lowered AST for a policy JSON without committing.

### V0.2 — `describe_expression`

**Behavior:** Same JSON input; return structured form + plain-English template (deterministic string templates — no LLM).

- [ ] **V0.2.1** Template walker over `DomainExpression` (PropertyAccess, Literal, Comparison, And/Or/Not).  
- [ ] **V0.2.2** Tool returns `{ structured, plainEnglish }` (names flexible; document in Description).  
- [ ] **V0.2.3** Test: `DescribeExpression_AgeGte18_ContainsAgeAnd18`.  
- [ ] **V0.2.4** Test: composite `and` produces readable English.

**Exit:** Agent can explain a guard without guessing.

### V0.3 — `describe_domain_element`

**Behavior:** Requires `sessionId` + kind + name (entity | stage | action | policy | relationship).

- [ ] **V0.3.1** Resolve element from session domain (reuse `DomainQueries` / same lookups as QueryTool).  
- [ ] **V0.3.2** Structured breakdown + template English (per expansion plan element table).  
- [ ] **V0.3.3** Policies embed `describe_expression` output for guards.  
- [ ] **V0.3.4** Fail-loud: unknown name / kind.  
- [ ] **V0.3.5** Smoke: session → add entity/property/policy → `describe_domain_element` entity non-empty.  
- [ ] **V0.3.6** Smoke: describe policy returns expression description.

**Exit:** Agent can orient in the model via one tool without parsing raw detail DTOs by hand.

### V0 exit criteria

- [ ] All three tools registered and described honestly  
- [ ] McpSmoke (or focused) tests green for V0.1–V0.3 happy + fail paths  
- [ ] Full suite green  
- [ ] README lists oracle tools  
- [ ] Update [`mcp-tool-surface-expansion.md`](mcp-tool-surface-expansion.md) §0: V0 ✅  

---

## 5. Slice S0 — `simulate_policy` (after V0 or parallel if unblocked)

**Gap:** `evaluate_policy` needs a **named committed** policy. Agents need “does this JSON guard pass on this bag?” before `add_policy`.

- [ ] **S0.1** Tool `simulate_policy(expressionJson, propertiesJson)` (session optional — pure if subject is bag only).  
- [ ] **S0.2** Reuse subject bag path from `EvaluatePolicy` / `DomainEntityInstance` or PolicyEvaluator bag helpers — **do not invent a second eval engine**. Prefer same path as product policy eval.  
- [ ] **S0.3** Return `{ result: bool }` via VM path; Description claims VM honestly.  
- [ ] **S0.4** Test: Age >= 18, subject Age=20 → true; Age=10 → false.  
- [ ] **S0.5** Affordance after success: `add_policy`, `lower_expression`, `describe_expression`.  
- [ ] **S0.6** Optional: `simulate_policy` with `sessionId` + entityName for type-aware subject validation later — **not required for thin S0**.

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

- [ ] V0 three tools green with tests  
- [ ] Agent loop documented in MCP README: lower → describe → (simulate) → add_policy  
- [ ] Suite green  
- [ ] Expansion plan §0 marks V0 done  
- [ ] No event tools; no Capture; no runtime CallAction unless later RT slice  

---

## 12. Agent pick (right now)

```text
CURRENT: V0.0.1 → V0.1.1 → V0.1.4 (lower_expression thin vertical)
THEN:    V0.2 → V0.3 → S0
STOP:    After V0 exit or S0 exit — dogfood before A* / V1
```

**Implementer watch-outs**

- Prefer session-free pure tools for expression contracts; session required only for domain elements.  
- Do not call LLM for “plain English” — templates only.  
- Match existing `DomainToolResponse` affordance style.  
- `Expression` alias / `DomainExpression` naming — follow DomainTools patterns.  
