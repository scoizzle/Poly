# Poly MCP 2.0 — a six-tool agent contract

Scope: the MCP tool surface only (names, jobs, inputs, outputs, state, envelope, diagnostics, migration). Not the DSL grammar, not the analyzer, not the exporters' internals. Every proposal below is a projection or adapter over capabilities that exist today; no analysis or DSL semantics change.

Method: read the 30 registered tools and their descriptions, the session store, the analyzer passes that publish solver facts, the exporters, the DSL guide, the shipped Agent Skill, and the 2026-10-12 gap map. Measured numbers are marked as measured; everything else is design judgement.

Current as of 2026-11-06, worktree `t3-4cc2823d`. Repo paths are relative to the repo root.

---

## Q1: Is 6 too few, 8 too many — and where is the cut?

### Takeaway

Six. The right cut is **one tool per decision an agent makes, not per endpoint**, and the remaining multiplicity belongs in *data* (a `format` enum, a `select` path, a `steps` array), which costs schema tokens once and adds zero selection ambiguity. Seven would only be justified by splitting `poly_check` into "gate" and "action contract"; eight only by splitting export by audience or keeping `examples` as its own tool. Both splits add a "which one?" hesitation for an answer that comes from the same `AnalysisResult`.

### Cited Findings

- The surface is 30 tools across 7 classes: `SessionTool`, `QueryTool`, `EvolveTool`, `PolicyTool`, `DslTool`, `OracleTool`, `RuntimeTool` — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 10.
- Measured: 30 tool descriptions total **6,730 characters** (mean 224, max 732 for `link_instances`, min 35 for `list_sessions`). That is roughly 1.7k tokens of prose before a single JSON schema is counted; with 30 schemas at ~3 parameters each the pre-call cost is estimated at 7–12k tokens.
- Eight of the 30 are read projections of the same `LatestAnalysis`: `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_report`, `get_domain_suggestions`, `get_relationships`, `get_constraints`, `get_policy_expression` — [`Poly.Mcp/Tools/DomainTools.cs`](../../Poly.Mcp/Tools/DomainTools.cs), lines 245–1289.
- Six are one exporter each, differing only in output format — [`Poly.Mcp/Tools/OracleTool.cs`](../../Poly.Mcp/Tools/OracleTool.cs), lines 122–265.
- Seven are runtime primitives (`create_instance`, `get_instance`, `list_instances`, `link_instances`, `unlink_instances`, `invoke_action`, `evaluate_policy`) that an agent must sequence by hand — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 66–75.
- Two authoring paths are advertised as co-equal: bulk `apply_dsl` and incremental `add`/`remove`, with a "Choosing Between Them" table — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 109–159.
- The analyzer already publishes queryable solver facts that **no MCP tool reads**: `ActionStageInvariant` (preconditions, narrowed ranges, merged constraints, postconditions) and `EffectInvariantMetadata` — [`Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs`](../../Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs), lines 29–39 and 487–540. Its only consumers are `StorageAnalyzer` and `EffectAnalyzer`, plus tests.
- The assign-legality check already distinguishes *certain* violation from *possible* violation: an error when the produced range is disjoint from the constraint, a warning when it merely overlaps — [`Poly/DomainModeling/Analysis/EffectAnalyzer.cs`](../../Poly/DomainModeling/Analysis/EffectAnalyzer.cs), lines 1378–1395.
- The abstract interpreter returns `AbstractValue.Unknown` for anything it cannot model (division, non-numeric operators, unbound symbols), and that unknown propagates silently into the published metadata — [`EffectInvariantAnalyzer.cs`](../../Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs), lines 216–266.
- Composition of several models is a *language* concern: a `uses` line names extensions, and "another Poly domain is a `contract`, not a `uses` line" — [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), lines 192–201 and 253–283.

### Inferences

