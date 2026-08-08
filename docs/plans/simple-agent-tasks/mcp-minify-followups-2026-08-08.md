# mcp-minify — Follow-ups (2026-08-08)

**Source:** [`mcp-minify-local-review-2026-08-08.md`](./mcp-minify-local-review-2026-08-08.md) (multi-pass, 5 bugs / 6 suggestions / 5 nits)
**Suite status:** DONE 2026-08-08 — gate closed before this queue existed; findings below are post-gate review items.
**Rule:** all items are checkable; mark `[x]` with evidence when closed. No commit unless asked.

**All items executed 2026-08-08.** Build green; suite **1938/1938** (1927 + 11 new tests: 7 add + 4 remove). No commit.

## Bugs

- [x] **B1 — `add` constraint Pattern key mismatch (`pattern` vs `regex`)**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: `BuildConstraint` now reads documented `pattern` (back-compat `regex` alias); `Poly.Tests/Mcp/UnifiedAddTests.cs` + `Add_Constraint_Pattern_Succeeds` pins success.
- [x] **B2 — `remove(kind: constraint)` message falsely claims no evolution path**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: message corrected to "constraint remove not implemented in unified remove" + names the real reason (`RemoveConstraintFromPropertyChange` removes by `ReferenceEquals`, unusable from payload identity); parent plan §3.3 row amended; test renamed `Remove_Constraint_NotImplemented_FailsClosed` (asserts "not implemented").
- [x] **B3 — `remove(kind: policy)` dropped stage/action scope**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: optional `stageName`/`actionName` payload fields wired to `RemovePolicyFromStage`/`RemovePolicyFromAction`; both-at-once fails closed; task-5 doc table updated. Tests: `Remove_Policy_StageScope_Succeeds`, `Remove_Policy_ActionScope_Succeeds`, `Remove_Policy_BothScopes_Fails`. **Bonus:** fingerprint no-op guard now counts action-level policies (`A{n}({ap}ap)`), so action-policy removal is detectable — without it the new action-scope test exposed a latent fail-open.
- [x] **B4 — `get_domain_suggestions` hint names deleted `add_policy`**
  - File: `Poly/DomainModeling/Analysis/AuthoringSuggestionAnalyzer.cs:130`
  - Done: hint reworded to "Use `add(kind: policy)` or `apply_dsl`…"; `GetDomainSuggestions_EntityWithPropertiesNoStages_HasSuggestions` asserts hint contains `add(kind: policy)` and not `add_policy`.
- [x] **B5 — `.github/agents/domain-modeling.agent.md` teaches JSON bags + deleted tools**
  - File: `.github/agents/domain-modeling.agent.md`
  - Done: expression section rewritten to DSL-fragment table; tool table rewritten to the 24-tool catalog with `add`/`remove` kind→payload table; approach/constraints updated.

## Suggestions

- [x] **S1 — `add` relationship cardinality: fail closed on unknown values**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: `TryParseCardinality` accepts OneToOne/OneToMany/ManyToMany/ManyToOne (full enum — core holds ManyToOne), defaults omitted → OneToMany, rejects anything else with allowed list; `Add_Relationship_UnknownCardinality_Fails` + `Add_Relationship_ManyToOne_Succeeds` (asserts stored ManyToOne).
- [x] **S2 — `add` kind parameter Description omits constraint/policy**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: parameter Description mirrors the tool-level kind list.
- [x] **S3 — fragment parse uses `inputs: null`; apply_dsl uses session ParserInputs (latent drift)**
  - File: `Poly.Mcp/Tools/DomainTools.cs:640`; `Poly.Mcp/Tools/OracleTool.cs:31`
  - Done: `AddPolicyCore` parses with `state.ParserInputs` (session-first); `simulate_policy` uses `McpDefaults.ParserInputs` (same registry sessions snapshot). Session ParserInputs are fixed (no per-session injection seam), so a form-through-`add` test is not writable; the fragment-level form test (`Fragment_OpenForm_Registry_Honored`) pins form handling, and the plumbing now shares the session default.
- [x] **S4 — stale docs advertise deleted surface**
  - Files: `docs/PROJECT-SUMMARY-FOR-AGENTS.md:112` (JsonParser marked deleted), `docs/domainmodeling-capability-inventory.md` (table remapped to `add`/`remove` + header note), `docs/plans/mcp-batch-snapshot-efficiency.md` + `mcp-domain-inspection-completeness.md` (SUPERSEDED banners), `docs/experiments/DOMAIN-DSL-SPEC.md` (pre-minify banner).
- [x] **S5 — fail-closed coverage gaps on unified add/remove**
  - Files: `Poly.Tests/Mcp/UnifiedAddTests.cs`, `Poly.Tests/Mcp/UnifiedRemoveTests.cs`
  - Done: added Pattern/Range/Length constraint tests, invalid-cardinality, non-string field value, stage/action policy scope removal + both-scope rejection.
- [x] **S6 — `add`/`remove` parse payload before session check**
  - File: `Poly.Mcp/Tools/DomainTools.cs`
  - Done: both tools check `McpSessionStore.TryGet` before `JsonDocument.Parse`; `Add_MissingSession_InvalidJson_ReportsSessionFirst` + `Remove_MissingSession_InvalidJson_ReportsSessionFirst` pin session-first ordering.

## Nits

- [x] **N1 — dead usings** — removed `Bootstrap`/`Lowering` (DomainTools) and `Constraints`/`Syntactic` (OracleTool). `Lowering` kept in OracleTool (`DomainToCSharpExporter` still used).
- [x] **N2 — `Poly.Mcp/README.md:138` "micro-tools"** → "apply_dsl or add/remove".
- [x] **N3 — stale test names** — `Add_Entity_CreatesEntity`, `Add_Entity_DuplicateName_RollsBack`, `Add_Property_AddsPropertyToEntity`.
- [x] **N4 — gate note count** — corrected to "24 tracked (22 M + 2 D) + 5 new".
- [x] **N5 — third copy of dual-cursor pattern** — new `Poly/DomainModeling/Parsing/DslParseCursorBase.cs`; `PolyDslParser`, `FragmentCursor`, and parity-test `ExprCursor` all derive from it (protected-field design keeps the 160 `_current` refs in PolyDslParser untouched). Full suite green proves parity.

## Process follow-ups

- [x] **P1 — gate dead-tool grep covers non-code surfaces**
  - Done: mcp-minify-gate.md grep-gate section extended with a full-tree grep over `Poly.Mcp Poly/DomainModeling docs .github/agents --glob '*.{cs,md}'` (expecting only negation/retirement contexts) + the rule "tool-name deletions must grep the full tree including `.md` and `.agent.md`"; failure mode documented (B4/B5 slipped the `*.cs`-only gates).
