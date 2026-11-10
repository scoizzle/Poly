# Poly MCP 2.0 — evaluation, surface design, and migration plan

**Date:** 2026-11-06
**Repo:** `/Users/scoizzle/.t3/worktrees/Poly/t3-4cc2823d` @ `01234078` (working tree also has: `M Poly.Mcp/Program.cs`, `?? Poly.Mcp/PolyMcpInstructions.cs` — not mine, preserved)
**Scope:** replace the 30-tool MCP surface with ~7 outcome tools, zero external consumers, engine and DSL unchanged.
**Constraint honoured:** no repo source was modified. I built `Poly.Mcp` and drove it over stdio to measure the real surface; `git status` shows only the pre-existing dirty files plus `research_notes/`.

---

## 0. Headline

1. **The token argument is weaker than the folklore.** The current 30-tool surface is **18,218 characters** of fixed context: **16,464** for `tools/list` + **1,754** for server instructions. That is roughly **4.6k–5.5k tokens**, not the 50k–134k seen with multi-server MCP fleets ([Anthropic](https://www.anthropic.com/engineering/advanced-tool-use)). A 7-tool surface saves ~3k tokens **once per conversation**, not per turn. If the case for 2.0 leans on tokens, it is weak.
2. **The real costs are per-call, not per-surface.** On a four-entity domain, `get_domain_analysis` returns **5,280 chars**, `get_dsl_guide` **9,111** (default) / **7,433** (`section:"0"`), `export_domain_to_csharp` **12,796**. Those dominate. 2.0 must shrink *results* and *turns*, not just the tool list.
3. **Confusion is the defensible case.** Anthropic names wrong tool selection and wrong parameters as the most common tool failures, and 30 tools with overlapping verbs (`get_domain_overview` / `get_entity_detail` / `describe_domain_element` / `get_domain_analysis`; `add` / `apply_dsl` / `validate_dsl`) is exactly that shape ([Anthropic](https://www.anthropic.com/engineering/advanced-tool-use), [writing tools](https://www.anthropic.com/engineering/writing-tools-for-agents)).
4. **Design bet:** one document in, one model out (`poly_define`), one scenario in, one trace out (`poly_simulate`), one projection out (`poly_inspect`), one artifact out (`poly_export`), one human doc out (`poly_report`), plus `poly_guide` and `poly_open`. Seven tools, explicit sessions, transactional simulation, capped responses.
5. **The eval must be pre-registered before the 2.0 descriptions are written**, or it measures the author, not the surface.
6. **Acceptance is non-inferiority on success plus superiority on reliability/selection**, with tokens as a secondary gate. Concretely: pass@1 not worse, pass^5 +0.10, wrong-tool ≤5%, fixed surface ≤50% of baseline, total tokens ≤80% of baseline, no task regression >1/5 trials (§9).

---

## 1. What I verified (facts used by the plan)

### 1.1 Surface size, measured

Method: `dotnet build Poly.Mcp/Poly.Mcp.csproj`, then a JSON-RPC `initialize` → `notifications/initialized` → `tools/list` over stdio against `dotnet Poly.Mcp/bin/Debug/net10.0/Poly.Mcp.dll`. Sizes are minified JSON characters. No tokenizer is installed locally, so token figures are an estimate band at 3.3–4.0 chars/token and are labelled as such; the harness must use the provider's `count_tokens` endpoint for the official number.

| Item | Chars | Est. tokens |
|---|---:|---:|
| `tools/list` result (30 tools) | 16,464 | 4.1k–5.0k |
| — descriptions | 6,705 | |
| — input schemas | 7,867 | |
| server `instructions` | 1,754 | 0.4k–0.5k |
| **Fixed surface total** | **18,218** | **4.6k–5.5k** |

Tool-result sizes on `docs/probes/fleet-eval/12-mcp/mcp-library.poly` (4 entities):

| Call | Chars | Est. tokens |
|---|---:|---:|
| `get_dsl_guide` (default) | 9,111 | 2.3k–2.8k |
| `get_dsl_guide(section:"0")` | 7,433 | 1.9k–2.3k |
| `get_domain_analysis` | 5,280 | 1.3k–1.6k |
| `export_domain_to_csharp` | 12,796 | 3.2k–3.9k |
| `export_domain_to_html` | 10,602 | |
| `export_domain_to_shacl` | 8,910 | |
| `export_domain_to_json_schema` | 3,256 | |
| `get_domain_report` | 2,119 | |
| `export_dsl` | 1,231 | |
| `get_domain_overview` | 351 | |
| `get_entity_detail(Loan)` | 780 | |

### 1.2 Harness facts

- The server is **stdio only** (`Program.cs`: `WithStdioServerTransport()`), wired with seven `[McpServerToolType]` classes and a `ToolExceptionBoundary` filter.
- **No test in the repo speaks MCP.** `Poly.Tests/Mcp/*` (25 files) call tool methods **in-process** through `Poly.Tests/TestHelpers/McpTestTools.cs`, which hand-wires the same singletons as `Program.cs`. A repo-wide grep for `StdioClientTransport`, `ProcessStartInfo` and `ModelContextProtocol.Client` found **zero** matches. The protocol layer — schemas, required params, `isError`, unknown-argument handling — is currently untested.
- Tests are TUnit, run as `dotnet run --project Poly.Tests/Poly.Tests.csproj` (not `dotnet test`); filter with `-- --treenode-filter`.
- Response envelope is uniform (`DomainToolResponse`): `success`, `message`, `sessionId`, `revision`, `data`, `diagnostics`, `affordances`. `ToolExceptionBoundary` turns uncaught exceptions into `isError: true` + the same envelope.
- Sessions are **explicit** (`sessionId` is a required parameter on every stateful tool) and process-local, with an 8h idle TTL / 15min sweep (`McpSessionOptions`). Good: the 2.0 plan should not make sessions implicit.
- The `Program.cs` working-tree diff turns on `ServerInstructions = PolyMcpInstructions.Text`; `PolyMcpInstructions.cs` is untracked. That text is already part of the surface under test, so the baseline must capture it (Phase 0).
- `get_dsl_guide` is the only tool that reads a docs file (`Poly.Mcp/Docs/poly-dsl-guide.md`, 1,325 lines) and it returns prose, not structure.

### 1.3 Defects observed while acting as a blind client

These are the failure modes 2.0 must fix and the contract suite must pin. I hit them by guessing, exactly as an agent would:

1. **Parameter name ≠ description.** The description says "Replaces the session domain with a `.poly` document"; the actual parameter is **`polyText`**. Passing `document` produced `isError: true` and `"missing a value for the required parameter 'polyText'"`.
2. **Unknown arguments are silently ignored.** `create_instance(..., values: {...})` (the real parameter is `propertiesJson`, a JSON **string**) did not reject the unknown key; it proceeded and failed later with `'Isbn' is required`, which points at the wrong cause.
3. **Free-form JSON bags.** `create_instance.propertiesJson: string`, `add.payload: string` — the repo's own guiding principles call this an anti-pattern (`docs/reference/mcp-guiding-principles.md` §4).
4. **Result shape is not guessable.** `create_instance` returns `data.instance` (a snapshot), not `data.instanceId`; my grader script crashed on the difference.
5. **The success path costs an extra 5k-char call.** `apply_dsl` returns diagnostics only on failure; to learn about warnings the agent must call `get_domain_analysis` (5,280 chars on a 4-entity model).

None of these are engine bugs. All are surface ergonomics, and all are cheap to test.

---

## 2. Proposed MCP 2.0 surface (7 tools)

Design rules applied: outcomes over endpoints; one noun per tool; flat args; enums instead of booleans where intent differs; no free-form JSON bags; explicit sessions; every mutation returns `revision` + `applied`; every response carries a size cap; no tool creates state implicitly.

| # | Tool | Replaces | Args (flat) | Returns | Why it is an outcome |
|---|------|----------|-------------|---------|----------------------|
| 1 | `poly_guide` | `get_dsl_guide` | `section?: string`, `topic?: "workflow"\|"syntax"\|"limits"` | short workflow + section index; full text only when `topic`/`section` asks | "teach me the language" without dumping 9k chars |
| 2 | `poly_open` | `create_domain_session`, `list_sessions` | `op: "create"\|"list"\|"close"`, `name?`, `sessionId?` | `sessionId`, `revision`, sessions | session lifecycle stays explicit |
| 3 | `poly_define` | `validate_dsl`, `apply_dsl`, `add`, `remove` | `sessionId`, `polyText`, `mode?: "check"\|"apply"` | `applied`, `revision`, `errors[]` (code, path, line, message), `warnings[]` (capped 10), `counts`, `affordances` | "make the model real, or tell me why not" in one call |
| 4 | `poly_inspect` | `get_domain_overview`, `get_entity_detail`, `describe_domain_element`, `get_domain_analysis`, `get_relationships`, `get_constraints`, `get_domain_suggestions`, `get_policy_expression` | `sessionId`, `focus: "overview"\|"entity"\|"element"\|"analysis"\|"relationships"\|"suggestions"`, `name?`, `limit?` | one projection, capped, `truncated` + `total` when capped | "answer one question about the model" |
| 5 | `poly_simulate` | `create_instance`, `link_instances`, `unlink_instances`, `get_instance`, `list_instances`, `invoke_action`, `evaluate_policy` | `sessionId`, `steps: Step[]`, `commit?: bool` (default false) | `ok`, `steps[]` with `index/op/ok/message/instanceId/stage/policy`, `mutationCommitted` | "prove the lifecycle works" in one call, one trace |
| 6 | `poly_export` | `export_dsl`, `export_domain_to_csharp`, `export_domain_to_json_schema`, `export_domain_to_json_ld`, `export_domain_to_shacl` | `sessionId`, `format` enum, `inline?: bool` | `format`, `text` (≤4k chars) or `{path, bytes, sha256}` | "give me a machine artifact" |
| 7 | `poly_report` | `get_domain_report`, `export_domain_to_html` | `sessionId`, `format: "markdown"\|"html"`, `audience?: "review"\|"operate"` | `format`, `text`/`{path,...}` | "give a human something they can read" |

**Scenario step schema** (the only structured arg; deliberately small, not a second IR):

```text
{ op: "create",  as?: "r1", entity: "Return", values?: { ... } }
{ op: "link",    instance: "r1", relationship: "lines", target: "l1" }
{ op: "act",     instance: "r1", action: "Approve", arguments?: { ... } }
{ op: "policy",  instance: "r1", policy: "WithinWindow" }
{ op: "expect",  instance: "r1", stage: "Received" }
```

**Deliberate non-goals**

- No incremental `add`/`remove`. The document is the unit. Measure the cost (re-sending a 2k-char document per iteration); only add a patch mode if the eval shows it dominates. This is the single biggest behavioral bet in 2.0 and it is the one most worth measuring.
- No `oracle_expression`. It is a fragment probe that duplicates `evaluate_policy` and invites confusion (README says so explicitly).
- No resources in v1 of 2.0. `poly_guide` and `poly_export` cover the need without depending on client resource support.
- No implicit session creation. Every stateful tool without a valid `sessionId` returns `error.code = "SESSION_REQUIRED"` with `poly_open` as the affordance.

**Two sub-decisions to A/B separately (cheap, 3 tasks each)**

1. `poly_` prefix vs bare names (`guide`, `open`, `define`, …). Anthropic reports prefix-vs-suffix naming has non-trivial effects that vary by model.
2. `poly_define` as one `mode` enum vs `poly_check` + `poly_apply` as two tools (8 tools). If wrong-tool rate on "check without applying" is high, split it.

---

## 3. Task suite (14 tasks)

Rules: prompts never name a Poly tool; each task has a **frozen reference solution** (`evals/fixtures/<task>.poly` plus a canned tool-call transcript) that must score 100% via replay before any model is run; each task declares an `allowed` tool set (generous) and a minimal `required` set (per [Anthropic's warning against grading the path](https://www.anthropic.com/research/demystifying-evals-for-ai-agents)); graders are code, not an LLM.

Tool-name mapping for the A arm is mechanical: `define→{validate_dsl,apply_dsl,add,remove}`, `inspect→{the 8 read tools}`, `simulate→{the 7 runtime tools}`, `export→{5 exports}`, `report→{2}`, `open→{2}`, `guide→{1}`.

### T1 — Model a returns policy
```text
You are building the backend for an online store's returns flow. Model the domain:
a customer requests a return of one or more items; a return moves requested → approved
→ received → refunded, and can be rejected from requested or approved; a refund is only
allowed after the return is received and only up to the original amount; a return reason
is one of damaged, wrong-item, changed-mind. The model itself — not application code —
must reject a refund before receipt. Then show me the JSON Schema your API would use.
```
**Pass iff** analysis has 0 errors; stages form that chain in that order; a guard on the refund action references the received stage and the amount bound; simulate: refund in `approved` fails, refund in `received` succeeds; JSON Schema `$defs` contains every entity and the reason enum. (Stages/actions are not emitted by the schema exporter — grader must not assert them there.)

### T2 — Find a contradictory transition
```text
Here is a draft returns model. Something is wrong with it: a return can end up refunded
without ever being received. Find the contradiction, name it, and fix it without weakening
the rule that money only moves after receipt.
<fenced .poly draft with a stage subscription Requested -> Refunded>
```
**Pass iff** the reply names the offending element (subscription/transition, not "a policy"); final model analyses clean; the refund guard still references receipt; simulate: direct refund from `requested` fails, refund after `received` succeeds.

### T3 — Simulate a lifecycle, including an illegal step
```text
Model a support ticket: new → triaged → in-progress → resolved, reopened from resolved,
closed only from resolved. Then walk one ticket through a legal path and one illegal
transition, and report exactly what the engine said at each step.
```
**Pass iff** trace has ≥5 steps; the illegal step reports failure with a reason; the instance stage after the failed step equals the stage before it (no partial mutation).

### T4 — Generate an API contract without changing the model
```text
Here is our order model. Do not change it. Produce the JSON Schema and the C# types
our API layer should use.
<fenced .poly>
```
**Pass iff** `export(dsl)` equals the fixture (semantic compare); schema asserts `required`, `minimum`/`maximum` from `range`, `maxLength` from `length`, enum members; C# compiles with `scripts/probe-check` (0 errors, 0 warnings).

### T5 — Hand a human a report
```text
Write a one-page review document a product owner can read. It must say what the model
allows, what it forbids, and which rules they should confirm with legal.
<fenced .poly for the returns domain>
```
**Pass iff** every entity appears; ≥1 constraint fact per constrained property; the forbidden transition is stated in words; no entity/property name outside the fixture; ≤2,500 words; HTML variant parses.

### T6 — Minimal change to an existing model
```text
Add partial refunds: a new action that refunds part of the amount, and a rule that all
refunds together cannot exceed the original amount. Everything else must stay exactly
as it is.
<fenced .poly>
```
**Pass iff** exported DSL adds only the action + rule (+ any state it needs); every pre-existing action/policy/constraint is unchanged; simulate: two partial refunds exceeding the original → second fails; two within → succeed.

### T7 — Relationships and automatic side effects
```text
Model a library: patrons check out books; a loan is due 14 days after checkout; when a
loan goes overdue a fine is created automatically for the patron; returning the book
closes the loan and leaves the fine on record.
```
**Pass iff** a patron→loan relationship exists; overdue creates a fine without an explicit action call; simulate: overdue → fine exists and is linked to the patron; return → loan in returned/closed stage; fine still present.

### T8 — The expressiveness boundary
```text
Model a rule that a customer may have at most 3 open returns at a time.
```
**Pass iff** either (a) the reply states the language cannot express a cardinality cap, cites the closest expressible alternative, and the model contains no false claim of enforcement; or (b) simulate shows the 4th open return rejected by a guard. **Fails** if the model claims the rule while nothing enforces it. This is the anti-hallucination task and the one most likely to expose description overreach.

### T9 — Temporal rule
```text
A refund must be requested within 30 days of delivery.
```
**Pass iff** the guard uses a temporal construct (`Today`/duration) rather than a literal date; with the harness clock fixed, a return 31 days after delivery is rejected and one 10 days after is accepted. *Requires clock injection — see §6 gap G4.*

### T10 — Irrelevance (should NOT use Poly)
```text
Summarize the tradeoffs between event sourcing and CRUD for a small team.
```
**Pass iff** ≤1 Poly call total (a single `poly_guide` is tolerated) and no mutation. Catches over-triggering, the mirror of the discoverability risk ([Anthropic](https://www.anthropic.com/research/demystifying-evals-for-ai-agents): test both directions).

### T11 — Refuse to force an inconsistent model
```text
Make this executable.
<fenced .poly with an undeclared transition target and a duplicate unique entry effect>
```
**Pass iff** the reply names the specific diagnostic (code + element path); either the model is unchanged (revision unchanged) with the conflict explained, or the intent-declaring element is changed with an explicit statement of what was changed and why. **Fails** if a constraint is silently dropped to make it pass, or if a "success" is claimed while errors remain.

### T12 — Interop artifacts agree
```text
Emit JSON-LD and SHACL for this model. The IRIs must agree between the two.
```
**Pass iff** both parse; for every class/property the term IRI is identical; SHACL node shapes target the same classes.

### T13 — Cold start (onboarding)
```text
We run a hotel: rooms, reservations, cancellations. Model it and prove a guest cannot
cancel after check-in.
```
**Pass iff** the first Poly call is `guide` or `open` (not a query on a non-existent session); model valid; simulate rejects cancel-after-check-in; ≤12 tool calls. This measures "time to first successful model" directly.

### T14 — Two sessions, one process
Harness sends T13 then T1 to the same server process, then asks each session for its entity list.
**Pass iff** session ids differ, each export contains only its own entities, and no `inspect` call returns the other session's data. This is the explicit-state regression test.

### Corpus shape
`12 scored tasks (T1–T9, T11–T13) + 1 negative (T10) + 1 isolation (T14)`.
Add 3 held-out tasks (different domain: insurance claim, restaurant reservation, CI pipeline) **after** the 2.0 descriptions are frozen, run once, and report separately. They are the defence against description overfitting.

---

## 4. Metrics

Primary (decision-bearing):

| Metric | Definition | Collection | Direction |
|---|---|---|---|
| `task_success` | code graders all pass for a trial | graders | ↑ |
| `pass@1` | mean over tasks of success rate | computed | ↑ |
| `pass^k` (k=5) | fraction of tasks passing **all** k trials ([τ-bench](https://arxiv.org/abs/2406.12045)) | computed | ↑ |
| `wrong_tool_rate` | calls outside the task's `allowed` set ÷ all calls | transcript | ↓ |
| `first_valid_model_calls` | tool calls until the first `define/apply` with 0 errors | transcript | ↓ |
| `first_valid_model_seconds` | wall clock, same point | transcript | ↓ |
| `recovery_success` | seeded-error trials reaching a valid model | graders | ↑ |
| `recovery_cycles` | failed apply → successful apply count | transcript | ↓ |
| `total_tokens_per_task` | provider usage, summed over turns | usage | ↓ |
| `fixed_surface_tokens` | `count_tokens(tools/list + instructions)` | provider `count_tokens` | ↓ |
| `tool_result_tokens` | `count_tokens` of every tool result text | tokenizer | ↓ |
| `peak_context_tokens` | max prompt tokens in a trial | usage | ↓ |
| `zero_tool_rate` | negative tasks with ≤1 call | transcript | ↑ |

Secondary / diagnostic (never gating alone):

- `tool_calls_per_successful_task` — the compound-tool payoff.
- `redundant_calls` — same tool+args repeated with no state change (catches "agent is lost", not "agent is thorough").
- `invalid_arg_calls` — `isError` caused by schema/arg mismatch (the `polyText` class).
- `artifact_bytes_offloaded` — artifact size returned as a path instead of inline text.
- `human_report_score` — rubric on T5, LLM or human, reported separately. It never enters the pass gate because it cannot be made deterministic.

Two accounting rules that matter:

1. **Tool definitions are not re-paid every turn** when the provider caches prompts. Report `cached_input_tokens` and `uncached_input_tokens` separately, and report `fixed_surface_tokens` as a one-time cost. Do not claim per-turn savings that the invoice does not show.
2. **Separate "tokens" from "context".** A 12.8k-char C# export offloaded to a file is a context win even if the artifact bytes are identical.

---

## 5. A/B design

### 5.1 Arms

- **A (baseline):** current surface, `POLY_MCP_SURFACE=1` (30 tools), same commit, same engine.
- **B (2.0):** `POLY_MCP_SURFACE=2` (7 tools).
- **A′ (sanity):** `HEAD` without the working-tree instructions change, to confirm the instructions text is not the effect.

Both surfaces live in **one binary** behind an env flag during the A/B. That is the key control: the only variable is the tool surface, not the build, the engine, or the machine. Phase 7 deletes A.

### 5.2 Trial protocol

- Fresh **process per trial** (the session store is process-local; this is the strongest isolation guarantee). Record startup time separately so it does not pollute `time_to_first_model`.
- Fresh conversation per trial; identical system prompt across arms (the server's `instructions` are *part of the surface*, so hold them byte-identical between A and B for the primary run; run a B′ ablation with 2.0-specific instructions only if B underperforms).
- Fixed model snapshot string (never a floating alias), temperature 0 where supported, fixed `max_tokens`, tool-call cap 25, per-call timeout 30s, per-trial timeout 10min, kill the process tree on timeout.
- Arm order alternated per (task, trial) with a recorded seed, so API drift and time-of-day hit both arms equally.
- **N=3 pilot**, **N=5 main run**, **N=5 held-out once**. 5 trials gives pass^5 and a usable per-task pass@1 (0, .2, .4, .6, .8, 1).
- Two model families (one Anthropic, one OpenAI-compatible). If only one shows the effect, report that as a generalization gap — do not average it away.

Volume: 12 tasks × 2 arms × 5 trials × 2 models = 240 trials. At 6–10 turns and 4–8k prompt tokens per turn that is order 10–20M input tokens; estimate from the run's own usage report, and run the pilot first. Parallelize 4–8 workers; rate limits are the practical ceiling.

### 5.3 Scoring without an LLM judge

Six deterministic graders, all runnable offline from the transcript + final artifacts:

| Grader | What it does | Oracle |
|---|---|---|
| `G1 model` | fetch `export(dsl)`, parse with Poly's own parser, compare a canonical shape (entities, property types, constraints, stage order, actions, guards, subscriptions, relationships, term metadata) | Poly's parser is the normalizer, so formatting/ordering noise disappears |
| `G2 scenario` | replay a fixed step list, compare per-step `ok`/stage/policy | engine is the oracle |
| `G3 artifact` | parse JSON Schema / JSON-LD / SHACL; compile C# with `scripts/probe-check`; assert subset facts | schema validators + Roslyn |
| `G4 report` | markdown/HTML structure: required headings, fact coverage, forbidden names, word cap | string/AST checks |
| `G5 transcript` | call count, allowed/required sets, redundancy, error classes, token totals | the transcript |
| `G6 refusal` | no mutation, named diagnostic, revision unchanged | the transcript + server state |

Path grading is deliberately generous: `required` is "at least one model-producing call" and "at least one artifact call", never an exact sequence. Everything else is `allowed`. This follows the observation that strict trajectory matching punishes valid exploration ([BFCL V3](https://gorilla.cs.berkeley.edu/blogs/13_bfcl_v3_multi_turn.html), [Anthropic](https://www.anthropic.com/research/demystifying-evals-for-ai-agents)).

**Validate the graders before spending on models.** Each task ships a canned tool-call transcript (the reference solution). A `--replay` driver feeds those calls through the same graders and must produce 14/14. If a reference solution fails a grader, the grader is wrong, not the model.

Statistics: paired per task. Report per-task `pass@1` deltas, aggregate mean with a bootstrap 95% CI over tasks, `pass^5` counts, and a paired sign test on the 12 task deltas. With n=12 this is directional evidence, not proof; say so. Require the direction to hold in two independent runs (or two models) before declaring an effect.

### 5.4 Transcript schema

JSONL, one file per trial, plus one `surface.json` + sha256 per run:

```json
{"trial":"T06-B-3","task":"T06","arm":"B","model":"<snapshot>","seed":17,
 "turns":[{"i":1,"usage":{"in":4210,"out":312,"cache_read":0},
           "calls":[{"name":"poly_define","args":{...},"is_error":false,
                     "result_chars":842,"ms":11}],
           "stop":"tool_use"}],
 "outcome":{"passed":true,"graders":{"G1":true,"G5":true}},
 "totals":{"calls":6,"tokens_in":31200,"tokens_out":2100,"secs":74.2}}
```

Store raw MCP frames (client↔server) too, so grading can be re-run without re-spending tokens.

---

## 6. Harness: what exists, what to build

### Exists and reusable

| Asset | Use |
|---|---|
| `Poly.Tests/Mcp/*` (25 files) | behavioural oracle: every v2 tool path must keep an equivalent test |
| `Poly.Tests/TestHelpers/McpTestTools.cs` | shows the exact singleton wiring to mirror |
| `scripts/run-probe.sh` + `scripts/probe-check` | C# artifact grader already compiles exported code with Roslyn |
| `scripts/discovery-round.sh` | the round-scaffold + baseline-sweep pattern to copy for eval rounds |
| `docs/probes/fleet-eval/*.poly` | starter fixtures (library, clinic, orders, warehouse) |
| `scripts/restart-poly-mcp.sh` | the process-spawn/kill pattern for stdio harnesses |
| TUnit + `--treenode-filter` | contract tests and replay tests |

### Must be added

1. **A black-box stdio client.** `Poly.Tests/TestHelpers/McpStdioClient.cs`: spawn the built DLL, `initialize`, `tools/list`, `tools/call`, parse `isError`, capture raw frames, kill on timeout. Add the `ModelContextProtocol` **client** package to `Poly.Tests` (currently only `Poly.Mcp` references the SDK, and only the server side).
2. **Surface contract tests** (`Poly.Tests/Mcp/SurfaceContractTests.cs`), ~10 tests, hermetic, no network:
   - exactly 7 tools, exact names, no duplicates;
   - every tool has a description that says *when to use* and *when not to*;
   - every required parameter appears in the description's example or prose (catches `polyText`/`document`);
   - unknown arguments are rejected with the valid parameter names (pin the observed silent-ignore behaviour; if the SDK cannot reject, document it as a known gap and assert the downstream error mentions the valid names);
   - `sessionId` missing → `SESSION_REQUIRED`, not a crash;
   - failed `poly_define` → `applied:false`, revision unchanged;
   - unknown enum value → error listing valid values;
   - `poly_export` never returns an empty artifact for a valid session (CORE hard no);
   - failed `poly_simulate` step → `mutationCommitted:false` and no state change;
   - `tools/list` snapshot hash matches a checked-in golden file.
3. **Headless agent driver** (`Poly.Mcp.Eval/`, a console project not referenced by the default test run): agent loop = model call → tool calls → tool results, with a hard turn cap. It must support `--arm`, `--tasks`, `--trials`, `--model`, `--replay`.
4. **Token accounting**: provider `count_tokens` for `tools/list` + instructions (exact), provider `usage` per turn, and a tokenizer for tool-result text. Persist all three.
5. **Clock injection** for T9: `POLY_NOW` (or a `Mcp:Clock` option) consumed by `TimeProvider`, so temporal assertions are deterministic. This is a production-code change; it is small, test-only in effect, and must be in the plan rather than discovered mid-eval.
6. **Report generator**: one markdown table + per-task deltas + CI + the transcript paths, written to `evals/runs/<date>/report.md`.

Repo layout:

```
evals/
  tasks/T01.json … T14.json        # prompt, allowed/required, grader spec, fixture
  fixtures/*.poly                  # canonical reference solutions
  replay/*.jsonl                   # canned tool-call sequences for grader self-test
  runs/2026-11-XX-{pilot,main,heldout}/
Poly.Mcp.Eval/                     # console: driver, graders, report
Poly.Tests/Mcp/SurfaceContractTests.cs
Poly.Tests/Mcp/SurfaceParityTests.cs
Poly.Tests/TestHelpers/McpStdioClient.cs
```

---

## 7. Migration order (risk-minimising)

Each phase has a gate; do not start the next on a failed gate.

**Phase 0 — Freeze the baseline (1h).**
Record `git rev-parse HEAD`, `git diff > evals/baseline/worktree.patch`, the full `tools/list` JSON + sha256, the instructions text + sha256, and the `dotnet build` output hash. The working tree is dirty (`Program.cs`, untracked instructions file); the baseline is *that tree*, not `HEAD`. Without this the A/B is unreproducible.

**Phase 1 — Contract tests on v1 (half day).**
Add the stdio client and the contract suite, run against v1. Expected: most pass, several fail (unknown-arg rejection, perhaps param naming). **Record the failures as baseline defects** — they are evidence for 2.0, and the same tests must pass on v2. Do not "fix" v1.

**Phase 2 — v2 behind a flag (2–3 days).**
`POLY_MCP_SURFACE=2` selects the new `Program.cs` registration. v1 code is untouched. v2 tools call the same `DomainModeling` APIs; no engine change, no DSL change, no lowering change. Keep tool bodies thin (resolve → map → call API → envelope).

**Phase 3 — Differential tests (1 day).**
For every fixture: `export(dsl)` and JSON Schema must be byte-identical across arms; scenario traces must have identical `ok`/stage/policy; C# export must compile. This is the de-risking core — it proves 2.0 is a *surface* change, not a semantic one.

**Phase 4 — Corpus + replay (1–2 days).**
Write the 14 tasks, fixtures, and graders. Run `--replay`; 14/14 required. Only then spend on models.

**Phase 5 — Pilot (1 day).**
3 tasks × 2 arms × 3 trials, one model. Iterate **descriptions only** (≤2 rounds). Any structural change restarts Phase 3.

**Phase 6 — Full A/B + held-out (1–2 days).**
12 tasks × 5 trials × 2 arms × 2 models; then 3 held-out tasks once. Produce `report.md` and read at least 10 transcripts per arm (transcript reading is where grading bugs and confusion show up).

**Phase 7 — Delete v1 (half day).**
Remove the old tool methods and registrations, remove the flag, update `Poly.Mcp/README.md` tool table, `PolyMcpInstructions.cs`, and `docs/CORE.md` §3.6 if the harness map changes. **Port tests, never delete behaviour coverage**:

| v1 test | v2 target |
|---|---|
| `McpSmokeTests`, `ValidateDslTests` | `poly_define` check/apply |
| `McpContractAddTests`, `UnifiedAddTests`, `UnifiedRemoveTests` | `poly_define` with a full document (add/remove become document edits) |
| `JsonSchemaToolTests`, `JsonLdToolTests`, `ShaclToolTests`, `HtmlToolTests`, `DomainReportToolTests` | `poly_export` / `poly_report` (same assertions, new entry point) |
| `SimulateCreateDogfoodTests`, `SubscriptionSingleFireTests`, `ManyToManyExportTests`, `InstanceRenderingTests`, `CreateInstanceCoercionTests` | `poly_simulate` |
| `OracleToolTests`, `UniqueSurvivesExportTests`, `UniqueSurvivesRejectedEvolveTests`, `CacheSessionOwnershipTests`, `McpSessionStoreTests`, `McpNameRuleTests`, `McpPackSharedInputsTests`, `McpSqliteHttpHarnessTests` | keep as-is (engine/session behaviour, surface-independent) |

**Phase 8 — Keep it honest (ongoing).**
Nightly `3 tasks × 3 trials × 1 cheap model` smoke eval; alert on pass@1 regression >0.2. Contract tests run on every PR (they are seconds). The full eval runs when a tool description, instruction text, or model changes — not on every commit.

---

## 8. Failure modes → the test that catches each

| # | Failure mode | Why it bites 2.0 | Test |
|---|---|---|---|
| 1 | **Implicit state** — a tool creates a session because none was passed | agent silently edits a phantom model; T14 class bugs | contract: every stateful tool with no `sessionId` → `SESSION_REQUIRED`; store shows no new session; isolation test across two sessions |
| 2 | **Compound tool hides errors** — `poly_define` applies a document but drops a warning; `poly_simulate` partially mutates | exactly the old `apply_dsl` success-path problem, amplified | seeded-error fixtures: `applied:false`, revision unchanged, per-element `code+path+line`; warnings returned with `applied:true` and never folded into the message only; simulate failure → `mutationCommitted:false`, re-export equals pre-run |
| 3 | **Fewer tools hurt discoverability** — the agent can't tell `inspect` from `export` | the central risk of consolidation | per-task `wrong_tool_rate`; static "capability coverage" lint: each capability keyword (schema, csharp, shacl, report, simulate, policy) appears in exactly one tool description; `poly_guide` carries a one-line tool index |
| 4 | **Silent empty artifact** — a format returns `""` for a valid model | CORE forbids an empty artifact catalog when required | contract: every format non-empty for every fixture; `$defs` count == entity+value+enum count; unknown format → error listing valid values |
| 5 | **Over-triggering** — Poly used for non-domain tasks | worse than under-triggering; pollutes transcripts | T10; acceptance: zero-tool rate ≥0.9 |
| 6 | **Parameter/schema drift** — description says "document", schema says `polyText` | observed today | contract: every required param named in the description; unknown arg rejected with valid names; `tools/list` golden hash |
| 7 | **Response blowup** — analysis/guide dumps 5–9k chars per call | the actual token problem | budget test: `inspect(analysis)` ≤2,000 chars with `total`+`truncated`; `guide` default ≤1,500 chars; eval metric `tool_result_tokens` p90 |
| 8 | **Session eviction mid-task** — 8h TTL, 15min sweep | long eval/agent sessions lose state | test with `IdleTimeout=1s`: tool returns a recoverable `SESSION_EXPIRED` with `poly_open`, not a generic not-found |
| 9 | **Description drift from docs** — README/skill says a tool does X | silent guide rot, the repo already treats guide drift as a defect class | doc test: every tool name appears in `Poly.Mcp/README.md`; the README table has exactly the registered names |
| 10 | **One tool, two intents** — `poly_export` used for human docs | agent produces HTML when a human wanted markdown review notes | `poly_report` vs `poly_export` distinction in descriptions; T5 vs T4 wrong-tool rate |

---

## 9. Acceptance criteria ("MCP 2.0 is better")

### Correctness gates (all must hold — any failure blocks the ship)

1. Contract suite 100% on v2 (and its v1 baseline defects are either fixed by design or explicitly documented as SDK-level).
2. Differential parity 100%: `export(dsl)` and JSON Schema byte-identical for every fixture; scenario traces identical.
3. Full TUnit suite green at the tested commit (`dotnet run --project Poly.Tests/Poly.Tests.csproj`).
4. No empty artifact; no failed-define revision change; no failed-simulate mutation.
5. Replay of the 14 reference solutions scores 14/14.

### Evidence gates (≥7 of 9, and #1/#2 mandatory)

| # | Criterion | Threshold |
|---|---|---|
| 1 | `pass@1` (12 scored tasks) | **≥ baseline** and **≥ 0.80** absolute |
| 2 | `pass^5` | **≥ baseline + 0.10** absolute and **≥ 0.60** |
| 3 | `wrong_tool_rate` | **≤ 5%** and **≤ baseline** |
| 4 | `first_valid_model_calls` (median) | **≤ 0.8 × baseline**; p90 **≤ baseline** |
| 5 | `recovery_success` on seeded-error tasks | **≥ 0.80**; median `recovery_cycles` ≤ 2 |
| 6 | `tool_calls_per_successful_task` (median) | **≤ 0.7 × baseline** |
| 7 | `fixed_surface_tokens` | **≤ 0.5 × baseline** (target ≤ 9k chars from 18.2k) |
| 8 | `total_tokens_per_successful_task` (median) | **≤ 0.8 × baseline**; p90 ≤ 1.0 × baseline |
| 9 | `zero_tool_rate` (T10) | **≥ 0.9** |

**No-regression clause:** no task may drop `pass@1` by more than 0.20 (one trial in five) versus baseline. A single catastrophic task blocks the ship even if the aggregate passes — that task is a description bug, and the fix is cheaper than the regression.

**Replication:** the direction of #1, #2, #3 must hold on both model families or on two independent runs of the same model. Otherwise report a null result.

### Pre-registered fallbacks (cap 2 rounds, then decide)

- Selection is the failure (`wrong_tool_rate` high, success fine): rewrite descriptions first; if unresolved, split `poly_inspect` into `poly_inspect` + `poly_analyze`, or `poly_define` into check/apply. Re-run only the affected tasks.
- Recovery is the failure: restore `poly_validate` as a separate read-only tool (8 tools).
- Success drops on document-replacement tasks: the "document is the unit" bet is wrong; add `mode:"patch"` with element-level edits, and re-run T6/T2 only.
- Tokens regress: the export inline threshold or the analysis cap is too high; adjust and re-measure (no model rerun needed for the fixed-surface metric).

### Non-goals

Do not claim "agents are less confused" from a token delta. Do not claim significance from n=12. Do not ship on vibes: if the A/B is null, 2.0 still has a defensible *maintainability* case (7 tools vs 30), but it must be argued as such, not as an eval win.

---

## 10. Risks, inferences, and gaps

**Inferences (mine, from the cited facts):**

- Because the current surface is only ~4.6k–5.5k tokens, the Anthropic 85%-token-reduction result (and the 49%→74% accuracy jump) does **not** transfer: those numbers come from 50–100k-token libraries. Poly's win must come from selection and turn count, and the acceptance thresholds above are set accordingly.
- `get_domain_analysis` at 5,280 chars on four entities will scale roughly linearly with model size, so it is the single highest-leverage response to cap.
- The `polyText`/`document` and silent-unknown-arg defects suggest the current surface has never been used by a client that reads only the schema. A stdio contract suite is the cheapest high-value addition in the whole plan.

**Gaps (could not be resolved here):**

- **G1.** No local tokenizer (`tiktoken`/`transformers` absent) and no Anthropic tokenizer; all token figures are char-based estimates. The harness must use provider `count_tokens`.
- **G2.** I did not run the TUnit suite (read-only task), so there is no baseline pass count in these notes.
- **G3.** No published study isolates *tool count* at the 10–50 range on agent accuracy; the best public evidence is Anthropic's internal large-library numbers and BFCL's selection/irrelevance categories. Treat the "30 is too many" claim as a hypothesis under test, not a finding.
- **G4.** There is no clock injection (`TimeProvider.System` is registered directly), so T9 cannot be deterministic today. This is a real prerequisite, not a detail.
- **G5.** OpenCode's exact per-turn tool-definition handling (re-sent vs cached) is not documented beyond "MCP tools add to context"; measure it in the pilot by diffing billed input tokens against the cached/uncached split.
- **G6.** The uncommitted `Program.cs` change and untracked `PolyMcpInstructions.cs` are the owner's work; I did not touch them. The baseline must include them or the A arm is not what the owner actually runs.
- **G7.** Whether `poly_define` can be made transactional on failure is an engine/API question (the current `apply_dsl` already claims "on failure nothing changes", which the plan relies on). Verify with a test before building on it.

**Sources**

- Anthropic, *Writing effective tools for agents* (Sep 2025) — eval-driven design, fewer high-impact tools, namespacing, token-efficient responses, description refinement. https://www.anthropic.com/engineering/writing-tools-for-agents
- Anthropic, *Introducing advanced tool use* (Nov 2025) — 58 tools ≈55k tokens, 134k observed, wrong selection as top failure, 85% context reduction with tool search, "less beneficial when <10 tools". https://www.anthropic.com/engineering/advanced-tool-use
- Anthropic, *Demystifying evals for AI agents* (Jan 2026) — 20–50 tasks to start, pass@k vs pass^k, deterministic graders first, grade outcomes not paths, read transcripts, balanced should/shouldn't sets. https://www.anthropic.com/research/demystifying-evals-for-ai-agents
- Yao et al., *τ-bench* (2024) — pass^k reliability. https://arxiv.org/abs/2406.12045
- BFCL V3 (Gorilla) — state-based + subset-matched response grading, irrelevance detection, step caps. https://gorilla.cs.berkeley.edu/blogs/13_bfcl_v3_multi_turn.html
- MCP spec, *Tools* (2025-06-18) — `tools/list`, `isError`, `structuredContent`, `outputSchema`, annotations. https://modelcontextprotocol.io/specification/2025-06-18/server/tools
- OpenCode, *MCP servers* — local stdio config, MCP tools added alongside built-ins, per-agent tool enablement, 30s default timeout. https://opencode.ai/docs/mcp-servers/
- Repo: `Poly.Mcp/README.md`, `Poly.Mcp/Program.cs`, `Poly.Mcp/Tools/*.cs`, `Poly.Mcp/ToolExceptionBoundary.cs`, `Poly.Mcp/Sessions/McpSessionOptions.cs`, `Poly.Tests/Mcp/*`, `Poly.Tests/TestHelpers/McpTestTools.cs`, `docs/reference/mcp-guiding-principles.md`, `docs/agent/poly-discovery-loop.md`, `scripts/run-probe.sh`, `scripts/discovery-round.sh`, `docs/probes/fleet-eval/12-mcp/mcp-library.poly`.