- Description cost is not the main problem; **name collision is**. `get_domain_analysis` vs `get_domain_suggestions` vs `get_constraints` vs `validate_dsl` vs `describe_domain_element` all answer "tell me about the model", and an agent must read five descriptions to pick one. Collapsing them to one `poly_inspect` with a `select` path removes the choice rather than shortening it.
- The reverse is true for formats: `format: "shacl"` in a description is ~2 tokens and creates no ambiguity, because the agent already knows which artifact the task needs. **Multiplicity in data is cheap; multiplicity in tools is expensive.**
- The 2026-10-12 gap map proposed more emitters (OpenAPI, GraphQL, Protobuf, SQL, OWL) as separate work — [`docs/reference/research/2026-10-12-ontology-of-ontologies-gap-map.md`](../../docs/reference/research/2026-10-12-ontology-of-ontologies-gap-map.md), lines 51–58. Under this contract each is one enum value plus one pure projection, which is a stronger argument for the single-`poly_export` shape than for the gap map's emitter-per-feature framing.
- The gap map is **stale on the emitters it calls absent**: JSON Schema, JSON-LD, SHACL and HTML now ship ([`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 60–64). Treat its Tier 1/2 list as historical rationale, not inventory.

### Gaps

- No measured agent-selection data exists in-repo (no fleet eval for tool choice). The 6-vs-7 judgement rests on description structure, not on observed mis-selection rates.
- No data on how often agents actually used `add`/`remove` versus `apply_dsl`; the case for deleting the incremental path is structural, not usage-based (see Q3).

---

## Q2: What is the exact contract?

### 2.1 The six tools

| Tool | The decision it serves | Mutates? | Replaces |
|---|---|---|---|
| `poly_guide` | "I don't know this language yet." | no | `get_dsl_guide` |
| `poly_describe` | "Here is the model; make it true (or tell me why not)." | model | `create_domain_session`, `apply_dsl`, `validate_dsl` |
| `poly_check` | "May I ship this / is this action legal?" | no | `get_domain_analysis`, `get_domain_suggestions`, `oracle_expression` |
| `poly_inspect` | "What is true here?" | no | `get_domain_overview`, `get_entity_detail`, `get_constraints`, `get_relationships`, `get_policy_expression`, `describe_domain_element` |
| `poly_export` | "Produce the artifact this task needs." | no | 6 export tools, `get_domain_report`, `export_dsl` |
| `poly_simulate` | "Try it and observe." | instances | `create_instance`, `get_instance`, `list_instances`, `link_instances`, `unlink_instances`, `invoke_action`, `evaluate_policy` |

Two conventions that are part of the contract, not cosmetics:

- **`poly_` prefix.** Clients flatten every server's tools into one list. `export` or `check` from three servers is a real collision; a five-character prefix removes it and costs nothing.
- **No verb pair per tool.** Each tool takes one input shape and answers one question. Where two verbs exist (`describe` validates *and* applies; `check` gates *and* interrogates), they share the same input, and the flag that selects the behaviour is a boolean or an enum, never a second tool.

### 2.2 Session identity and the state model

**Identity is the MCP connection.** No `sessionId` argument, no `create_domain_session`, no `list_sessions`.

Why this is safe here: the server is stdio and process-local, and the README already notes that session identity "stays the `sessionId` argument — this process is stdio and has no `Mcp-Session-Id` header" ([`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 217). Today's `sessionId` is therefore pure bookkeeping: every call pays for it, it can be mistyped, it can be evicted after 8h idle, and there is no ownership check — the shipped skill warns agents to "work only in the `sessionId` you created" ([`.agents/skills/poly/SKILL.md`](../../.agents/skills/poly/SKILL.md), line 32). That warning is evidence the id is a failure surface, not a feature.

The whole state is three fields per workspace:

```
WorkspaceState {
  Model?  model          // last successfully applied document + its AnalysisResult
  long    revision       // 0 = empty; +1 on every successful describe(apply)
  Run?    run            // instance bag for poly_simulate
}
```

Rules:

- **Empty is a state, not an error.** `poly_inspect` on an empty workspace returns `ok: true`, `summary: "No model."`, `data: {model: null}`, and `next: ["poly_guide()", "poly_describe(poly: \"...\")"]`. No `session not found` branch exists any more, which deletes a whole failure class.
- **Replace is the only write.** `poly_describe(apply: true)` atomically swaps `model`, increments `revision`, and clears `run`. That is exactly today's `Replace` contract: "Atomically replaces a session's domain and analysis (revision + 1) and clears runtime instances" ([`Poly.Mcp/Sessions/ISessionStore.cs`](../../Poly.Mcp/Sessions/ISessionStore.cs), lines 34–40).
- **Every envelope carries `state`** (`model`, `revision`, `entities`, `instances`). Five fields, ~15 tokens, and it answers "which model am I looking at?" without a second call. A revision that resets to 0 is how an agent detects a server restart.
- **Multi-model, if it is ever needed, is `workspace: string`** defaulting to `"default"`. The state becomes a dictionary keyed by that string; no lifecycle tools, no ids to leak. This is deliberately *not* built now: two models are almost always two *documents*, and a Poly model composes through `uses` and `contract` inside one document ([`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), lines 192–201). **Multi-model is a language concern, not a session concern.**
- **Branching without a second model** is `poly_describe(apply: false)`: parse, evolve and analyze the proposal, return findings and counts, touch nothing. That is today's `validate_dsl` dry run, and it covers "compare two designs" without forking state.

### 2.3 Response envelope

```json
{
  "ok": true,
  "summary": "Model replaced: 5 entities, 12 actions. 0 errors, 2 warnings.",
  "findings": [],
  "data": { },
  "next": ["poly_check()", "poly_export(format: \"json-schema\")"],
  "state": { "model": "Orders", "revision": 7, "entities": 5, "instances": 0 }
}
```

Field contract:

- **`ok`** — the requested job reached an answer that lets the caller proceed. For `check`, that means *no error-severity finding*; for `describe`, the document was accepted (or, with `apply: false`, is valid); for `simulate`, the steps could be executed at all — a model blocking a step is reported per step, not by failing the call (see 2.9). Call-level failures (bad selector, unknown format, unknown action or handle, malformed JSON) are `ok: false` with a `POLY4xx` finding; model-level problems are `ok: false` with `DM*`/`CS*` findings. The code prefix is the disambiguator, so no extra field is needed.
- **`summary`** — one sentence, always present, states the outcome *and* the state change. Prefix `INVALID:` when the model has errors, so an agent skimming one field cannot misread `check`.
- **`findings`** — always an array, never null, empty when there is nothing to act on. Shape below.
- **`data`** — present only when the answer *is* an artifact or a fact set (`inspect`, `export`, targeted `check`, `simulate`). Omitted for verdict-shaped answers (`describe`, model-level `check`), where `summary` + `findings` are the whole answer. This is the "when to return detail" rule: **detail when the caller asked for a thing, not when it asked for a judgement.**
- **`next`** — zero to three *call sketches* with at least one argument, e.g. `"poly_check(action: \"Order.Submit\")"`. Today's `affordances` is a list of bare names ([`Poly.Mcp/Tools/DomainTools.cs`](../../Poly.Mcp/Tools/DomainTools.cs), line 32); bare names force the agent to re-derive the arguments from the schema. Sketches are ~8 tokens and remove that step.

**Finding shape:**

```json
{
  "code": "DMEFF008",
  "severity": "error",
  "path": "Orders.Order.Total",
  "message": "Assigned expression value range [0, 5000] is entirely outside constraint range(0, 1000) on property 'Total'.",
  "hint": "The guard `require amount <= 5000` admits values Total cannot hold.",
  "fix": "Tighten the guard to `require amount <= 1000`, or widen Total to range(0, 5000)."
}
```

- `severity` ∈ `error | warning | hint | info` — the existing `DiagnosticSeverity` values.
- `path` — the existing element path from `DslFinding.ElementPath` ([`DomainTools.cs`](../../Poly.Mcp/Tools/DomainTools.cs), lines 157–162), dot-joined: `Orders.Order.Draft.Submit` for an action in a stage, `Orders.Order.Email` for a property. **The same vocabulary is a valid `poly_inspect` selector** (2.7) — one path language in both directions, so `finding.path` can be pasted straight into `select`.
- `hint` — the *why*, one sentence, distinct from `message` (the *what*). The analyzers already produce messages that mix both; splitting them is a projection, not new analysis.
- `fix` — **advisory text, not an executable patch.** The repo has no auto-fix engine and this contract does not add one. Saying so in the schema description prevents an agent from trying to apply it.
- Findings are capped at 20 with `"truncated": true, "omitted": <n>`; the full text is never silently dropped, because `poly_export("dsl")` reproduces the model and the agent can re-`check` with a narrower selector.

### 2.4 `poly_guide` — learn

Inputs: `topic?: string` (default `"quickstart"`; a section number, a section title, `"example:<name>"`, or `"all"`), `workspace?`.

Output `data`: `{guide: string, sections: [{number, title, chars}]}`.

Token discipline is the point. Today's "short guide" concatenates §0 (6,181 chars of teaching prose), §10, the golden workflow and the index — roughly 7k chars (~1.8k tokens) for a document whose actionable content is ~600 chars. The measured section sizes are §0 6,181 · §2 2,953 · §3 5,960 · §4 2,433 · §5 474 · §6 8,022 · §7 6,573 · §8 19,891 · §9 1,145 · §10 627 · §11 4,938 · §12 3,551 · §13 565; the whole guide is 63,938 chars (~16k tokens) and must never be the default.

New default `quickstart` (~700 chars): the minimal valid document, the four-line workflow, the `Do NOT Use` denylist (§10), and the section index. `topic: "principles"` returns §0 for an agent that wants the modelling rationale; `topic: "6"` returns actions; `topic: "example:orders"` returns a task-shaped example. Examples live here rather than in a seventh tool because "teach me" and "show me" are the same decision — an agent that needs one usually needs the other.

### 2.5 `poly_describe` — author, validate, apply

Inputs: `poly: string` (the full `.poly` document), `apply?: bool` (default **true**), `workspace?`.

Output: `summary`, `findings`, `state`; `data` only on success with `{domainName, entityCount, relationshipCount, revision}`.

- One canonical authoring path: **`.poly` text**, whole document. There is no incremental surface.
- `apply: true` is the default because a rejected document changes nothing — apply is fail-closed, so the safe default is also the fast one. That collapses today's `validate_dsl` + `apply_dsl` into one call and one round trip.
- `apply: false` is the shadow evaluation: same parse, same evolve, same analysis gate, no mutation, same findings. It is the "compare two designs" affordance.
- The domain name comes from the document (`domain Orders`), so there is no `domainName` argument and no way for the name and the text to disagree.

**Do `add` / `remove` survive? No.** Four reasons, in order of weight:

1. **They cannot round-trip.** Anything authored incrementally must survive `poly_describe` → `poly_export("dsl")`, and the shipped rule is "anything you author must survive `apply_dsl` → `export_dsl`" ([`.agents/skills/poly/SKILL.md`](../../.agents/skills/poly/SKILL.md), line 23). A path that can produce a document the printer cannot reproduce is a second, weaker definition of the model.
2. **They are strictly less expressive.** No constraint removal (documented workaround: "use `apply_dsl`" — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 37), no entry/exit blocks, no stage subscriptions, no contracts, and a per-kind payload table that itself needs a guide section ([`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), §12).
3. **They cost more per edit.** A 40-line model is ~400 tokens; twelve `add` calls at ~150 tokens of call overhead each is strictly worse, and each call re-runs the whole analysis pipeline.
4. **They create the selection question** the contract exists to delete: "bulk or incremental?" is a decision with no correct answer, which is why the README needs a five-row table to explain it.

The honest cost: editing one property in a 300-line model means re-sending the document (~2–3k tokens). That is the price of a single definition of the model, and it is cheaper than maintaining a second authoring surface whose failure mode is a model that only exists in one direction. If agents demonstrably re-describe large models often, the cheapest fix is not `add` — it is a text `patch: {find, replace}` on `poly_describe`, which stays inside the `.poly` text path and adds no new language. Recommend not building it until the friction is observed.

### 2.6 `poly_check` — prove

This is the differentiating tool, and its honesty is the whole design.

Inputs (one shape, two granularities):

```
check(
  select?:  string    // "Order.Submit", "Order.Draft.Submit", "Order.Total", or a policy
  stage?:   string    // evaluate the action in this stage context only
  args?:    object    // proposed argument values, e.g. {"amount": 5000}
  expression?: string // a DSL expression fragment (no session needed)
  bindings?:   object // property bag for `expression`
  severity?: "errors" | "warnings" | "hints" | "all"   // default "errors"+"warnings"
  workspace?: string
)
```

Output `data` (targeted form):

```json
{
  "verdict": "violated",
  "subject": { "kind": "action", "entity": "Order", "action": "Submit", "stage": "Draft" },
  "property": "Total",
  "admissible": { "min": 0, "max": 1000, "source": ["declared range(0,1000)"] },
  "produced":   { "min": 0, "max": 5000, "source": "parameter amount + assign" },
  "witness":    { "value": 1001, "why": "any amount above 1000 lands outside Total" },
  "constraints": [
    { "kind": "range", "text": "range(0, 1000)", "source": "Order.Total" },
    { "kind": "range", "text": "range(0, 5000)", "source": "parameter amount" }
  ],
  "guards": [ { "policy": "can_submit", "value": "unknown", "why": "reads Total, which is not bound" } ]
}
```

What it answers, and the exact engine fact behind each answer:

| Question | Engine source | Fidelity |
|---|---|---|
| Whole-model validity | `AnalysisResult.Diagnostics` (DMSTR/DMSEM/DMEFF/DMCS/DMSS/DMDEP/DMBEH/DMREL/DMAS codes) | exact |
| Is a property's constraint set jointly satisfiable | `ConstraintQualityAnalyzer` — duplicate range/length/pattern merge, equality-vs-other, default-vs-other ([`ConstraintQualityAnalyzer.cs`](../../Poly/DomainModeling/Analysis/ConstraintQualityAnalyzer.cs), lines 91–135) | exact for literals |
| Can this action, in this stage, assign out of range | `EffectAnalyzer` disjoint-vs-overlap test ([`EffectAnalyzer.cs`](../../Poly/DomainModeling/Analysis/EffectAnalyzer.cs), lines 1378–1395) | error = disjoint, warning = overlap |
| What values are admissible for a property in this action+stage | `ActionStageInvariant.NarrowedRanges` / `MergedConstraints` (declared ∩ guard-narrowed) ([`EffectInvariantAnalyzer.cs`](../../Poly/DomainModeling/Analysis/EffectInvariantAnalyzer.cs), lines 29–34) | interval only |
| Do these argument values violate a constraint | parameter constraints merged into the target property's constraints on assign (`BuildPostconditionConstraints`, lines 428–448), plus `ConstraintValidation.IsSatisfiedBy` | exact for the merged set |
| Are the preconditions themselves unsatisfiable | `ReportUnsatisfiablePreconditions` → `DMCS001` (lines 452–464) | exact (empty set) |
| Does this expression hold for this binding | `oracle_expression`'s existing evaluator, or `EvaluatePolicy` on a live instance | exact where bound |

**The verdict is three-valued, not boolean: `proven | violated | unknown`.** This is not caution, it is the actual semantics: the abstract interpreter returns `AbstractValue.Unknown` for anything outside its numeric/equality model — division, a module not modelled, a symbol with no constraint, a guard that reads an unbound property. Today that unknown is silently absorbed; a check tool that reported `ok` on it would be lying. `unknown` must be first-class in `data.verdict`, and the `why` field must name the unmodelled part. An agent that gets `unknown` learns to bind the property or simulate instead; an agent that gets a false `proven` learns to distrust the tool.

**A "witness" is an interval witness, not a model.** For a numeric conflict, the witness is the boundary value the analyzer's own ranges imply (the smallest value in the guard's admissible set that the property rejects, or vice versa). For string, pattern, enum or equality conflicts there is no constructive witness; return the conflicting constraint pair and `witness: null`. Do not simulate a counterexample search — the engine has no such solver.

