# mcp-minify — Local review (multi-pass) — 2026-08-08

- **Target**: local (uncommitted mcp-minify suite changes; 24 tracked + 5 untracked files)
- **Mode**: multi (Pass A = this session, adversarial; Pass B = fresh-context reviewer, diff-only)
- **Issue counts**: 5 bugs, 6 suggestions, 5 nits
- **Verdict**: **not ship as-is** — suite mechanics are correct and green (1927/1927 read-only re-run this session), but B1–B5 are concrete contract/product-surface bugs on valid inputs; gate greps did not cover suggestion payloads, agent definitions, or live hint text. Close B1–B5 (+ S1) via `mcp-minify-followups-2026-08-08.md`.
- **Process notes**: gate greps (`DomainExpressionJsonParser`, per-type registrations) were scoped to `*.cs` — they cannot see `*.md` docs, `.github/agents/*.agent.md`, or string payloads inside product code (`AuthoringSuggestionAnalyzer` hints). Add a "dead-tool name" grep across `Poly.Mcp`, `.github/agents`, and `docs/` to the gate (follow-up P1).

## Summary

The suite retires `DomainExpressionJsonParser`, unifies evolve into `add`/`remove` (24 registered tools, re-grep verified), and reuses `DslExpressionParser` behind a faithful `FragmentCursor` copy — parse paths are genuinely DSL-only and fail-closed, and test conversions preserved semantics. The locked parent-plan §3.3 payload table is not fully implemented (policy remove scope, constraint remove), one tool Description documents a payload key the code does not read (`pattern` vs `regex`), live product output (`get_domain_suggestions`) still names the deleted `add_policy` tool, and an active agent definition still teaches JSON expression bags and all deleted per-type tools. Suite marked DONE while these contract gaps exist and are untested.

## Issues

### Issue 1 -- Severity: bug
- File: `Poly.Mcp/Tools/DomainTools.cs:499` (Description) vs `:925` (`BuildConstraint` reads `"regex"`)
- Description: The `add` Description documents `Pattern needs {"pattern":"^[a-z]+$"}` but `BuildConstraint` reads key `"regex"` and throws `"Pattern requires 'regex' config."` otherwise. A client following the shipped tool Description with a `pattern` key gets `Success: false` on a documented input — key/identity mismatch between documented contract and implementation. The old `add_constraint` Description said `regex` and matched; the rewrite introduced the drift. No unified-add test exercises Pattern. (found by Pass B)
- Suggestion: Read `pattern` in `BuildConstraint` (optionally accept `regex` for back-compat); add a unified-add Pattern success test.
- Status: open

### Issue 2 -- Severity: bug
- File: `Poly.Mcp/Tools/DomainTools.cs:754`
- Description: `remove(kind: constraint)` fails with "there is no remove_constraint evolution path" — a **lying invariant**: `DomainEvolution.RemoveConstraintFromProperty` exists (`Poly/DomainModeling/Evolution/DomainEvolution.cs:367`) and is core-test-covered. Constraint removal is un-implemented in the tool, not impossible in the core. Per protocol §3.9 the invocation must be corrected or the path wired. (found by Pass B)
- Suggestion: Either wire `remove(kind: constraint)` (reconstruct the constraint via `BuildConstraint` from type+args and call `RemoveConstraintFromProperty`), or correct the message to "constraint remove not implemented in unified remove — use apply_dsl" and track the gap in the parent plan.
- Status: open

