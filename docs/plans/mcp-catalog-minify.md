# MCP catalog minify + drop JSON expression tools

**Date:** 2026-08-07  
**Status:** Draft plan — **not admitted** until explicit CURRENT pick  
**Agent suite:** [`simple-agent-tasks/mcp-minify-README.md`](simple-agent-tasks/mcp-minify-README.md) (trivial-agent micro-tasks 0→G)  
**Principle:** One product authoring surface (`.poly` DSL); thin MCP; fewer non-overlapping tools  
**Related:**  
- Trust: [`customer-trust-proof-map.md`](customer-trust-proof-map.md) · ADR [`2026-07-11-platform-trust-bar-and-dogfood.md`](../decisions/2026-07-11-platform-trust-bar-and-dogfood.md)  
- Grammar (archived): [`archive/completed-2026-08-mid/grammar-integration.md`](archive/completed-2026-08-mid/grammar-integration.md) (E1 done; **GI-8 cancelled**)  
- Expansion history (archived): [`archive/completed-2026-08-mid/v2-to-v3/mcp-tool-surface-expansion.md`](archive/completed-2026-08-mid/v2-to-v3/mcp-tool-surface-expansion.md)  
- Mutation safety: [`mcp-mutation-safety.md`](mcp-mutation-safety.md) (easier with fewer writers)

---

## 1. Purpose

1. **Minify** the default MCP tool catalog so agents choose from a small, non-overlapping set.  
2. **Eliminate** all MCP paths that consume **JSON expression bags** (`DomainExpressionJsonParser`).  
3. Center **DSL** (`apply_dsl` / expression fragments / guide) for bulk structure and all expression text.  
4. **Unify** granular evolve into **two** tools — `add` and `remove` — with a **kind** + **payload** (not one tool per domain object type).  
5. Cancel **GI-8** (JSON dual-media on Grammar) — no second expression language to maintain.

**Success sentence for agents:**  
> Session + inspect + `apply_dsl` + unified `add`/`remove` + runtime + oracles that take **DSL text**, never expression JSON IR.

---

## 2. Why (evidence + product)

| Driver | Detail |
|--------|--------|
| **Product** | DSL + E1 are shipped; JSON bags were a stopgap for policy/oracle before text parse was trustworthy. |
| **Honesty** | Dual media diverges on open forms (`Now`, units); tool Descriptions teach a private JSON schema. |
| **Agent performance** | Large overlapping catalogs hurt tool selection and burn context (Anthropic agent/MCP engineering guidance: fewer, non-overlapping tools; load only what you need). |
| **Ops** | Fewer write tools → simpler session locks / mutation safety. |

---

## 3. Inventory (2026-08-07, live `Poly.Mcp`)

**~47 registered tools** across `DomainTools`, `RuntimeTool`, `OracleTool`.

### 3.1 JSON expression consumers (delete or rewrite)

| Tool | File | Action |
|------|------|--------|
| `add_policy` | DomainTools | **Rewrite** → DSL expression fragment |
| `analyze_expression` | OracleTool | **Rewrite** → DSL fragment *or* **delete** if unused in dogfood |
| `lower_expression` | OracleTool | **Rewrite** or **delete** |
| `lower_expression_to_csharp` | OracleTool | **Rewrite** or **delete** |
| `describe_expression` | OracleTool | **Rewrite** or **delete** |
| `simulate_policy` | OracleTool | **Rewrite** → DSL fragment + subject bag |

**Code to remove after zero callers:**  
`Poly/DomainModeling/Lowering/DomainExpressionJsonParser.cs` + `DomainExpressionJsonParserTests`.

**Not JSON expression IR (keep shapes if tools stay):**  
- Batch tools use JSON **arrays of names** (`add_properties`) — those are payload packaging, not expression IR; they still go away under minify (structure via DSL).  
- `evaluate_policy` subject `properties` as JSON object is a **runtime sample bag**, not an expression; keep if tool stays (document clearly).