**What it must not claim:** cross-entity or relational reasoning beyond the documented call-chain and for-invoke propagation; arithmetic inversion beyond min/max endpoint composition; uniqueness (explicitly "cannot validate statically; requires instance set" — [`ConstraintValidation.cs`](../../Poly/DomainModeling/Analysis/ConstraintValidation.cs), line 19). `proven` means "no violation is derivable from this abstract model", and the description says exactly that.

`severity` defaults to errors + warnings; hints are opt-in because `DMAS001`/`DMAS002` are advisory authoring suggestions ([`DomainModelDiagnosticCodes.cs`](../../Poly/DomainModeling/Analysis/DomainModelDiagnosticCodes.cs), lines 53–55) and would otherwise drown the two findings that matter.

### 2.7 `poly_inspect` — read the model

Inputs: `select?: string` (default = overview), `detail?: "brief" | "full"` (default `brief`), `workspace?`.

`select` grammar — four forms, matched against the same dotted path findings use:

| `select` | Returns |
|---|---|
| omitted | `{name, entities, relationships, stages, actions, errors, warnings, uses}` |
| `Order` | entity: properties (type, constraints, requiredness, terms), stages, actions, policies, navigations, subscriptions |
| `Order.Total` | property: constraints with their source, admissible range, default, terms, which actions assign it |
| `Order.Submit` or `Order.Draft.Submit` | action: params, guards, effects, result type, per-stage invariant context (preconditions, narrowed ranges, postconditions) |
| `Order.can_submit` | policy: expression text, where it is attached, which actions it gates |
| `relationships` / `terms` / `uses` | the domain-level lists |