### Issue 3 -- Severity: bug
- File: `Poly.Mcp/Tools/DomainTools.cs:743-757`; task spec `mcp-minify-5-remove-unified.md` dispatch table; plan §3.3
- Description: Unified `remove(kind: policy)` is entity-scope-only. The suite task 5 table says `policy | entityName, name (+ optional scope fields if old tool had them) | remove_policy`; the old `remove_policy` supported `scope: stage|action` (HEAD baseline verified), and `RemovePolicyFromStage`/`RemovePolicyFromAction` exist (`DomainEvolution.cs:491+`). The rewrite dropped scope and **deleted the three tests forcing those sibling paths** (`RemovePolicy_StageScope_Removes`, `RemovePolicy_InvalidScope_Rejected`, `RemovePolicy_MissingStageName_Rejected`) with no replacement oracle. Stage/action-scoped policies are reachable in valid domains (DSL `require` on stage actions, `AddPolicyToStage`), so incremental removal of them via MCP is now impossible (only full-domain `apply_dsl` replace). (found by Pass A + Pass B, merged)
- Suggestion: Wire scope dispatch (`stageName`/`actionName` payload fields → `RemovePolicyFromStage`/`RemovePolicyFromAction`) per the task table, re-adding fail-closed tests; or explicitly amend the task/plan table and record the narrowed surface.
- Status: open

### Issue 4 -- Severity: bug
- File: `Poly/DomainModeling/Analysis/AuthoringSuggestionAnalyzer.cs:130`
- Description: DMAS001 hint text shipped via `get_domain_suggestions` reads "Policies enforce business rules. Use 'add_policy' to define guards." — naming the deleted tool. Reachable on any valid domain with Boolean/range properties and no policies (the smoke path `GetDomainSuggestions_EntityWithPropertiesNoStages_HasSuggestions` runs it, asserting nothing about hint text). Product output now teaches agents to call a dead tool. (found by Pass B)
- Suggestion: Reword to "Use `add(kind: policy)` or `apply_dsl` to define guards."; add a hint-text assertion.
- Status: open

### Issue 5 -- Severity: bug
- File: `.github/agents/domain-modeling.agent.md:11-39,80`
- Description: Active agent definition still teaches the retired JSON expression format ("`add_policy` accepts a single `expression` JSON string" + JSON shape table) and lists ten deleted tools (`mcp_poly_mcp_add_entity`, `add_property`, …, `add_policy`). Any session using this agent calls dead tools and authors JSON bags — violates locks L1/L2. The suite's docs pass covered `Poly.Mcp/README.md` and the DSL guides but not `.github/agents/`. (found by Pass B)
- Suggestion: Rewrite the tool table to the 24-tool catalog with `add`/`remove` kind+payload; replace the JSON section with DSL-fragment form (or point at `get_dsl_guide`).
- Status: open

### Issue 6 -- Severity: suggestion
- File: `Poly.Mcp/Tools/DomainTools.cs:631-636`; `Poly.Mcp/Tools/OracleTool.cs:32`; contrast `:1193` (apply_dsl)
- Description: `AddPolicyCore` and `simulate_policy` call `ParseExpressionFragment(expressionDsl, inputs: null)` while `apply_dsl` parses with session `parseState.ParserInputs` (pack `ExpressionFormRegistry`). Latent sibling-path drift: once a pack registers open forms (p1 temporal is "Ready (after mut-safety)" per PIPELINE-STATUS), `apply_dsl` accepts `ExpiresAt > Now + 2 days` but `add(kind: policy)`/`simulate_policy` reject the same expression. Reachability today: none (no pack forms registered in sessions) — hence suggestion, not bug. (found by Pass B)
- Suggestion: Plumb session ParserInputs into `AddPolicyCore`/`simulate_policy`, or document the restriction; add a form-registry test through `add(kind: policy)`.
- Status: open

### Issue 7 -- Severity: suggestion
- File: `Poly.Mcp/Tools/DomainTools.cs:637` (relationship cardinality switch)
- Description: Invalid or undocumented `cardinality` silently maps to `OneToMany` (`_ => …`). `RelationshipCardinality.ManyToOne` is a real enum value; `"ManyToOne"` is passed by `McpSmokeTests:673,685` which assert Success only — the silent downgrade is masked by Success-only asserts. Inherited from old `add_relationship` (not a regression), but the new dispatch's stated fail-closed posture doesn't cover value validation and no test catches it. (found by Pass A + Pass B, merged)
- Suggestion: Fail closed on unknown cardinality with the allowed list; fix the two McpSmokeTests sites to use a documented value or assert the stored cardinality.
- Status: open