### 3.2 Target default catalog (core)

Aim **~16–20 tools**:

| Role | Tools |
|------|--------|
| **Session** | `create_domain_session`, `list_sessions` |
| **Inspect** | `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_suggestions` |
| **DSL authoring** | `get_dsl_guide`, `apply_dsl`, `export_dsl` |
| **Unified evolve** | **`add`**, **`remove`** (kind + payload — see §3.3) |
| **Runtime** | `create_instance`, `get_instance`, `list_instances`, `link_instances`, `unlink_instances`, `invoke_action` |
| **Policy eval** | `evaluate_policy` (subject bag / instanceId — not expression IR) |
| **Optional inspect** | `get_relationships` *or* fold into overview/detail; `get_policy_expression` if still useful |

**Delete as separate tools** (capabilities fold into `add` / `remove` or `apply_dsl`):

| Bucket | Today’s tools |
|--------|----------------|
| Granular structure add | `add_entity`, `add_property`, `add_stage`, `add_action`, `add_action_to_stage`, `add_relationship`, `add_properties`, `add_stages`, `add_actions_to_stages`, `add_constraint`, `add_policy` |
| Granular remove | `remove_entity`, `remove_property`, `remove_stage`, `remove_action`, `remove_action_from_stage`, `remove_relationship`, `remove_policy` |
| Redundant inspect | Prefer drop `get_domain_snapshot`, `get_constraints` if detail/analysis cover |
| Oracle bloat | Merge or drop multi lower_*; expression oracles take DSL if kept |

**Oracle policy (locked):** at most **one** expression oracle (DSL fragment) or none; C# export not default.

### 3.3 Unified `add` / `remove` (locked direction)

Replace N micro-tools with **two** tools that dispatch on a domain-object **kind** and a structured **payload**.

#### Names

| Tool | Role |
|------|------|
| `add` | Create/attach one domain definition element (or a small homogeneous batch) |
| `remove` | Remove one domain definition element by identity |

Avoid `add_domain_element` verbosity unless a name collision appears — **`add` / `remove`** are fine if Descriptions are explicit.

#### Shape (illustrative)

```text
add(
  sessionId,
  kind: "entity" | "property" | "stage" | "action" | "stage_action"
      | "relationship" | "constraint" | "policy",
  payload: { ... kind-specific fields ... }   // object or JSON string of object
)

remove(
  sessionId,
  kind: same enum,
  payload: { ... identity fields for that kind ... }
)
```

#### Payload contracts (minimum)

| kind | `add` payload (required fields) | `remove` payload |
|------|----------------------------------|------------------|
| `entity` | `name` | `name` |
| `property` | `entityName`, `name`, `typeName` | `entityName`, `name` |
| `stage` | `entityName`, `name` | `entityName`, `name` |
| `action` | `entityName`, `name` | `entityName`, `name` |
| `stage_action` | `entityName`, `stageName`, `name` | `entityName`, `stageName`, `name` |
| `relationship` | `name`, `source`, `target`, `cardinality` | `name` |
| `constraint` | `entityName`, `propertyName`, `type`, type-specific args | `entityName`, `propertyName`, `type` (or constraint id if we add one) |
| `policy` | `entityName`, `name`, `expression` (**DSL fragment**), optional scope | `entityName`, `name`, optional scope (`entity`/`stage`/`action` + names) |

**Batch (optional v1):**  
- `add` may accept `items: [payload, …]` for **same kind only** (replaces `add_properties` / `add_stages` / `add_actions_to_stages`).  
- Mixed-kind batches → **reject** (use `apply_dsl` or multiple `add` calls). Fail closed.

**Not in `add`/`remove`:**  
- Full effects, multi-hop subscriptions, large structure → **`apply_dsl`**.  
- Runtime instances → existing runtime tools.  
- Expression **IR as JSON bags** → never; policy `expression` is DSL text only.

#### Implementation notes