`detail: "brief"` is the default because the dominant token risk in a read tool is dumping the model. Brief returns names, types and counts; full returns constraint sources, invariant contexts and term metadata. `data.truncated` + a `next` sketch (`poly_inspect(select: "Order", detail: "full")`) covers the escalation.

This tool is where the solver's *facts* live (admissible ranges, preconditions, postconditions, merged constraints) — `poly_check` is where its *verdicts* live. That split is why they stay separate tools: the agent's decision differs ("what should I write next?" vs "may I ship?"), and so does the failure response.

### 2.8 `poly_export` — generate the artifact

Inputs: `format: "dsl" | "json-schema" | "csharp" | "markdown" | "html" | "json-ld" | "shacl"`, `workspace?`.

Output `data`: `{format, files: [{name, text}], entry?, bytes}` — always a file list, because `csharp` emits several files ([`OracleTool.cs`](../../Poly.Mcp/Tools/OracleTool.cs), lines 122–155) and one uniform shape beats a union.

| Format | Verdict | Why |
|---|---|---|
| `dsl` | **core** | the only lossless round-trip; it is also the "save my work" affordance and the way to get the whole model text without a read tool |
| `json-schema` | **core** | the machine contract agents ask for first; constraint projections (`range`, `length`, `pattern`, `default`, `enum`) are real and tested; behavior is deliberately not emitted |
| `csharp` | **core** | proves the domain is runnable; the lowered model is the product claim |
| `markdown` | **core** | the human-review artifact; also the PR body |
| `html` | keep | same facts as markdown, one self-contained page; one enum value, zero new code |
| `json-ld` | keep, second tier | pure projection over declared IRIs; earns its place only when the task is semantic-web interop, so it is never suggested in `next[]` |
| `shacl` | keep, second tier | same projection, validation-shaped; already built and tested |
| OWL / RDFS / OpenAPI / GraphQL / Protobuf / SQL / CSV / Excel | **not in the contract** | not built, and promising them in a description would be a lie. Adding one later is one enum value plus one projection — which is the argument for this shape |

