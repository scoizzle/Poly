# DSL delta fragments — design lock (draft for review)

**Date:** 2026-08-08
**Status:** Draft — design lock **for external review**. Not admitted as a suite until §9 decisions are locked.
**Review target:** written to be crowdsourced to an independent agent for adversarial review (see §11).
**Principle:** One product authoring surface (`.poly` DSL); thin MCP; **no payload JSON for structure**.
**Related:** [`mcp-catalog-minify.md`](mcp-catalog-minify.md) (parent plan — "Center DSL for bulk structure and all expression text") · mcp-minify suite + follow-ups 2026-08-08 · [`poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md) (AGENTS.md mandates guide sync) · `Poly.DslCompiler` host

---

## 1. Purpose

Let agents author **incremental domain changes as DSL fragments** — the same `.poly` member syntax, plus a `remove` keyword — instead of the JSON-payload `add` / `remove` MCP tools. When this lands, the JSON payload surface for structure is gone entirely: bulk → `apply_dsl` (replace), incremental → fragment submissions (delta).

**Success sentence for agents:**
> Session + inspect + `apply_dsl` (replace) + **fragment submission** (delta, incl. `remove`) + runtime + oracles — no structure payload JSON anywhere; expressions already DSL-only.

**Net catalog effect (proposed):** 24 → 23 MCP tools (retire `add` and `remove`, add one fragment tool — see §6).

---

## 2. Why now (the arc)

1. mcp-minify already made **expressions** DSL-only (`DslExpressionFragment.ParseExpressionFragment`, `DslExpressionFragment.cs`). The only remaining JSON in authoring is the **structure payload** of `add` / `remove` (names, types, cardinality).
2. That payload schema is the source of a real bug class: B1 in the 2026-08-08 review (`pattern` vs `regex` documented-vs-parsed key drift). If the DSL *is* the schema, that class is impossible.
3. The DSL is **additive by construction**: every additive member already lowers to a `DomainChange` record. The language is incomplete only because there is no inverse — no way to say "remove".
4. Once `remove` exists in the DSL, `add`/`remove` MCP tools are redundant: agents submit fragments, and the tool layer is just a merge executor.

---

## 3. Grounding (verified facts — reviewer should re-check)

| Claim | Evidence |
|---|---|
| Parser is full-document only (`Expect(Domain)` header, then `top` walk, `Expect(EndOfFile)`) | `Poly/DomainModeling/Parsing/PolyDslParser.cs:93-97` |
| Every additive member emits a `DomainChange` (`AddEntityChange`, `AddPropertyToEntityChange`, `AddStageChange`, `AddActionChange`, `AddActionToStageChange`, `AddPolicyToEntityChange`, `AddPolicyToActionChange`, `AddRelationshipChange`, constraint changes) | `Poly/DomainModeling/Evolution/DomainChange.cs` |
| Inverse removal records exist for **entity, property, stage, action, action-from-stage, relationship, entity/stage/action policy** | `Poly/DomainModeling/Evolution/DomainChange.cs` (`RemoveEntityChange` … `RemovePolicyFromActionChange`) |
| `remove(kind: policy)` scope wiring (stage/action) already lands these ops through the MCP layer | `Poly.Mcp/Tools/DomainTools.cs` (B3 follow-up) |
| `require Policy` in an action is deferred to `PendingRequire` and resolved to `AddPolicyToActionChange` after the entity body | `Poly/DomainModeling/Parsing/PolyDslParser.cs:56-57, 299-321` |
| Negated gate materializes a synthetic action policy named `not_<Policy>` | `PolyDslParser.cs:307` |
| `RemoveConstraintFromPropertyChange` removes by **`ReferenceEquals`** (instance identity — cannot be named from text) | `Poly/DomainModeling/Evolution/DomainChange.cs` (`RemoveConstraintFromPropertyChange.ApplyTo`) |
| Grammar is table-driven (`top`, `entity-body`, `stage-body` rules) — new `remove` patterns are additive table rows, not a rewrite | `Poly/DomainModeling/Parsing/DslGrammar.cs:40-88` |
| `require` is a raw `TokenKind.Require` dispatch in the action body, not a table pattern | `PolyDslParser.cs:207, 270-276` |
| N1 nav line emits **only** `AddRelationshipChange` (no separate nav-property change record) | `PolyDslParser.cs:1035` (`ResolvePendingNavs`) |
| `remove` is **not** a current tokenizer keyword — no token collision today; adding it requires a `WordToKind` entry (and a check for domains that used `remove` as an identifier) | `Poly/DomainModeling/Parsing/DslTokenReader.cs` (`WordToKind` — no "remove" row) |
| `apply_dsl` REPLACES the session domain (documented contract) | `Poly.Mcp/Tools/DomainTools.cs` (`ApplyDsl` Description + HONESTY NOTES) |
| Shared dual-cursor base exists for cursor reuse | `Poly/DomainModeling/Parsing/DslParseCursorBase.cs` (N5) |

---

## 4. Proposed syntax

### 4.1 Fragment document (new parse mode)

A fragment is a `.poly`-syntax document **without the `domain` header**, containing only entity/stage/action/policy members. Example:

```poly
// fragment: extend + shrink an existing domain
Order: entity {
  Total: Number range(0, 10000)
  remove Archived: stage
  Submit: action {
    remove require Verified
  }
}
remove Invoice: entity
```

### 4.2 `remove` at every additive level

| Level | Additive (exists today) | `remove` (proposed) |
|---|---|---|
| top | `Order: entity { }` | `remove Order: entity` |
| entity member | `Total: Number` · `Alive: stage { }` · `Submit: action { }` · `Adult: policy { … }` | `remove Total: property` · `remove Alive: stage` · `remove Submit: action` · `remove Adult: policy` |
| stage member | `DoWork: action { }` | `remove DoWork: action` (stage-scoped) |
| action gate | `require Policy` · `require not Policy` | `remove require Policy` · `remove require not Policy` |
| relationship | N1 nav `orders: many Order` (implicit relationship) | `remove orders: many Order` (identity = relationship name — see §5.6) |

The `: kind` annotation is load-bearing, not decoration: `remove Active: stage` vs `remove Active: property` disambiguate same-named members (property "Active" and stage "Active" can coexist on one entity). This mirrors the `kind` field the MCP `remove` tool uses today.

### 4.3 Lowering table (each remove has an existing inverse record)

| Additive (today) | Change record | `remove` syntax | Inverse record |
|---|---|---|---|
| `Order: entity` | `AddEntityChange` | `remove Order: entity` | `RemoveEntityChange` |
| `Total: Number` | `AddPropertyToEntityChange` | `remove Total: property` | `RemovePropertyFromEntityChange` |
| `Alive: stage` | `AddStageChange` | `remove Alive: stage` | `RemoveStageChange` |
| `Submit: action` | `AddActionChange` | `remove Submit: action` | `RemoveActionChange` |
| `DoWork: action` (in stage) | `AddActionToStageChange` | `remove DoWork: action` (in stage) | `RemoveActionFromStageChange` |
| `Adult: policy` | `AddPolicyToEntityChange` | `remove Adult: policy` | `RemovePolicyFromEntityChange` |
| `require Policy` (in action) | `AddPolicyToActionChange` | `remove require Policy` | `RemovePolicyFromActionChange` |
| constraint tail `range(0,100)` | `AddConstraintToPropertyChange` | **open** — see §5.5 | `RemoveConstraintFromPropertyChange` (identity problem) |
| N1 nav `orders: many Order` | `AddRelationshipChange` | `remove orders: many Order` | `RemoveRelationshipChange` |

---

## 5. Merge semantics (the invented part — this is the real design surface)

### 5.1 Replace vs delta (locked proposal)

- **`apply_dsl` keeps its REPLACES contract** — it remains the whole-state path (bootstrapping, round-trips, reproducibility).
- **Fragment submission is delta** — parsed changes are applied to the **current session domain** in order, through the existing analysis-gated evolution path (`McpSessionStore.Evolve`).
- The two paths must be documented side by side (guide §12 "Dual Authoring Path" becomes "Three surfaces": replace, delta, runtime).

### 5.2 Member identity (locked proposal)

Member identity = **(kind, name, parent)** — the exact key the MCP `add`/`remove` payloads use today. No ambiguity between property/stage/action/policy of the same name; stage-scoped action identity includes the stage.

### 5.3 Per-kind merge rules (proposed — reviewer to validate completeness)

| Operation | Rule |
|---|---|
| Additive member | Create if absent. If present **with identical shape** → no-op success (idempotent, safe for re-submission). If present **with different shape** (e.g. property type differs, policy expression differs) → **fail loud** with a diff-style message; never silently overwrite. |
| `remove` member | Remove if present. If absent → **fail loud** (no vacuous success — matches the `Remove*Change` "nothing to remove" behavior). |
| Mixed add + remove of same identity in one submission | **Fail loud** (self-conflict); a submission must not both create and remove the same (kind, name, parent). |

### 5.4 Requires

- `remove require Policy` removes the **gate reference** (`RemovePolicyFromActionChange`), never the policy definition.
- `remove require not Policy` targets the synthetic `not_<Policy>` gate — **mirrors the additive syntax exactly** (additive creates `not_<Policy>`; remove must name it symmetrically). **Open question (O1):** alternatively, `remove require Policy` could be defined to remove whichever gate (plain or negated) exists. Reviewer should pick one and justify.
- Removing a policy definition (`remove Adult: policy`) that is still required by an action is caught by the **analysis gate** (dangling reference) — same as today.

### 5.5 Constraint removal (open — O2)

`RemoveConstraintFromPropertyChange` removes by `ReferenceEquals` — a text fragment cannot name a constraint *instance*. Options:

- **(a)** Add a by-type/by-shape removal change to the core: `RemoveConstraintFromPropertyByType(entity, property, constraintType[, args])` — new core change record + tests. Fragments then support `remove range(...)` on a property tail.
- **(b)** Constraint removal only via **re-declaring the property without the tail** — but that needs an "update property" semantic, which does not exist today (5.3 says different-shape = fail loud). Larger change.
- **(c)** Out of scope for v1: `remove` does not handle constraint tails; constraint deltas go through redefinition. Honest, but leaves the language incomplete for the constraint dimension.

Reviewer: pick (a), (b), or (c) with reasoning. Note (a) requires a core change record — the only kind here that does (everything else reuses existing records).

### 5.6 Relationship identity (open — O3)

N1 nav `orders: many Order` declares an implicit relationship; the nav line emits **only** `AddRelationshipChange` (verified — `PolyDslParser.cs:1035`), so the source-entity "nav property" is the relationship itself, not a second record. Wrinkle: does `remove orders: many Order` also detach the nav from the source entity? Proposals:

- **(a)** `remove orders: many Order` removes the relationship — which, because the nav *is* the relationship, is symmetric with additive by construction (additive adds exactly one record; remove deletes exactly that record). No extra nav-property cleanup exists to define.
- **(b)** Relationship removal only, nav stays as a dangling declaration → analysis gate catches it → effectively (a) with an error.
- **(c)** Explicit: removing a nav-backed relationship is only legal via `remove orders: many Order`, defined as (a).

Reviewer: pick one; verify against how `AddRelationshipChange` vs nav-property changes actually apply in `DomainChange.cs`.

---

## 6. MCP surface impact

- **Retire `add` and `remove`** (the JSON payload tools) — they become redundant once fragments are the delta path. Catalog 24 → 23 with one new tool.
- **New tool (name open — O4):** `apply_fragment(sessionId, fragmentText)` — parses a fragment document, applies changes in order through the analysis gate, fails loud on conflict/absent-remove/self-conflict. Candidate names: `apply_fragment`, `evolve_dsl`, `apply_delta`. Reviewer: check naming against the "name for what it is" rule.
- **Unchanged:** session tools, inspect tools, `apply_dsl`/`export_dsl`/`get_dsl_guide`, runtime instance tools, `evaluate_policy`, `simulate_policy`, `describe_domain_element`, `export_domain_to_csharp`.
- The `add`/`remove` **dispatch tables** (kind → EvolutionBuilder) become the reference for fragment lowering — reuse, don't delete the mapping logic (it moves into the fragment handler).

---

## 7. Docs / guide / gate deltas (AGENTS.md mandate)

- **`poly-dsl-guide.md` + `poly-dsl-agent-guide.md`:** new `remove` keyword section; fragment-vs-document distinction; §12 becomes three-surface.
- **`DslGrammar`/printer/guide smoke:** `GetDslGuide_ReturnsProductSurface` will catch drift — guide must be updated in the same change.
- **Gate greps (P1 pattern):** tool-name greps must now also assert `add`/`remove` MCP tools are gone (they were the last `McpServerTool(Name = "add"|"remove")` — the gate's "unified tools present" grep flips to "absent").
- **`.github/agents/domain-modeling.agent.md`:** fragment submission replaces the `add`/`remove` payload table.
- **`domainmodeling-capability-inventory.md`:** MCP column → fragment syntax.
- **`export_dsl` note:** removes are transient change-stream ops and never print — `.poly` is now two dialects (state-doc vs delta-script); must be documented explicitly (open — O5: is that acceptable, or should `export_dsl` gain a delta view?).

---

## 8. Explicit non-goals

- No change to runtime instance tools (`create_instance`, `invoke_action`, `link_instances`, `unlink_instances`, `evaluate_policy`).
- No change to expression syntax or `DslExpressionFragment` (already DSL-only).
- No `update`/rename keyword in v1 (only create + remove; "different shape = fail loud" covers mutation needs for now).
- No multi-version dual registration of `add`/`remove` (retire hard, same as mcp-minify L4).
- `apply_dsl` REPLACES semantics unchanged.

---

## 9. Open decisions for review (the reviewer's work queue)

- **O1** — `remove require` negation semantics: mirror syntax (`remove require not Policy`) vs "remove whichever gate exists".
- **O2** — constraint removal: core by-type change (a) / property redefinition (b) / out of scope v1 (c).
- **O3** — relationship + nav-property removal coupling (a)/(b)/(c).
- **O4** — fragment tool name.
- **O5** — two-dialect `.poly` acceptability; `export_dsl` delta view needed or not.
- **O6** — idempotent re-add ("identical shape = no-op") vs strict fail on any redeclaration. Idempotency helps agents retry; strictness is simpler to reason about. (Recommended: idempotent.)
- **O7** — analysis-gate interaction: single gate at end of the whole fragment (recommended) vs per-member gates. Single gate means a bad middle member rolls back the whole submission (current `Evolve` behavior); is that the right UX for a 10-member delta?
- **O8** — ordering: are `remove` lines before/after additive lines significant, or is the document processed strictly top-to-bottom (recommended: top-to-bottom, each validated against current state)?

---

## 10. Risks

| Risk | Mitigation |
|---|---|
| Merge semantics invented per kind → ambiguity | §5.2–5.3 identity + rules locked before implementation; suite tests per rule |
| `remove` breaks round-trip (never prints) | §7 explicit two-dialect documentation (O5) |
| Agents confuse replace vs delta | Guide §12 three-surface table; tool descriptions say REPLACES vs DELTA explicitly |
| Constraint remove identity impossible today | O2 decision — must be made before the syntax is promised in the guide |
| Scope creep into update/rename | §8 non-goals |
| Regression on the 1938-test suite | Suite for the fragment parser + merge rules (mirror `DslExprParityTests` frozen-IR style) |

---

## 11. Review checklist for the crowdsourcing agent

Review **this document only** as the contract (open the cited files to verify claims — primary evidence, no chain-trust):

1. **Grounding (§3):** verify each table row by reading the cited files. Any row wrong → bug.
2. **Completeness of §4.2/4.3:** is every additive member covered by a `remove` form? Any additive surface with no inverse → bug. Is `remove` syntax unambiguous against the existing tokenizer (e.g. `remove` as identifier vs keyword — `TokenKind` collision check in `DslTokenReader.cs`)?
3. **Merge rules (§5):** are there member kinds or conflict cases §5.3 misses (e.g. removing a stage that still has actions; removing an entity that is a relationship target — analysis gate covers, but confirm the rule text says so)?
4. **The three wrinkles (O1–O3):** verify the `not_<Policy>` materialization claim (§3) and the `ReferenceEquals` constraint claim (§3); validate the options' feasibility against `DomainChange.cs`.
5. **MCP retirement (§6):** what breaks when `add`/`remove` are deleted — tests (UnifiedAddTests/UnifiedRemoveTests ~25 tests), guide text, agent definition, gate greps? Is the 24→23 count right?
6. **Replace-vs-delta coherence (§5.1, O5):** is a replace tool + a delta tool with opposite semantics a defensible surface, or should fragments fold into `apply_dsl` (mode flag)? Pick and justify.
7. **Recommend a verdict:** ship the lock as-is / ship with O*-X locked differently / needs rework — with the specific reasons.

---

## 12. Exit criteria (once locked)

- [ ] All of O1–O8 decided with one-line rationale each.
- [ ] Fragment parse mode implemented (no `domain` header; `remove` keyword at all levels) with tests.
- [ ] Merge rules implemented per §5.3 with a fail-closed test per rule.
- [ ] `apply_fragment` tool registered; `add`/`remove` deleted; catalog 23.
- [ ] Guide + agent definition + capability inventory updated; gate greps flipped.
- [ ] Full suite green (≥1938 baseline).
