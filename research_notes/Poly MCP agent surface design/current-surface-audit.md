# Poly MCP — current agent-surface audit (2026-11-06)

**Scope.** The 30-tool MCP surface in `/Users/scoizzle/.t3/worktrees/Poly/t3-4cc2823d` as it stands today: `Poly.Mcp/Tools/{DomainTools,OracleTool,RuntimeTool}.cs`, `Program.cs`, `PolyMcpInstructions.cs`, `Poly.Mcp/README.md`, `Poly.Mcp/Docs/poly-dsl-guide.md`, `.agents/skills/poly/SKILL.md`, `Poly.Mcp/Sessions/*`, `Poly.Tests/Mcp/*`, plus repo history (`docs/probes/**`, `docs/plans/**`, `docs/decisions/**`). Read-only: nothing in the repo was modified except this note.

**Method.**

1. Static read of every tool method (attributes, `[Description]`s, response DTOs, affordance arrays).
2. Live measurement: spawned the built server (`Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll`) over stdio, ran `initialize` → `notifications/initialized` → `tools/list`, then drove two synthetic domains end-to-end and recorded the model-visible `content[0].text` length of every result. Sizes below are **characters of that JSON text** (the bytes a client feeds the model). Token figures are a labelled estimate band of 3.3–4.0 chars/token; no tokenizer was available locally.
3. Cross-checked against the repo's own benchmark doc `docs/plans/reference/mcp-guiding-principles.md` and the two dogfood reports.

**Domains used for measurement.**

- **2 entities / 8 properties** (`Ledger`: `Account` with length/unique/range/default/policy/2 stages/2 actions incl. one parameterised guarded action; `Entry` with a Date and one back-nav; 2 relationships).
- **10 entities / 60 properties / 20 actions / 5 relationships** (`Scale`: 6 length-constrained `Text` properties per entity, 3 stages, 2 actions each).

Reproduction recipes are in the appendix. `tools/list` measured 16,464 chars, matching the independent measurement in the sibling memo `evaluation-and-migration.md` §1.1 exactly.

---

## Q1. Which tools overlap, and where do they force a choice an agent cannot make well?

### Takeaway
Eight tools answer "what is in my model?" in mutually incompatible shapes, three tools surface the same authoring hints, three tools evaluate expressions under different preconditions, and two authoring paths are both documented as the preferred one. The catalog carries no "use X not Y" rule anywhere in the tool list.

### Cited Findings

**Eight read-the-model tools, no stated preference.**