Fails closed exactly as today: a model with analysis errors exports nothing and returns the errors ([`OracleTool.cs`](../../Poly.Mcp/Tools/OracleTool.cs), lines 128–131).

### 2.9 `poly_simulate` — run a scenario

Inputs: `steps: Step[]`, `reset?: bool` (default false), `stopOnError?: bool` (default true), `workspace?`.

Step vocabulary — **only what the executor can do**, nothing invented:

| `op` | Fields | Engine call |
|---|---|---|
| `create` | `entity`, `props?`, `as?` (handle alias) | `ModuleInstanceExecutor.Create(entityName, values)` |
| `link` / `unlink` | `from`, `relationship`, `to` | `executor.Link` / `Unlink` (store-aware, ownership-checked) |
| `invoke` | `on`, `action`, `args?` | `executor.Invoke(instance, actionName, args)` |
| `evaluate` | `on`, `policy` | `executor.EvaluatePolicy(instance, policyName)` |
| `get` | `on` (handle or `"*"`) | snapshot renderer |

`ModuleInstanceExecutor` exposes `Create`, `Invoke`, `EvaluatePolicy`, `Link`, `Unlink` and `SetProperty` ([`Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs`](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs), lines 102–280). **`SetProperty` is deliberately not exposed as a step.** Initial state belongs in `create.props`; a mid-run write is a host backdoor that lets a scenario repair data the model forbids, which inverts the tool's purpose. If a test needs it, that is a test concern, not an agent concern.