- Single Evolve path: map kind → existing `EvolutionBuilder` methods (same semantics as today’s micro-tools).  
- Unknown `kind` or missing required payload field → fail closed with a message listing allowed kinds / fields.  
- Descriptions embed a short kind→payload table (or point at `get_dsl_guide` for effects-heavy work).  
- Prefer **one schema** documented in tool Description + suite tests per kind; avoid inventing a second domain language beyond field names.

#### Why unify instead of delete all micro-tools

| Approach | Pros | Cons |
|----------|------|------|
| Delete all; only `apply_dsl` | Smallest catalog | Full-domain replace for tiny edits; poor agent UX |
| Keep 15 `add_*`/`remove_*` | Familiar | Catalog bloat; selection errors |
| **Unified `add`/`remove`** | Two slots; atomic small edits; clear kind enum | Payload schema must stay strict and documented |

**Locked:** unified tools, not “delete all evolve micro-tools.”

---

## 4. Design locks

1. **Bulk / full structure:** `apply_dsl` remains the path for whole-domain (or large) authoring.  
2. **Incremental structure:** only via unified **`add` / `remove`** (kind + payload). No per-type tool names.  
3. **Expression authoring:** product DSL text only — fragments use `DslExpressionParser` / session `ExpressionFormRegistry`.  
4. **No JSON expression bags** after suite exit — zero `DomainExpressionJsonParser` call sites. Payload JSON for **structured fields** (names, types, cardinality) is OK; expression **body** is never JSON IR.  
5. **GI-8:** cancelled / won’t pull.  
6. **Breaking change:** hard cut on JSON expression params and on old `add_entity`/`remove_*` tool names (no dual registration).  
7. **Affordances:** point only at remaining tools.

---

## 5. Work slices

### M0 — Inventory freeze + suite scaffold

- [ ] Copy final keep/drop table into suite README (`simple-agent-tasks/mcp-minify-README.md`).  
- [ ] List all tests that pass JSON expressions (`McpSmokeTests`, oracle tests).  
- [ ] Mark grammar-integration GI-8 cancelled.  

**Exit:** Admit bar clear; no product code required.

### M1 — Expression fragment API (core)

- [ ] Add public parse entry, e.g.  
  `PolyDslParser.ParseExpressionFragment(string text, DomainParserInputs? inputs = null)`  
  (or dedicated `DslExpressionFragmentParser`) — fail closed on empty, invalid, or trailing tokens.  
- [ ] Unit tests: comparisons, and/or, quantifiers smoke, open-form registry hook, trailing junk fails.  

**Exit:** Core can parse `Age >= 18` without full domain document.

### M2 — Kill JSON expression IR (policy path via unified `add`)

- [ ] No standalone `add_policy` tool — policy create is `add(kind: "policy", payload: { expression: "<dsl>" })`.  
- [ ] Oracle expression tools: DSL fragment **or** delete.  
- [ ] Delete `DomainExpressionJsonParser` + tests when grep is clean.  
- [ ] Update MCP smoke tests off JSON expr and off old `add_policy` names.  

**Exit:** No JSON expression parse in product path.

### M3 — Unified `add` / `remove` + delete micro-tools

- [ ] Implement `add(sessionId, kind, payload)` and `remove(sessionId, kind, payload)` dispatching to existing EvolutionBuilder ops.  
- [ ] Golden tests: one happy path per kind; unknown kind fails; missing fields fail; same-kind batch optional.  
- [ ] Policy kind uses M1 fragment parser for `expression`.  
- [ ] Remove `[McpServerTool]` / methods for all per-type `add_*` / `remove_*` (including batches).  
- [ ] Affordance strings → `add` / `remove` / `apply_dsl` only.  

**Exit:** Two evolve tools replace ~20 micro-tools; semantics match prior Evolve paths.

### M4 — Oracle / inspect diet