### Issue 8 -- Severity: suggestion
- File: `Poly.Mcp/Tools/DomainTools.cs:504`
- Description: The `add` **parameter** Description for `kind` lists only "entity, property, stage, action, stage_action, relationship" — omitting `constraint` and `policy` that the tool-level Description (and dispatch) support. MCP clients showing parameter descriptions understate the surface. (found by Pass A + Pass B, merged)
- Suggestion: Mirror the tool-level kind list in the parameter Description.
- Status: open

### Issue 9 -- Severity: suggestion
- File: `Poly.Tests/Mcp/UnifiedAddTests.cs`; `Poly.Tests/Mcp/UnifiedRemoveTests.cs`
- Description: Coverage gaps on the new dispatch surface: no Pattern (hides Issue 1), Range, or Length constraint via `add`; no invalid-cardinality test (Issue 7); no non-string field value test (`"name": 42`); no fail-closed test for `remove(kind: policy)` when the policy is stage/action-scoped (residual of Issue 3). Happy paths per kind are covered; each fail-closed sibling in the tool Description lacks a forcing test. (found by Pass B)
- Suggestion: Add the missing fail-closed tests per kind (construct illegal payload, assert exact message + no mutation).
- Status: open

### Issue 10 -- Severity: suggestion
- File: `Poly.Mcp/Tools/DomainTools.cs:516` (entry ordering)
- Description: `add`/`remove` parse the payload JSON **before** the session-existence check (which happens inside `Evolve`). A missing session with invalid JSON reports "Invalid payload JSON" instead of "Session not found" — error-priority inconsistency with every other tool (session checked first). No test pins the ordering. (found by Pass A)
- Suggestion: Check `McpSessionStore.TryGet` before parsing payload; add a missing-session + invalid-JSON test.
- Status: open

### Issue 11 -- Severity: nit
- File: `Poly.Mcp/Tools/DomainTools.cs:10,13`; `Poly.Mcp/Tools/OracleTool.cs:9,16`
- Description: Dead usings after tool deletions: DomainTools.cs `Poly.DomainModeling.Bootstrap` + `Poly.DomainModeling.Lowering`; OracleTool.cs `Poly.DomainModeling.Constraints` + `Syntactic` alias (no remaining references). (found by Pass A)
- Suggestion: Remove unused usings.
- Status: open

### Issue 12 -- Severity: nit
- File: `Poly.Mcp/README.md:138`
- Description: Runtime Tools section still says "1. apply_dsl / micro-tools → model in session" — stale "micro-tools" mention in the otherwise-updated README. (found by Pass A)
- Suggestion: Replace with "apply_dsl or add/remove".
- Status: open

### Issue 13 -- Severity: nit
- File: `Poly.Tests/Mcp/McpSmokeTests.cs:145,160,178`
- Description: Test names still reference deleted tools (`AddEntityTool_CreatesEntity`, `AddEntityTool_DuplicateName_RollsBack`, `AddPropertyTool_AddsPropertyToEntity`) while calling unified `Add`. Name drift weakens dead-tool greps. (found by Pass B)
- Suggestion: Rename to `Add_Entity_…` style.
- Status: open

### Issue 14 -- Severity: nit
- File: `docs/plans/simple-agent-tasks/mcp-minify-gate.md:16`
- Description: pr1 audit note says "22 modified + 5 new files"; actual change set is **24 tracked** (22 M + 2 D) + 5 untracked (verified via `git status --porcelain` count 29 = 24 + 5). (found by Pass A + Pass B, merged)
- Suggestion: Correct the count in the gate note.
- Status: open

### Issue 15 -- Severity: nit
- File: `Poly/DomainModeling/Parsing/DslExpressionFragment.cs:36-100`
- Description: `FragmentCursor` is the third hand-copy of the dual-cursor pattern (PolyDslParser, `DslExprParityTests.ExprCursor`, fragment). Parity verified line-for-line today (Unread → TryMatch → Read; Peek(1); GrammarException at current position), but three copies drift silently on future cursor fixes. (found by Pass B)
- Suggestion: Extract a shared cursor base and have fragment + parity tests consume it; optionally assert fragment IR-equality against the full parser for a shared corpus.
- Status: open