Output `data`:

```json
{
  "trace": [
    { "step": 0, "op": "create", "ok": true,  "handle": "Order#1", "stage": "Draft" },
    { "step": 1, "op": "invoke", "ok": false, "action": "Submit",
      "stage": { "from": "Draft", "to": "Draft" },
      "failedGuards": ["can_submit"],
      "effects": [] },
    { "step": 2, "op": "evaluate", "ok": true, "policy": "can_submit", "value": false }
  ],
  "final": [ { "handle": "Order#1", "entity": "Order", "stage": "Draft", "properties": { } } ],
  "failedAt": 1
}
```

Semantics that must be explicit, because they are the compound tool's main hazard:

- `ok` at the call level = "the steps could be executed". A guard-blocked `invoke` is `ok: false` **for that step**, `ok: true` for the call, plus a `findings` entry with `path: "step:1"`; an unknown action, entity, relationship or handle is `ok: false` for the call, because the scenario itself is malformed. Without this split an agent reads a correct guard rejection as a broken tool.
- `stopOnError: true` by default. Continuing after a failed invoke produces a trace whose later steps ran against unexpected state — confusing, and rarely what the caller wanted.
- Handles (`Order#1`) are stable for the life of the run and are the only way to reference an instance. `create.as` lets a scenario use readable names (`as: "order"`).
- Instances persist across calls. `reset: true` clears them. `poly_describe(apply: true)` clears them too — same rule as today, where a successful apply "clears prior runtime instances" ([`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 206).
- **No `runId`.** One run per workspace. Two concurrent runs is an A/B scripting concern, not a modelling one; the agent can `reset` and re-run. If it is ever needed, add `run: string` on the same pattern as `workspace` and change nothing else.

### 2.10 Tool descriptions and server instructions (verbatim drafts)

Each description names the job, the trigger, what comes back, and the one gotcha. 1–3 sentences, ≤ 50 words.

**`poly_guide`**
> Returns the `.poly` language surface: the minimal valid document, the authoring workflow, the constructs to avoid, and a section index. Call it before writing a model the first time in a session. Pass `topic` for one section or an example domain.

**`poly_describe`**
> Parses and analyzes a `.poly` document and, unless `apply` is false, makes it the current model — replacing the previous one and clearing simulated instances. Returns findings with code, element path and suggested fix; a rejected document changes nothing. Use `apply: false` to test a design without replacing the model.

**`poly_check`**
> Runs the constraint solver on the current model: whole-model errors and warnings, or one targeted question about an action, an argument set, a property or an expression. Returns a three-valued verdict — proven, violated or unknown — with the admissible range, a witness when one exists, and a suggested fix. `proven` means no violation is derivable from the analyzer's abstract model.

**`poly_inspect`**
> Reads the current model: the overview, or one entity, property, action, policy or relationship, with the constraints and value ranges the solver derived. Read-only. Keep `detail` brief unless you need constraint sources and invariant contexts.

**`poly_export`**
> Generates an artifact from the current model: `dsl` (lossless round-trip), `json-schema`, `csharp`, `markdown`, `html`, `json-ld` or `shacl`. Fails closed when the model has errors. Use `dsl` to save a model or edit it by hand.

**`poly_simulate`**
> Runs an ordered scenario against the current model — create, link, unlink, invoke, evaluate, get — and returns one trace entry per step. Instances persist between calls; `reset: true` starts from an empty instance set. A guard-blocked invoke is a failed step, not a failed call.

**Server instructions** (replaces [`PolyMcpInstructions.cs`](../../Poly.Mcp/PolyMcpInstructions.cs)):

```
Poly is an executable ontology tool. Use it when a task has a domain: instead of
hard-coding assumptions, describe the domain you are working in — entities,
properties, constraints, stages, actions, policies — prove the description is
consistent with the constraint solver, then produce the tools or artifacts the
task needs from that one model.

Six tools, one job each:
- poly_guide      learn the .poly surface (call once, before authoring)
- poly_describe   author: hand it the whole .poly document; it validates and applies
- poly_check      prove: model validity, or one targeted action/argument question
- poly_inspect    read one element, with the constraints the solver derived
- poly_export     produce dsl, json-schema, csharp, markdown, html, json-ld or shacl
- poly_simulate   run a scenario and read the trace

Loop: guide → describe → check → fix → export or simulate. One workspace per
connection; poly_describe replaces the model, poly_simulate accumulates instances.
Save anything you want to keep with poly_export(format: "dsl") — the workspace is
process-local and dies with the server.

Rules that matter:
- Author only the shipped surface. poly_guide lists what runs and what is rejected.
- poly_check's verdict can be "unknown": the analyzer models numeric ranges and
  equality, not arithmetic, strings or relationships. Bind what you can, or simulate.
- A rejected document changes nothing. Read the finding's path and fix the .poly.
- The constraint solver is the point. If it rejects an action, fix the model; do not
  weaken or delete a constraint to get past a diagnostic.
```

### 2.11 Migration map — 30 → 6

| Today | New | Note |
|---|---|---|
| `create_domain_session`, `list_sessions` | — | identity is the connection; the domain name comes from the document |
| `apply_dsl` | `poly_describe(apply: true)` | default |
| `validate_dsl` | `poly_describe(apply: false)` | same findings |
| `add`, `remove` | — | deleted; `.poly` text is the only authoring path |
| `get_dsl_guide` | `poly_guide` | default is ~700 chars, not ~7k |
| `get_domain_analysis` | `poly_check()` | gate form |
| `get_domain_suggestions` | `poly_check(severity: "hints")` | `DMAS001/002` |
| `get_domain_overview` | `poly_inspect()` | |
| `get_entity_detail` | `poly_inspect(select: "Order")` | |
| `get_constraints` | `poly_inspect(select: "Order.Total")` | constraints gain a source (`declared`/`guard`/`parameter`) |
| `get_relationships` | `poly_inspect(select: "relationships")` | |
| `get_policy_expression` | `poly_inspect(select: "Order.can_submit")` | merges with `describe_domain_element` |
| `describe_domain_element` | `poly_inspect(select: …)` | one selector replaces five names |
| `get_domain_report` | `poly_export("markdown")` | |
| `export_dsl` | `poly_export("dsl")` | |
| `export_domain_to_json_schema` | `poly_export("json-schema")` | |
| `export_domain_to_csharp` | `poly_export("csharp")` | |
| `export_domain_to_html` | `poly_export("html")` | |
| `export_domain_to_json_ld` | `poly_export("json-ld")` | second tier |
| `export_domain_to_shacl` | `poly_export("shacl")` | second tier |
| `create_instance` | `poly_simulate([{op:"create"}])` | `props` in the step |
| `link_instances`, `unlink_instances` | `poly_simulate([{op:"link"|"unlink"}])` | |
| `get_instance` | `poly_simulate([{op:"get"}])` or the `final` block | |
| `list_instances` | `poly_simulate([{op:"get", on:"*"}])` | capped |
| `invoke_action` | `poly_simulate([{op:"invoke"}])` | guard attribution preserved (`failedGuards`) |
| `evaluate_policy` | `poly_simulate([{op:"evaluate"}])` | |
| `oracle_expression` | `poly_check(expression, bindings)` | works with no model for primitive-only fragments |

### 2.12 Buildability

- `poly_guide` — embedded resource, already loaded by `DslTool` ([`DomainTools.cs`](../../Poly.Mcp/Tools/DomainTools.cs), lines 1528–1545); add a `quickstart` composition.
- `poly_describe` — the existing `validate_dsl` path plus `store.Replace`; no new parser work.
- `poly_check` — a projection over `AnalysisResult.Diagnostics` + `EffectInvariantQueries` + `ConstraintValidation.Describe`. Zero analyzer changes: everything it reports is already computed and already published (that no MCP tool consumes it today is the gap this closes).
- `poly_inspect` — `DomainQueries`, `BehaviorMetadata`, `OwnershipAggregateMetadata`, `TermFacets`; all exist.
- `poly_export` — existing exporters are pure projections over `Domain` ([`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 82–92); the tool adds a `switch`.
- `poly_simulate` — the only real new code: a step loop, handle registry and trace renderer over `ModuleInstanceExecutor`. Still no semantics change, because every step is one existing method call.

---

## Q3: Which current capabilities have no home, and is that acceptable?

### Takeaway

Three things lose their home on purpose (incremental authoring, session lifecycle, `SetProperty`), and three lose it incidentally (`list_instances` enumeration, bare-bag expression probing, the HTML/Markdown duplication). The deliberate losses are acceptable and are the point; the incidental ones are covered by a cheap `on: "*"` and an empty-model `check`, except enumeration, which is reduced but not lost.

### Cited Findings

- `remove` cannot delete a constraint; the documented workaround is `apply_dsl` — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 37.
- The incremental payload table lives in guide §12 and lists nine kinds plus contract shapes — [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), §12 (3,551 chars).
- `.poly` is N1-only for navigations, so incremental `add relationship` accepts cardinalities the document cannot express — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), line 24 and the guide's §4 title.
- `oracle_expression` is a session-free fragment probe that "fail-closed for those reads" on relationships — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 59 and 207.
- `SetProperty` exists on the executor and is used by the host/tests, not by any MCP tool — [`ModuleInstanceExecutor.cs`](../../Poly/DomainModeling/Runtime/ModuleInstanceExecutor.cs), line 276.
- Instances are session-scoped and enumerated by `list_instances` with an optional entity filter — [`RuntimeTool.cs`](../../Poly.Mcp/Tools/RuntimeTool.cs), lines 564–570.