- `get_domain_overview` (counts + names), `get_entity_detail` (one entity's properties/stages/actions/policies/navs), `get_domain_analysis` (diagnostics + structured facts), `get_domain_report` (Markdown for a person), `get_relationships` (nav list), `get_constraints` (constraint detail per property), `export_dsl` (the model as DSL), `describe_domain_element` (one element, prose) — `Poly.Mcp/Tools/DomainTools.cs:245,275,323,474,566,1151,1482`, `Poly.Mcp/Tools/OracleTool.cs:335`.
- Only `get_domain_report` says who it is for ("use it to show a person what the model means", `DomainTools.cs:474`). None says when to prefer it over `export_dsl` or `get_domain_analysis`.
- The cheapest complete read of the whole model is `export_dsl` (834 chars for 2 entities, 3,880 for 10). The "human" report costs 1,434 / 7,824. `get_entity_detail` costs 909 / 994 **per entity**, so reading 10 entities individually costs ~9,940 chars — 1.27× the whole-domain report and 2.6× `export_dsl`.
- `get_entity_detail` is the only one with a fix comment about stage-action counting (`DomainTools.cs:304-306`), and `describe_domain_element(entity)` was not fixed the same way: on `Account` with 3 stage actions, `describe_domain_element` reported `0 actions` while `get_entity_detail` reported `3 stage actions` (measured; `OracleTool.cs:364` uses `detail.Actions.Count` only). Same model, contradictory counts, two tools.
- `get_constraints` is redundant with the per-property `constraintCount` in `get_entity_detail` (`DomainTools.cs:59-63,293`) but is the only way to see constraint *values* (`Poly.Mcp/Tools/DomainTools.cs:1171-1186`).

**Three surfaces for the same advisory hints.**

- `get_domain_analysis` includes hints in `data.messages` and `data.diagnosticFacts` and in `envelope.diagnostics` (`DomainTools.cs:434-447,465`; `DomainQueries.cs:296-302` includes errors+warnings+hints).
- `get_domain_suggestions` filters the same diagnostics to `DMAS001` (`DomainTools.cs:538-546`).
- `validate_dsl` returns the same hints as `findings` on a *valid* document (`DomainTools.cs:1714-1716`). Measured: a clean 2-entity document returns 5 hints; a clean 10-entity document returns 60 hints and is 98.3% hint text (see Q4).

**Three evaluators with disjoint preconditions.**

| Tool | Input | Needs a session | Needs an instance | Reads relationships |
|---|---|---|---|---|
| `evaluate_policy` | entity + policy name + `instanceId` | yes | yes | yes, if linked |
| `oracle_expression` | DSL fragment + JSON property bag | no | no | no — "Expressions that read relationships fail" (`OracleTool.cs:549`) |
| `invoke_action` | instance + action | yes | yes | yes |
| `get_policy_expression` | entity + policy name | yes | no | n/a (inspect only) |
| `describe_domain_element(kind:policy)` | entity + policy name | yes | no | n/a |

- `evaluate_policy`'s description spends 40% of its words on what *not* to pass ("instanceId is required — do not pass a property bag", `DomainTools.cs:1289`) — an explicit correction of a choice the catalog still permits elsewhere (`oracle_expression`).
- `oracle_expression`'s promised failure mode does not match its actual error: passing `entries any Total > 1` returned `Invalid expression: Trailing tokens after expression: 'any' (line 1, col 12)` — a parser error, not the documented "expressions that read relationships fail". Measured 325 chars.
- `describe_domain_element(policy)` and `get_policy_expression` return the same information at 745 and 311 chars respectively; the second is a strict subset of the first and neither cross-references the other in its description.

**Two authoring paths, both declared best.**

- `README.md:142-157` presents bulk (`apply_dsl`) and incremental (`add`/`remove`) as complementary, then states in the chooser table: "**AI agent building a model step by step** → Incremental (`add` / `remove`)" (`README.md:156`).
- `PolyMcpInstructions.cs:21-26`, `.agents/skills/poly/SKILL.md:12-15`, and the guide's own golden workflow (`poly-dsl-guide.md:1287`) all prescribe the opposite: write one document, `validate_dsl`, `apply_dsl`.
- The descriptions bias the same way as the README at the moment of first use: `create_domain_session` returns `affordances: ["add","get_domain_overview"]` — the guide and `apply_dsl` are not offered (`DomainTools.cs:213`). Measured response: 187 chars.
- `add`'s own description says "Bulk structure uses apply_dsl" (`DomainTools.cs:610`), and every successful `add` affordance list includes `apply_dsl` (`DomainTools.cs:640-641` etc.). The surface points in both directions within one response.

**Authoring asymmetry: 13 add kinds, 7 remove kinds.**

- `add` accepts `entity, property, stage, action, stage_action, relationship, constraint, policy, value_type, contract, contract_value_type, contract_endpoint, contract_binding` (`DomainTools.cs:613`).
- `remove` accepts `entity, property, stage, action, stage_action, relationship, policy` (`DomainTools.cs:912`). Constraints are refused with a 510-char explanation of core internals ("core constraint removal is instance-identity-based (RemoveConstraintFromPropertyChange removes by ReferenceEquals)…", `DomainTools.cs:1021-1026`). The four contract kinds and `value_type` have no removal path at all.
- The only escape from an unremovable element is `apply_dsl`, which replaces the domain and clears runtime instances (`DomainTools.cs:1417`; `McpSessionStore.cs:231-257`). So "undo one constraint" costs the whole model plus the instance state.

**Shapes one path can author but another cannot express or read.**

- `add(kind:relationship)` accepts `ManyToMany` (`DomainTools.cs:847-874`), but `export_dsl` fails closed on any ManyToMany because the shipped DSL cannot print it (`DomainTools.cs:1488-1503`, test `ExportDsl_WithManyToMany_FailsClosedNamingRelationship`). Sessions are memory-only (`README.md:213`), so a model with a ManyToMany relationship cannot be persisted by any tool.
- The four `contract_*` kinds are addable, but none of the five exporters or the report reads `ImportedContracts` (grep over `DomainReportExporter`, `JsonSchemaExporter`, `JsonLdExporter`, `ShaclExporter`, `DomainQueries`: zero hits). Only `export_dsl` prints them (`Poly/DomainModeling/Language/DomainDslPrinter.cs:109`).
- README claims "Both paths converge to the same internal representation and produce identical models" (`README.md:159`). The ManyToMany and contract cases are counterexamples in the current code.

### Inferences
- The read surface grew by accretion: one tool per DTO landed at a time (report, suggestions, constraints, describe, analysis facts), so the catalog has no stated "answer this question with that tool" rule. An agent must infer the mapping from descriptions alone.
- Because `create_domain_session` advertises only `add`, the surface's own next-step hints steer an agent into the incremental path that the instructions call second-best. Recovering the batch path requires reading the guide and ignoring the affordances.
- The `add`/`remove` kind asymmetry is invisible until an agent tries to undo something; the failure then costs a full `apply_dsl` round trip and all instances.

### Gaps
- No transcripts of real agent tool-choice sessions exist in the repo, so wrong-tool *rates* cannot be computed. The only recorded agent behavior is the dogfood/probe prose (Q7).
- Whether any consumer depends on `describe_domain_element`'s entity shape is not documented; it has 30 in-process test call sites (Q5) but no assertions on the action count.

---

## Q2. Which descriptions are implementation-first rather than outcome-first?

### Takeaway
Ten of 30 descriptions lead with engine vocabulary before any task outcome ("bootstrapped session", "diagnostics and structured domain facts", "DSL expression fragment against a JSON property bag", "structured description of a domain element"); five more are bare label restatements that add nothing to the name. Only five name a use case.

### Cited Findings

**Implementation-first phrasings (description text, verbatim).**

| Tool | Description fragment | Why it is engine-first | Cite |
|---|---|---|---|
| `create_domain_session` | "Creates a new **bootstrapped** domain session with **built-in primitive types**" | Names an internal construction step and an inventory the agent cannot use yet; never says "start here for a task" | `DomainTools.cs:198` |
| `get_domain_analysis` | "Returns analysis diagnostics and **structured domain facts** (entity structure, aggregates, topology, actions, subscription plans)" | Five analysis-pass names; the agent asked "is my model sound / what will it do" | `DomainTools.cs:323` |
| `oracle_expression` | "Evaluates a **DSL expression fragment** against a **JSON property bag**" | Two internal representations; no domain task | `OracleTool.cs:549` |
| `describe_domain_element` | "Returns a **structured description** of a domain element" | Restates the name; the value (plain-English guard text) is unstated | `OracleTool.cs:335` |
| `get_domain_overview` | "high-level overview … (entity/primitive/relationship **counts** and entity names)" | Counts, not the question ("what does this domain model?") | `DomainTools.cs:245` |
| `list_sessions` | "Lists all **active domain sessions**" | Sessions are an implementation artifact; the agent needs "which model is mine" | `DomainTools.cs:220` |
| `export_domain_to_shacl` | "a `sh:NodeShape` per entity … `sh:PropertyShape` per property/navigation carrying range/length/pattern/equality constraints and requiredness" | Glossary dump before the outcome | `OracleTool.cs:230` |
| `export_domain_to_json_ld` | "an `@context` plus an RDFS `@graph` (classes, properties with domain/range, enum members)" | Same | `OracleTool.cs:191` |
| `get_domain_suggestions` | "authoring suggestions … identify common gaps like missing stages, actions, or policies — they are advisory and do not block evolution" | Mostly a disclaimer about non-blockingness | `DomainTools.cs:525` |
| `add` | "Creates one domain element. kind is case-sensitive…" | "kind/payload" dispatch vocabulary, not "add an entity called X" | `DomainTools.cs:610` |

- Lean descriptions that mostly restate the name: `get_entity_detail` (93 chars), `get_constraints` (84), `get_policy_expression` (80), `list_sessions` (33), `export_dsl` (53), `list_instances` (79) — `DomainTools.cs:275,1151,1246,220,1482`, `RuntimeTool.cs:564`.
- Descriptions that *do* name an outcome/use case: `get_domain_report` ("use it to show a person what the model means", 229), `export_domain_to_html` ("for a person … save it or open it in a browser", 251), `export_domain_to_json_schema` ("hand the ontology to JSON Schema tooling or diff the model", 305), `evaluate_policy` ("do not pass a property bag", 255), `invoke_action` (672 chars of resolution order and effects).
- No description states a "when **not** to use" case except `apply_dsl`'s implied replacement warning and `evaluate_policy`'s bag warning. The repo's own principle requires "Purpose + when to use + when *not* to use + what it returns" (`docs/plans/reference/mcp-guiding-principles.md` §4).
- Free-form string parameters, which the same doc calls an anti-pattern ("Avoid free-form `dict` / untyped JSON", §4): `add.payload`, `remove.payload` (JSON-as-string), `create_instance.propertiesJson`, `invoke_action.argsJson`, `oracle_expression.propertiesJson` — 5 of 61 parameters. `add.kind`/`remove.kind`/`describe_domain_element.kind` are unconstrained strings, so the schema validator cannot catch a plural `entities`; the tool must (and does) return a 479-char "Unknown kind" message.
- Response shape is invisible from the catalog: `tools/list` publishes `name`, `description`, `inputSchema` only. **0 of 30** tools declare `outputSchema`; **0 of 30** declare MCP annotations (`readOnlyHint`, `destructiveHint`, `idempotentHint`), which the repo's principles §7 asks for. So `export_domain_to_csharp` and `get_domain_overview` are indistinguishable to a client that wants to gate destructive calls.

### Inferences
- The vocabulary is the engine's: "session", "instance", "oracle", "bag", "facts", "kind". An agent has to translate a task ("check this rule holds") into that vocabulary before it can pick a tool.
- The long descriptions that do exist (`link_instances` 727, `invoke_action` 672, `unlink_instances` 424) spend the space on mechanics and multi-step recipes, not on disambiguation from the neighbouring evaluators.
- With no output schemas or annotations, the only way to learn a response shape is to call the tool — which is exactly where the large payloads (Q4) are.

### Gaps
- The repo has no description-quality eval (no task suite measuring selection). The principles doc recommends one (§10); nothing in `Poly.Tests` exercises `tools/list` text (the closest is `McpSmokeTests.GetDslGuide_ReturnsProductSurface`, which asserts guide content, not descriptions).

---

## Q3. What must an agent know before its first useful call, and what is only discoverable by trial?

### Takeaway
Five prerequisites are load-bearing and none is in the tool schemas: a `sessionId` (26/30 tools), the `.poly` grammar, the `add`/`remove` payload shapes (only in guide §12), the fact that `apply_dsl` replaces the domain, renames it, and clears instances, and the required-by-default create rule. Three of these are only discoverable by failing a call.

### Cited Findings

**Session identity.**
- 26 of 30 tools take `sessionId`; the four that do not are `create_domain_session` (takes `domainName`), `list_sessions`, `get_dsl_guide`, and `oracle_expression` (live `tools/list` schemas).
- `list_sessions` returns only opaque ids: `data.sessionIds` (`McpSessionStore.cs:215-217`; measured `{"sessionIds":["942c…"]}`, 138 chars). There is no domain name, no creation time, no revision. With several sessions live, an agent cannot tell which id is its model without calling `get_domain_overview` on each.
- There is **no tool to delete a session** and no rename: `McpSessionStore.Remove` exists (`McpSessionStore.cs:222`) but is not exposed on any tool (grep over `Poly.Mcp/Tools` finds no call). Sessions persist for the 8h idle TTL / 15min sweep (`README.md:214`).
- `list_sessions`' own description is "Lists all active domain sessions" with no warning that the server is shared; the warning exists only in the skill ("there is no ownership check", `SKILL.md:32`).

**Grammar and payload shapes.**
- `add`/`remove` take `payload` as a **string** of JSON, with shapes only in `get_dsl_guide` section 12 (`DomainTools.cs:610-614,909-913`). The catalog gives no example; the description is a pointer.
- `README.md:144` still claims "The tool Description embeds the kind → payload table." It does not — it points at guide §12 (verified against the live `tools/list`).
- The full grammar is 63,938 chars on disk (1,325 lines). `get_dsl_guide` default returns 9,111 chars (§0 principles + §10 denials + golden workflow + index); `section:"all"` returns 78,766 chars of JSON-escaped text.
- Getting the sections needed for a normal domain (§3 entities, §4 navs, §5 stages, §6 actions, §7 subscriptions, §8 policies) is 43,377 chars of source, one call per section. There is no "give me everything needed to write an action" request.
- The guide has **no section 1** — headings run 0, 2, 3 … 13 (`poly-dsl-guide.md` headings; `McpSmokeTests.GetDslGuide_ReturnsProductSurface` asserts `get_dsl_guide("1")` fails, `McpSmokeTests.cs:1387-1389`). An agent that asks for "1" gets an error and the index.

**`apply_dsl` is destructive in three ways.**
- Replaces the domain (not merges), increments revision, and clears runtime instances (`DomainTools.cs:1417`, `McpSessionStore.cs:231-257`; asserted by `ApplyDsl_MinimalEntity_ReplacesSession`).
- Renames the domain: `CompileDsl` takes the name from the document (`DomainTools.cs:1691`), and `Replace` installs it. Measured: a session created as `Ledger` reports `Other` after applying a document headed `domain Other`. The session id is unchanged, so the name is not a reliable handle.
- Re-validates against the session's *current* extension set: `CompileDsl` seeds from `state.Domain.Extensions` (`DomainTools.cs:1675-1679`), so the "dry run" is stateful — the same document can resolve differently depending on what the session currently holds. (Code-level inference; no test asserts the difference.)
- Instances do not survive a re-apply: measured, an instance created before re-applying returns `Instance '…' not found.` afterwards (code: `McpSessionStore.Replace` builds a new state, `McpSessionStore.cs:238-257`).

**Create semantics.**
- A property is required unless `optional`, `default(...)`, or assigned by the first stage's entry (`poly-dsl-guide.md` §3, S29 fix). An agent learns this by `create_instance` failing with `'Name' is required.` (measured 169 chars) — the tool description for `create_instance` does not mention requiredness at all (`RuntimeTool.cs:181-193`).
- Instance ids are 32-char `Guid("N")` strings, returned nested at `data.instance.instanceId` (`RuntimeTool.cs:118-131,266`), not `data.instanceId`. The peer memo records a grader script crashing on exactly this difference (`evaluation-and-migration.md` §1.3.4).
- `get_instance` renders **all** property values and nav handles as strings (`RuntimeTool.cs:71-78`), so type information is lost on read-back; `list_instances` shows only a `propertyCount` (`RuntimeTool.cs:156-161`).

**Steering that exists only in affordances.**
- `validate_dsl`'s failure affordances are `["get_dsl_guide","validate_dsl"]`; success is `["apply_dsl","export_dsl"]` (`DomainTools.cs:1406-1408`). An agent that only ever reads responses never learns that `get_domain_analysis` exists for the accepted model.
- `apply_dsl`'s success list has 8 entries including `apply_dsl` itself (`DomainTools.cs:1474-1476`) — an invitation to re-apply, which clears instances.

### Inferences
- The first-run sequence is learned from the server instructions, not from the catalog: `initialize` carries 1,754 chars of prose (`PolyMcpInstructions.cs:9`, measured) that name the golden path. An agent that ignores `instructions` and reads only `tools/list` would have to guess between `add` and `apply_dsl`, and would not know the DSL guide is mandatory.
- `validate_dsl` requiring a `sessionId` means "check this document before I commit to a session" is impossible; the cheapest dry run is create-session → validate, which leaves a session behind (no delete tool).
- Because `list_sessions` is name-blind, the skill's "keep one session per task" is unenforceable in practice after a context loss.

### Gaps
- No measurement of how often agents call `get_dsl_guide` before `apply_dsl`, or of first-call failure rate. Could be derived from transcripts, which are not in the repo.
- The stateful-seed behaviour of `validate_dsl`/`apply_dsl` (extensions inherited from the current session) is not covered by any test; its practical impact is unmeasured.

---

## Q4. Which responses are large or low-signal? Quantified.

### Takeaway
The surface has three token sinks: a 16.5k-char catalog that is paid every turn, a guide whose sections total 43k chars, and diagnostic/export payloads that are 93–98% duplicated or non-actionable. A clean 10-entity model costs 21,170 chars to validate, 31,978 to analyse, and 51,484 to export to C# — and 92.9% of the analysis payload is the same 60 hints written three times.

### Cited Findings

**Fixed cost, paid before any task.**

| Item | Chars | Est. tokens @3.3–4.0 | Cite |
|---|---:|---:|---|
| `tools/list` result (30 tools) | 16,464 | 4.1k–5.0k | measured; ≈6.7k of description text, 7,867 of input schemas |
| server `instructions` | 1,754 | 0.4k–0.5k | `initialize` result, `PolyMcpInstructions.cs:9` |
| `get_dsl_guide` default | 9,111 | 2.3k–2.8k | measured; §0 alone is 6,219 of it |
| `get_dsl_guide(section:"all")` | 78,766 | 19.7k–23.9k | measured; 63,938-char source + JSON escaping |

**Per-call payloads (model-visible `content[0].text`).**

| Tool | 2 entities | 10 entities | Growth | Notes |
|---|---:|---:|---|---|
| `validate_dsl` (valid document) | 2,873 | 21,170 | ~7× | 60 hints on a clean 10-entity doc |
| `apply_dsl` (accepted) | 787 | 1,605 | 2× | returns entity/relationship snapshot |
| `get_domain_overview` | 337 | 369 | flat | 10 "primitives" always counted |
| `get_domain_analysis` | 4,444 | 31,978 | 7× | 60 hints, 3 encodings |
| `get_domain_report` | 1,434 | 7,824 | 5× | full model in Markdown |
| `get_domain_suggestions` | 511 | n/a | — | DMAS001 only |
| `get_relationships` | 409 | 738 | 2× | |
| `export_dsl` | 834 | 3,880 | 5× | most compact complete model |
| `get_entity_detail` | 909 | 994 | flat/entity | per entity |
| `get_instance` | 547 | — | — | one instance |
| `invoke_action` | 298 | — | — | |
| `evaluate_policy` | 291 | — | — | |
| `describe_domain_element(entity)` | 532 | — | — | |
| `describe_domain_element(policy)` | 745 | — | — | |
| `get_constraints` | 531 | — | — | |
| `get_policy_expression` | 311 | — | — | |
| `export_domain_to_json_schema` | 2,414 | 15,187 | 6× | |
| `export_domain_to_json_ld` | 4,828 | 31,948 | 7× | |
| `export_domain_to_shacl` | 6,278 | 41,041 | 7× | |
| `export_domain_to_html` | 7,480 | 39,281 | 5× | |
| `export_domain_to_csharp` | 8,051 | 51,484 | 6× | ~5.1k chars per entity |

**Duplication, measured.**

- `get_domain_analysis` (10 entities): 30,178-char envelope. `envelope.diagnostics` = 8,401, `data.messages` = 8,401, `data.diagnosticFacts` = 11,221. The same 60 findings are written three times: **28,023 / 30,178 = 92.9%** of the payload.
- `validate_dsl` (10 entities, valid): 19,970-char envelope. `diagnostics` = 8,401, `data.findings` = 11,221 (the formatted-string twin of the same array). **19,622 / 19,970 = 98.3%** duplication.
- `diagnosticFacts` is not a lossless superset: `elementPath` is present there, the human `messages` carry it inline, and for a rejected document `BuildValidationData` inserts mutation errors with `elementPath: ""` (`DomainTools.cs:1720-1730`), so neither encoding is complete on its own.

**The hints are the noise, and they are per-property.**
- `DMCS001` ("length minimum N is enforced when the value is written. Storage projects only HasMaxLength(M)") fires once per length-constrained property. The 10-entity domain had 60 length-constrained properties → exactly 60 hints, 0 errors, 0 warnings (measured `Counter({'Hint': 60})`, all `DMCS001`).
- `get_domain_analysis`'s message on that domain is "0 info(s), 0 warning(s). 60 hint(s). No errors." — the tool's own summary says the model is clean, then ships 28k chars of hint text.
- `DomainQueries.GetAnalysisSummary` documents the choice deliberately: "nothing is dropped: an answer that silently stops at the tenth consequence is worse than a long one" (`Poly/DomainModeling/Queries/DomainQueries.cs:293-295`). The comment addresses truncation, not duplication.

**Error prose is large for a single mistake.**
- One typo (`Status: AccountStatus` on a 2-property entity) produced a **1,763-char** response: the message lists all ten value types, explains `Guid`/`Now` are expression-only, and adds a line/col (`measured`; fixed by S27, `docs/probes/findings/scratch-2026-10-14.md`).
- A rejected 2-entity document returns 2,006 chars for one error + two hints.
- `remove(kind:constraint)` returns 510 chars explaining `ReferenceEquals` internals (`DomainTools.cs:1024`).

**No tool offers a size control.**
- 0 of 30 tools have `limit`, `offset`, `format`, `detail`, or `include` parameters; total parameters across the surface is 61, almost all required identity strings (`tools/list` schemas). The repo's principles ask for `limit`/`hasMore` and an optional `responseFormat` (`mcp-guiding-principles.md` §5); neither exists.
- The five artifact exporters always inline the entire artifact; there is no "write to file", "summary", or "counts only" mode (`OracleTool.cs:122-296`). An agent asked for one type definition pays for the whole file set.

**Cost of a first useful model (arithmetic sum of measured pieces).**
`tools/list` 16,464 + instructions 1,754 + guide default 9,111 + guide §3–§8 43,377 + `validate_dsl` 2,873 + `apply_dsl` 787 ≈ **74,366 chars ≈ 18.6k–22.5k tokens** before the first instance exists. Section responses add ~5% JSON escaping, so this is a floor.

### Inferences
- The dominant recurring cost is diagnostics, not model data: the two "check my model" tools spend 93–98% of their bytes restating hints the summary already counts.
- The artifact exporters are all-or-nothing; they are the only tools whose payload scales with model size *and* with artifact verbosity at the same time (C# ~5.1k chars/entity, SHACL ~4.1k/entity).
- `export_dsl` is the cheapest complete read (834 / 3,880) but is described as a persistence step ("Exports the current session domain as .poly DSL text"), so nothing steers an agent to use it for orientation.

### Gaps
- Token counts are estimates from a char band; the provider tokenizer was not available. Relative sizes are exact.
- No measurement on a production-sized domain (>10 entities); the 10-entity sample is a synthetic lower bound for real use.
- Whether `DMCS001` hints are actionable for a storage pack user is a product judgement; the report can only say they dominate the payload.

---

## Q5. Which capabilities are load-bearing, and which are incidental?

### Takeaway
The working set is small: session + guide + validate/apply + one whole-model read + instance create/invoke/evaluate carry the golden path. Nine further tools are duplicate or single-consumer paths (the 5 artifact exporters, `oracle_expression`, `get_policy_expression`, `get_constraints`, `get_domain_suggestions`): 5 tools are never advertised by any affordance, and the thin ones carry ≤6 in-process test call sites (`validate_dsl` 6, `get_domain_report` 4, JSON-LD 4, SHACL 4).

### Cited Findings

**Affordance graph.** Every response may name next tools (`DomainTools.cs:32`). Counting all static affordance literals across `Poly.Mcp/Tools/*.cs`:

| Tool | Times suggested by another tool |
|---|---:|
| `get_entity_detail` | 51 |
| `get_domain_overview` | 48 |
| `apply_dsl` | 43 |
| `add` | 37 |
| `get_domain_analysis` | 24 |
| `create_domain_session` | 20 |
| `list_sessions` | 18 |
| `list_instances` | 15 |
| `get_relationships` | 14 |
| `create_instance` | 13 |
| `validate_dsl` | 12 |
| `remove` | 9 |
| `evaluate_policy` | 7 |
| `invoke_action`, `link_instances`, `get_policy_expression`, `get_instance`, `export_dsl` | 4 each |
| `get_dsl_guide`, `export_domain_to_json_schema` | 3 each |
| `unlink_instances`, `get_domain_report`, `export_domain_to_json_ld`, `export_domain_to_csharp` | 1 each |
| **never (static)** | `describe_domain_element`, `oracle_expression`, `get_constraints`, `export_domain_to_shacl`, `export_domain_to_html` |
| conditional only | `get_domain_suggestions` (`affordances.Add` when `hintCount > 0`, `DomainTools.cs:456-457`) |

- So 5 of 30 tools are reachable only by reading the catalog; one more only when hints exist. Conversely `apply_dsl` is the most-advertised authoring tool (43) while `get_dsl_guide` — which `apply_dsl`'s own description makes mandatory — is offered 3 times.

**Test weight (in-process call sites in `Poly.Tests`, 25 MCP files).**

| Tool class | Call sites | Reading |
|---|---:|---|
| `ApplyDsl` | 295 | primary authoring path in tests |
| `CreateInstance` / `InvokeAction` / `EvaluatePolicy` | 235 / 117 / 191 | runtime is heavily exercised |
| `Add` | 859 (inflated by other `.Add(` symbols; relative only) | primary incremental path |
| `ExportDsl` / `LinkInstances` / `GetInstance` / `Remove` | 56 / 58 / 52 / 54 | well covered |
| `GetEntityDetail` / `GetDomainOverview` / `DescribeDomainElement` / `ExportDomainToCSharp` | 42 / 34 / 30 / 30 | covered |
| `GetConstraints` / `GetDomainAnalysis` / `ListInstances` / `CreateDomainSession` / `ListSessions` | 18 / 16 / 16 / 16 / 8 | thin relative to role |
| `GetDslGuide` | 14 | the mandatory prerequisite is tested mostly for text content |
| `GetPolicyExpression` / `UnlinkInstances` | 12 / 12 | |
| `ValidateDsl` | **6** | the promoted dry run is the least-exercised authoring tool |
| `GetDomainSuggestions` / `GetDomainReport` / `ExportDomainToJsonSchema` / `ExportDomainToHtml` | 6 / 4 / 6 / 6 | |
| `ExportDomainToJsonLd` / `ExportDomainToShacl` | 4 / 4 | |

- `ValidateDslTests.cs` (103 lines, 4 tests) is the entire dedicated coverage of the dry-run contract; `UnifiedAddTests`/`UnifiedRemoveTests` cover kinds one at a time.
- The evaluators' semantic overloading is pinned by tests, not just code: `EvaluatePolicy_AgeGuard_ReturnsTrueForAdult` asserts `minor.Success` is **true** while the message contains `false` (`McpSmokeTests.cs:530-531`), i.e. `success` means "the call ran", not "the policy passed".

**Load-bearing (used by the golden path and the guides).**
`create_domain_session`, `get_dsl_guide`, `validate_dsl`, `apply_dsl`, `get_domain_analysis`, one whole-model read (`get_domain_report` or `export_dsl`), `create_instance`, `invoke_action`, `evaluate_policy`, `link_instances`, `unlink_instances`, `get_instance`, `list_instances`, `get_entity_detail`.

**Incidental / single-consumer.**
- `oracle_expression`: session-free bag probe; 16 test call sites. The guide's golden-workflow test explicitly asserts `oracle` is *not* part of the workflow (`McpSmokeTests.cs:1424`).
- `get_policy_expression`: 311 chars, strict subset of `describe_domain_element(kind:policy)` (745 chars, which adds plain English + structured tree).
- `get_constraints`: per-property detail already counted by `get_entity_detail`; only the values are new.
- `get_domain_suggestions`: a filter over `get_domain_analysis` hints; both fire on DMAS001.
- `export_domain_to_*` (5): pure projections with no MCP-only consumer; the README itself calls the exporter "a pure projection … so the MCP tool, a test, or a human CLI all read the same text" (`README.md:84`). Measured 8,051–51,484 chars each.
- `list_sessions`: id-only, no names, no delete, no timestamps.
- `describe_domain_element`: 30 call sites, but its entity branch contradicts `get_entity_detail` on action counts and duplicates its message three times in one payload (`message`, `data.detail`, `data.description` all the same string, `OracleTool.cs:359-368`; measured 532 chars for a 110-char fact).

**Capabilities with no read-back at all.**
- Contracts: addable via 4 kinds, invisible to every read tool and every exporter except `export_dsl`.
- Session name/revision history: `list_sessions` returns ids only; nothing exposes a per-session domain name.
- Deleted instances: `list_instances` "skips deleted" (`README.md:72`) but nothing reports that an id existed and is deleted; `get_instance` says "not found".
- Property types on instances: `get_instance` stringifies values; a `Date` and a `Text` are indistinguishable in the payload (`RuntimeTool.cs:71-78`).

### Inferences
- The surface's working set is small; the catalog is large mostly because of read projections that restate each other and exports that serve external toolchains.
- Test weight follows implementation history (DSL/runtime) rather than advertised workflow: the dry run that every guide tells the agent to use first is the thinnest-tested authoring tool.
- Five unadvertised tools are effectively dead weight for an agent that navigates by affordances; they only exist for an agent that reads the catalog and guesses.

### Gaps
- No usage telemetry; "load-bearing" is inferred from guides, affordances and tests, not from observed calls.
- `Add` call-site count is polluted by unrelated `.Add(` symbols; a symbol-precise count would need Roslyn (not run here).

---

## Q6. What do the skill and README tell an agent, and where does that conflict with the tools?

### Takeaway
The skill and README are internally consistent about the *spirit* (DSL-first, fail closed) but conflict with each other and with tool behavior on four points: which authoring path an AI agent should use, whether `add` can express models the DSL cannot, whether `add`'s payload table is in the description, and whether sessions are per-task or shared.

### Cited Findings

| # | Claim (source) | Observed behavior | Evidence |
|---|---|---|---|
| 1 | "AI agent building a model step by step → Incremental (`add`/`remove`)" (`README.md:156`) | Instructions, skill and the guide's golden workflow all prescribe one `apply_dsl` document (`PolyMcpInstructions.cs:21-26`, `SKILL.md:12-15`, `poly-dsl-guide.md:1287`) | direct conflict; no tie-breaker in the catalog |
| 2 | "Use `add` … The tool Description embeds the kind → payload table." (`README.md:144`) | Description says "Payload shapes are in get_dsl_guide section 12" (206 chars) and the parameter is a JSON **string** (`DomainTools.cs:610-614`) | `README.md:144` is stale |
| 3 | "Both paths converge … produce identical models." (`README.md:159`) | `add` alone can create `ManyToMany` (unexportable) and 4 contract kinds (invisible to reads) (`DomainTools.cs:1488-1503`, `DomainTools.cs:727-790`) | counterexamples in current code |
| 4 | "Round-trip safe. Anything you author must survive `apply_dsl` → `export_dsl`." (`SKILL.md:23`) and "the incremental `add relationship` tool accepts … `ManyToMany`" (`SKILL.md:24`) | Following the second rule breaks the first: `export_dsl` refuses the model | two rules in the same file conflict |
| 5 | "Sessions are scratch and the server is shared: `list_sessions` lists every session and there is no ownership check." (`SKILL.md:32`) | Server instructions say "start one scratch model for this task" / "Keep one session per task" (`PolyMcpInstructions.cs:23,39`); `list_sessions` cannot distinguish tasks (ids only) | guidance is unactionable without names |
| 6 | Skill description names 8 tools (`create_domain_session`, `apply_dsl`, `get_domain_analysis`, `export_dsl`, `export_domain_to_csharp`, `create_instance`, `invoke_action`, `evaluate_policy`) (`SKILL.md:3`) | 30 are registered; of the 5 exporters only `export_domain_to_csharp` is named — JSON Schema, JSON-LD, SHACL and HTML are absent | discovery gap at the skill layer |
| 7 | "Every MCP tool's Name + Description + Success must match actual behavior… Evaluation fails → `Success: false`" (`README.md:163-171`) | `evaluate_policy` returns `success: true` when the policy evaluates **false** (`DomainTools.cs:1356-1362`; test asserts it); `invoke_action` returns `success: false` for a guard block, conflating protocol failure with a legitimate domain outcome | `success` carries two meanings across the family |
| 8 | `invoke_action` "On success, returns the new stage when a transition occurred." (`RuntimeTool.cs:610`) | It always returns a stage, and it is the **actor's** stage even when the action returns a new instance: `Spawn` on `Parent`/`Idle` returned `New stage: 'Idle'` while the returned `Kid` was in `Fresh` (measured; `RuntimeTool.cs:686,697-701`) | reproduced 2026-11-06; S4 recurred 5× in `docs/probes/findings/scratch-2026-10-14.md` |
| 9 | `get_policy_expression` "Returns the guard expression text" (`DomainTools.cs:1246`) | Fixed on 2026-10 (S31) — now returns DSL text; verified `Balance >= 0` (311 chars) | historical conflict, now resolved |
| 10 | `get_entity_detail` "detailed information about a specific entity (properties, stages, actions, policies)" (`DomainTools.cs:275`) | Now counts stage actions separately (S30 fix); `describe_domain_element(entity)` still reports `detail.Actions.Count` = 0 for the same entity (`OracleTool.cs:364`) | fix not propagated to the sibling tool |

- `README.md:9-10` correctly says "**30 tools** registered via `Program.cs` (SessionTool, QueryTool, EvolveTool, PolicyTool, DslTool, OracleTool, RuntimeTool)" and that matches `Program.cs:25-31` and the live `tools/list`.
- The skill's "Deeper inspection" list (`SKILL.md:36`) includes `oracle_expression` but not `get_domain_report`, `export_domain_to_html`, `get_domain_suggestions`, `describe_domain_element`'s stage/action/relationship kinds, or `list_instances`/`get_instance`; it also omits `get_relationships`' filter and `get_domain_analysis`' structured facts.

### Inferences
- The two documents were written at different stages (README during the dual-path era, skill after DSL-first) and neither was reconciled; an agent that reads both gets contradictory orders.
- The `README.md:163-171` "honesty invariant" is about naming, not about the meaning of `success`; the evaluators satisfy the letter (they return a bool) while breaking the spirit (a false policy is a successful call).
- Rule 4 in the skill is the most expensive conflict: it tells an agent it may author shapes that the only persistence tool refuses.

### Gaps
- No record of which document an agent actually loads first (skill vs README vs `instructions`), so the practical precedence is unknown.
- The `ManyToMany`/contract gaps might be acceptable if no real task needs them; no task suite exercises either shape.

---

## Q7. Hard evidence of agents getting it wrong.

### Takeaway
The repo's own dogfood and probe records document the same failure families this audit reproduces: wrong-path authoring, invisible hints, entity-level policy surprises, subscription direction, `get_entity_detail` under-counting, and `invoke_action`'s wrong `newStage`. Two of those (S30, S31) were fixed; S4 (newStage) and the guide's subscription contract are still open in behavior.

### Cited Findings

- **Dogfood 1 (2026-07-18)** ranks "no runtime execution through MCP" as pain 18 and records API-naming confusion (`AddActionWithEffect` creates rather than modifies), DMAS001 hints invisible through `get_domain_analysis`, and unclear `actor` rejection — `docs/plans/archive/completed-2026-08-late/v2-to-v3/agent-summaries/dogfood/DOGFOOD-REPORT-20260718.md`.
- **Dogfood 2 (post-runtime)** records: DMAS001 still invisible (score 15), `AddActionToStage` creates empty copies so actions "succeed but do nothing" (score 14), entity-level policies gate **all** actions though the tool description does not say so (score 12), and subscription direction is "hard to reason about" (target, not source) — `DOGFOOD-REPORT-2-20260718.md`.
- **Cycle 11 MCP matrix** (2026-10-15) is the closest thing to an agent-surface test: 22 calls across all 24 tools then live. It found `get_entity_detail` reporting "0 actions" for stage-only actions (S30) and `get_policy_expression` dumping the raw AST (S31) — both fixed, with tests `GetEntityDetail_StageActions_AreReportedSeparately` and `GetPolicyExpression_ReturnsDslText_NotTheAst` (`docs/probes/findings/scratch-2026-10-14.md`, "Fixed in the fix batch").
- **S4 `newStage` = actor's stage** is recorded five times (S4 plus "recurrence #2–#5") from the gym cycle through cycle 7 — e.g. `AddSlip` returned `Payslip` in `Added` with `New stage: 'Draft'`. It is **not** in the fixed table, and it reproduces today (measured 2026-11-06: returned `Kid` in `Fresh`, reported `New stage: 'Idle'`).
- **S14 subscriptions do not fire on create-into-stage** — the guide §7 says "enters", behavior is "transitions only"; recorded as an open 🟠 with a repro in the same findings file. The audit's own `create_instance` never triggers a subscription, consistent with that reading.
- **Issue-thread evidence of agent-shaped failures**: #387 (a rejected evolve disabled `unique` for the rest of the session — fixed by passing `priorAnalysis`, `DomainTools.cs:1085-1092`), #197 (the analysis answer dropped anchors and truncated — fixed by carrying all findings, `DomainQueries.cs:293-302`), #200 (`get_instance` dumped whole object graphs for navs — fixed to `#instanceId` / `#linked`, `RuntimeTool.cs:64-78`).
- **Guide-drift guards in tests**: `McpSmokeTests.GetDslGuide_ReturnsProductSurface` asserts the guide does **not** contain `require {` or `require{` (`McpSmokeTests.cs:1399-1400`), evidence that a previously taught form was wrong; the same test asserts the short guide is <240 lines and has no §13.
- **Naming-confusion guard**: `GetDslGuide_GoldenWorkflow_NamesNamedStoreHarness` asserts the golden-workflow paragraph contains `evaluate_policy(instanceId)`/`invoke_action` and does **not** contain "oracle", "bag", "clock", or "stage" (`McpSmokeTests.cs:1404-1428`) — a test written to stop the guide from steering agents at the wrong evaluator.
- **Parameter-name failure recorded by a blind client**: the peer memo's blind-client run passed `document` instead of `polyText` and got `missing a value for the required parameter 'polyText'`; it also observed unknown arguments being silently ignored (`create_instance(..., values: {...})`) and `create_instance` returning `data.instance` rather than `data.instanceId` (`evaluation-and-migration.md` §1.3).
- **Tool count history**: `MCP2` recorded 24 tools in Oct 2026 (`docs/plans/poly-eng/tasks/MCP2.md`); `mcp-catalog-minify.md` targeted "~16–20" and inventoried "~47 registered tools" before minification; the live surface is 30 (2 session + 10 inspect + 6 authoring/DSL + 5 export + 7 runtime).
- **The repo's own principles are the benchmark that the surface currently misses**: ~10–25 tools, "when *not* to use" in descriptions, no free-form JSON bags, `limit`/pagination, `readOnlyHint`/`destructiveHint` annotations, names before ids, `revision` on every response (`docs/plans/reference/mcp-guiding-principles.md` §2–§9). Measured today: 30 tools, 5 untyped JSON-string parameters, 0 tools with limit/offset, 0 annotations, 0 output schemas, and `revision` omitted by 11 tools (including every runtime tool, `export_domain_to_csharp` (`OracleTool.cs:141`) and `describe_domain_element` (`OracleTool.cs:368`)).

### Inferences
- The same defect families recur because they are surface-shaped, not engine-shaped: the counts, the hints, and the `newStage` label survived multiple runtime rewrites (#368/#371) because no test asserts what an agent reads.
- The probe record is the best available proxy for an agent eval: it is a human-directed agent exercising the surface and writing down where it guessed wrong. It is prose, not a scored suite.

### Gaps
- No quantitative wrong-tool rate, calls-per-task, or recovery rate. `mcp-catalog-minify.md` §2 cites Anthropic guidance but the repo never measured its own surface.
- No transcript of an agent choosing between `get_domain_report` and `export_dsl`, or between `evaluate_policy` and `oracle_expression`.

---

## Q8. Cross-cutting inventory (what exists, what is missing)

| Dimension | Current state | Evidence |
|---|---|---|
| Tools | 30 | `Program.cs:25-31`; live `tools/list` |
| Classes | 7 `[McpServerToolType]` | same |
| Parameters | 61, mostly required identity strings | `tools/list` schemas |
| Output schemas | 0/30 | `tools/list` keys: `name`, `description`, `inputSchema` |
| Annotations | 0/30 | same |
| Pagination/format params | 0/30 | same |
| Session delete / rename | none | `McpSessionStore.Remove` unexposed (`McpSessionStore.cs:222`) |
| Session listing detail | ids only | `McpSessionStore.cs:215-217` |
| Instance delete | none | `RuntimeTool.cs` has create/link/unlink/get/list/invoke only |
| Property update | none (remove + re-add only) | `remove`/`add` (`DomainTools.cs:610,909`) |
| Constraint update/remove | none via MCP | `DomainTools.cs:1021-1026` |
| Contract read-back | none except `export_dsl` | grep of all exporters/queries: no `ImportedContracts` |
| ManyToMany persistence | none (`export_dsl` refuses) | `DomainTools.cs:1488-1503` |
| Transactional/idempotent markers | none | 0 annotations |
| `revision` on every response | 19/30 (missing: `list_sessions`, `get_dsl_guide`, `export_domain_to_csharp`, `describe_domain_element`, `oracle_expression`, and all six runtime tools) | `OracleTool.cs:141,368`; `RuntimeTool.cs` returns no revision |
| Error envelope | uniform via `ToolExceptionBoundary` | `ToolExceptionBoundary.cs:36-47` |
| Read-only tools marked read-only | 5 say "Read-only" in prose, 0 in metadata | `tools/list` |

**Measured error paths (chars, model-visible).**

| Failure | Chars | Cite |
|---|---:|---|
| `add` unknown kind (`entities`) | 479 | measured; `DomainTools.cs:791-796` |
| `add` missing field | 299 | `DomainTools.cs:897-901` |
| `add` malformed payload JSON | 376 | `DomainTools.cs:626-631` |
| `remove` constraint refusal | 510 | `DomainTools.cs:1021-1026` |
| `create_instance` unknown property | 344 | `RuntimeTool.cs:230-238` |
| `create_instance` missing required property | 169 | measured |
| `create_instance` unknown entity | names all available entities | `RuntimeTool.cs:202-208` |
| `validate_dsl` typo (1 error) | 1,763 | measured (S27 message) |
| `validate_dsl` rejected 2-entity doc | 2,006 | measured |
| missing session | 121 | measured; `DomainTools.cs:590-592` |

---

## Gaps (what this audit could not establish)

1. **No tokenizer.** All token figures are char/3.3–4.0 estimates; sizes in chars are exact.
2. **No agent transcripts.** Wrong-tool rates, calls/task and recovery rates are unmeasured; the dogfood/probe prose is the only behavioural record.
3. **No production-scale domain.** The 10-entity synthetic sample is a lower bound; real models (20–40 entities) will scale the exporters and hint noise further (linear in properties/entities).
4. **No stdio-level test.** `Poly.Tests/Mcp/*` calls methods in-process (peer memo §1.2); the schema layer, unknown-argument handling and `isError` path are untested, so the catalog numbers above are the only protocol-level evidence.
5. **`validate_dsl` seed dependency** (session extensions inherited into the dry run) is code-derived, not test-confirmed; its practical effect on validation results is unmeasured.
6. **Why 30 rather than the minify target of 16–20** is not recorded in a single decision; `mcp-catalog-minify.md` set the target and the surface later grew by 5 exporters + `get_domain_report`/`get_domain_suggestions` with no catalog-size note.

---

## Appendix — reproduction

All commands run from the repo root; nothing else was written to the repo.

```sh
# 1. Build (already built in the worktree at time of audit)
dotnet build Poly.Mcp/Poly.Mcp.csproj

# 2. Speak MCP over stdio (newline-delimited JSON-RPC)
#    initialize -> notifications/initialized -> tools/list -> tools/call
dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll
```

Measurement domains (paste as `polyText`):

```poly
domain Ledger

uses storage

Account: entity {
  Name: Text length(2, 40)
  Code: Text length(2, 10) unique
  Balance: Number range(0, 1000000)
  Status: Text default("Open")
  NonNegative: policy { Balance >= 0 }
  Open: stage {
    Deposit: action (amount: Number) require NonNegative { assign Balance to Balance + amount }
    Freeze: action { transition to Frozen }
  }
  Frozen: stage { Unfreeze: action { transition to Open } }
  entries: many Entry
}

Entry: entity {
  Amount: Number
  Posted: Date
  account: Account
}
```

```poly
domain Scale
# 10 entities E0..E9, each:
#   P0..P5: Text length(1, 40)
#   Draft: stage { Submit: action { transition to Active } }
#   Active: stage { Close: action { transition to Done } }
#   Done: stage { }
#   peers: many E<n+1>   (on E0..E4 only)
```

Key reproductions of the behavioural findings:

- **`describe_domain_element` vs `get_entity_detail` action count** — apply `Ledger`, then
  `describe_domain_element(kind:"entity", name:"Account")` → "0 actions"; `get_entity_detail("Account")` → "3 stage actions".
- **`invoke_action` `newStage`** — apply a `Parent`/`Kid` domain where `Spawn: action -> Kid { create in kids { Name: "kid" } }` runs from `Parent.Idle`; the response says `Returned 'Kid' … New stage: 'Idle'` while `get_instance(kid)` says `Fresh`.
- **`evaluate_policy` success overloading** — a policy that evaluates false returns `{"success": true, "data": {"result": false}}` with message "Policy failed (false)."
- **Diagnostic duplication** — `get_domain_analysis` on `Scale`: `envelope.diagnostics` and `data.messages` are byte-identical; `data.diagnosticFacts` repeats each message with `elementPath`. 92.9% of the payload.
- **`validate_dsl` on a clean `Scale`** — 60 `DMCS001` hints, 0 errors; `diagnostics` + `findings` = 98.3% of the payload.