- [ ] Keep ≤1 expression oracle (DSL) or none.  
- [ ] Drop or merge remaining oracle noise; C# export only if generation dogfood needs MCP.  
- [ ] Snapshot/constraints/relationships — keep or fold per dogfood note.  

**Exit:** Catalog green.

### M5 — Docs + trust map

- [ ] MCP README: DSL for bulk; `add`/`remove` for incremental; never expression JSON.  
- [ ] Kind/payload table in tool Descriptions or short `get_evolve_kinds` doc in guide appendix (optional).  
- [ ] Trust proof map: MCP expressions = product DSL.  
- [ ] Expansion plan remains superseded (no new per-type tools).  

**Exit:** Honest docs.

### M6 — Gate

- [ ] Full suite green.  
- [ ] Grep: no `DomainExpressionJsonParser`; no per-type `add_entity` tools registered; no “JSON expression” in Descriptions.  
- [ ] Path: session → guide → `apply_dsl` and/or `add` → runtime invoke.  
- [ ] Pre-ship review gate.  

---

## 6. `add` vs `apply_dsl` (when to use which)

| Situation | Tool |
|-----------|------|
| New domain / large rewrite / effects / subscriptions | `apply_dsl` |
| One entity, property, stage, action, relationship, constraint, policy | `add` |
| Delete one element | `remove` |
| Many mixed kinds in one shot | multiple `add` **or** `apply_dsl` (prefer DSL for coherence) |

No third parallel authoring language.

---

## 7. Sequencing

```text
M0 inventory
M1 fragment API
M2 kill JSON expr (oracle + tests)
M3 unified add/remove + delete micro-tools
M4 oracle/inspect diet
M5 docs
M6 gate
```

**Do not** admit temporal pack and this suite as dual CURRENT.

---

## 8. Explicit non-goals

- Multi-version dual registration of old `add_entity` names.  
- GI-8 Grammar port of JSON expressions.  
- Expression body as JSON IR inside `payload` (field names only).  
- Mixed-kind batch in one `add` call (v1).  
- Replacing runtime instance tools.  
- Full mutation-safety rewrite (separate; easier after fewer tool entrypoints).  

---

## 9. Risks

| Risk | Mitigation |
|------|------------|
| Agents invent payload fields | Fail closed + Description table + tests per kind |
| Agents confuse `add` with `apply_dsl` | Docs: bulk vs incremental |
| Payload JSON confused with expression JSON | Naming: `payload` is structure; `expression` field is DSL string only |
| Kind explosion later | New kinds require suite admit — no silent per-type tools |

---

## 10. Success definition

- [ ] Default catalog documents exact N at M6.  
- [ ] Exactly **two** structure-increment tools: `add`, `remove` (plus `apply_dsl` for bulk).  
- [ ] Zero JSON expression tools / zero `DomainExpressionJsonParser`.  
- [ ] Zero per-type `add_entity` / `remove_stage` / … MCP registrations.  
- [ ] Expression text = product DSL only.  
- [ ] GI-8 cancelled.  
- [ ] Suite green; dogfood path documented.  

---

## 11. Agent pick (when admitted)

```text
CURRENT: mcp-minify
THEN:    M0 → M1 → M2 → M3 → M4 → M5 → M6
BLOCK:   re-adding per-type add_*/remove_* tools without explicit admit
```

```bash
copilot --agent plan-suite-until-done -p "Suite: mcp-minify. Mode: until-done."
```

**Solidified:** [`simple-agent-tasks/mcp-minify-README.md`](simple-agent-tasks/mcp-minify-README.md)  
Tasks: `mcp-minify-0` … `mcp-minify-7` + `mcp-minify-gate`.

---

## 12. Decision

**Minify by unification + JSON expression retirement.**  
- Expressions: DSL only.  
- Incremental evolve: **`add` / `remove` with kind + payload**, not one tool per domain object type.  
- Bulk evolve: **`apply_dsl`**.  
Expansion-era micro-tools and expression JSON bags are retired, not dual-tracked.