### Inferences

| Capability | Status | Assessment |
|---|---|---|
| Incremental `add`/`remove` | deleted | **Acceptable and desirable.** It is a weaker second definition of the model that cannot round-trip. The one real cost is re-sending a large document (Q2.5). |
| `create_domain_session` / `list_sessions` | deleted | **Acceptable.** There is nothing to list and nothing to own once identity is the connection. |
| `SetProperty` mid-run | excluded | **Deliberate.** It would let a scenario repair data the model forbids. |
| Instance enumeration | reduced | `get(on: "*")` returns handles (capped). Enumeration is a debugging affordance, not a modelling one; a trace usually names the handles already. |
| Bare-bag expression probe | kept | `poly_check(expression, bindings)` on an empty workspace evaluates primitive-only fragments; relationship reads return `unknown` instead of a fail-closed error. Slightly *better* than today, because unknown is visible. |
| Markdown vs HTML | both kept | Same projection, different rendering; one enum value each. Not a home question. |
| `get_domain_suggestions` | merged | Advisory hints are findings; the tool was only a `severity` filter. |
| `get_policy_expression` | merged | A policy is a `select` path. |
| Contract/`uses` composition | never had a tool | Correct: composition is language-level ([`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), lines 192–201), not a session operation. |

### Gaps

- No in-repo usage telemetry to confirm that incremental authoring was rarely used. The deletion argument is structural.
- If agents do need to enumerate instances routinely, `get(on: "*")` may prove too coarse; the escape hatch is a `select: "instances"` on `poly_inspect`, which would blur the model/runtime split. Do not add it pre-emptively.

---

## Q4: What failure modes does the contract introduce, and how are they mitigated?

### Takeaway

Four new failure classes appear: implicit state, compound-tool partial failure, overloaded selectors/formats, and three-valued verdicts. Each has a cheap, structural mitigation — `state` in every envelope, `ok` defined per job, the finding-path-as-selector identity, and `unknown` as a first-class verdict rather than a silent success.

### Cited Findings

- Today's envelope is `{success, message, sessionId, revision, data, diagnostics[], affordances[]}` — [`DomainTools.cs`](../../Poly.Mcp/Tools/DomainTools.cs), lines 25–33.
- `affordances` carries bare tool names, e.g. `["get_domain_analysis"]` — same file, lines 452–460.
- Today a failed call can be a missing session, a missing analysis, a model with errors, or a lowering failure — four shapes across `OracleTool` ([`OracleTool.cs`](../../Poly.Mcp/Tools/OracleTool.cs), lines 126–131).
- The session store evicts idle sessions after 8h and the process is in-memory only — [`Poly.Mcp/README.md`](../../Poly.Mcp/README.md), lines 211–215.
- `invoke_action` distinguishes a guard block from an execution failure and names the guard — [`RuntimeTool.cs`](../../Poly.Mcp/Tools/RuntimeTool.cs), lines 700–740.
- The DSL guide's "Do NOT Use" section is 627 chars of fail-closed constructs — [`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md), §10.

### Inferences

| Failure mode | What it looks like | Mitigation |
|---|---|---|
| **Implicit state** — "which model?" | Agent reconnects or the server restarts; the next `inspect` returns an empty model and the agent assumes its model was rejected | `state` in every envelope; `revision` resets to 0 on restart; empty-model `next[]` names `poly_guide` and `poly_describe`; `summary` says "No model." not "error" |
| **Silent overwrite** | `poly_describe(apply: true)` on a model the agent did not intend to replace | `summary` names the domain and counts; `revision` bumps visibly; `apply: false` exists for experiments; the description says "replacing the previous one" |
| **Compound partial failure** | `simulate` step 3 of 5 fails; agent cannot tell if the call failed | `ok` = "job ran", per-step `ok`, `failedAt`, `summary` names the step; `stopOnError` default |
| **Guard block read as tool error** | A correct `require` rejection looks like a broken scenario | `failedGuards` on the step (already produced by `MapGuardFailure`), and the description states the rule |
| **Overloaded selector / format** | Agent invents `select: "Order.Total.range"`; unknown enum value | Fail closed with `POLY401` + `candidates[]`; the selector grammar is the same dotted path findings emit, so the correct value is always one paste away |
| **Detail dumping** | `inspect(detail: "full")` on a 40-entity model returns 40k tokens | `brief` is the default; `truncated` + a `next` sketch; `export("dsl")` is the sanctioned full-text path |
| **Verdict drift** | `check` says `proven`, runtime still blocks (ids, uniqueness, storage overlay) | `proven` is scoped in the description and in `data.verdictWhy`; `unknown` is never reported as `proven`; uniqueness is explicitly out of scope |
| **Fewer tools, more per call** | One `simulate` call does what six did, so a single mistake costs more | Steps are data: the agent can re-send a corrected scenario; `reset` makes the retry deterministic; the trace localizes the bad step |
| **Stale skill** | An agent with the old skill calls `apply_dsl` and gets "unknown tool" | Acceptable at 0 consumers; the error names the server, and server instructions list all six names. Add the old→new map to `poly_guide` for one release if a skill is known to be cached |

### Gaps

- No data on how often agents re-derive a model after a server restart, so the cost of implicit state is reasoned, not measured.
- The `unknown` rate of the abstract interpreter is unmeasured (no instrumentation counts `AbstractValue.Unknown`). If it is high, `poly_check` will answer `unknown` often and lose credibility; the first implementation should count it and report the share in `summary` (e.g. "2 of 9 facts unmodelled").
