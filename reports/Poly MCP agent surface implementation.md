# Poly MCP 2.0 — implementation status

**Date:** 2026-11-06 · **Branch:** `feat/ontology-agent-ux` · **Design:** `Poly MCP agent surface design.md` (this folder)

**Status:** the first implementation slice is complete. The seven-tool v2 surface is the default; v1 remains behind `POLY_MCP_SURFACE=1` until the model-driven gates run.

- [x] Seven outcome tools, one `.poly` authoring path, explicit workspace handle, one envelope.
- [x] Tool annotations, output schemas, structured content, and unknown-argument rejection.
- [x] dsl + JSON Schema byte parity with v1 on four fixtures.
- [x] Deterministic 15-task A/B, with the gate table below.
- [ ] Model-driven pass@1/pass^5, wrong-tool rate, recovery.
- [ ] Port the remaining v1 tests and delete v1 after non-inferiority.

The seven-tool surface is implemented and is the default. The legacy 30-tool surface remains behind `POLY_MCP_SURFACE=1` for the A/B the design requires before deletion.

## Shipped

| Tool | Behaviour |
|---|---|
| `poly_guide` | 715-char quickstart by default; `topic` delegates to the full DSL guide (with a v2 tool-name map at the top). |
| `poly_open` | `create` (workspace handle + state) or `list` (per-workspace state, not bare ids). |
| `poly_define` | Validate + apply one `.poly` document; `apply:false` is a dry run; a rejected document leaves revision and model unchanged and returns findings. |
| `poly_check` | `proven` / `violated` / `unknown` for the whole model, `Entity` / `Entity.Member` select, or an expression with bindings; findings capped at 20 with `findingsTotal` and `truncated`. |
| `poly_inspect` | Overview, entity, or member; brief by default, `detail:"full"` for the element. |
| `poly_export` | One `format` enum: dsl, json-schema, csharp, markdown, html, json-ld, shacl; inline under 4 KB, otherwise a random temp file with `bytes` + `sha256`. |
| `poly_simulate` | Ordered `create`/`link`/`unlink`/`invoke`/`evaluate`/`get`/`list` steps; aliases (`"as":"p1"`) resolve created instance ids; one trace entry per step, `failedAt`. |

Every response is `{ok, summary, findings[], data?, next[], state{workspace, model, revision, entities, instances}, errorCode?}`. A missing/expired handle returns `WORKSPACE_REQUIRED`; stateful `next` entries are call sketches with the real handle.

## Measured

| | v1 (`POLY_MCP_SURFACE=1`) | v2 (default) |
|---|---|---|
| Tools | 30 | 7 |
| `tools/list` JSON | 17,102 chars | 5,412 chars |
| Server instructions | none | 1,839 chars |
| Quickstart | 9,111 chars (default guide) | 715 chars |

The v2 fixed surface is ~7.3k chars (~1.8k–2.2k tokens), roughly 40% of v1’s 18.2k. The per-call win is the larger one: `poly_define` returns an 879-char envelope for a 4-entity model, and `poly_check` 2,053 chars with four hints, versus the v1 analysis envelope that repeated the same 60 findings three times.

## Deliberate deviations from the design

- **Workspace reuses `McpSessionStore`.** The handle is the existing session id (GUID); no second store. Per-process and stdio-only, so the isolation tests are still meaningful.
- **`poly_check` argument-specific proofs return `unknown`.** The analyzer’s range facts are not yet exposed as a per-argument witness; the tool says so instead of guessing. `select` and expression checks are real.
- **`poly_simulate` has no `set` step.** A mid-run write could repair data the model forbids; the design excludes it.
- **v1 is not deleted.** The design says delete only after non-inferiority; the flag keeps the comparison possible.
- **Protocol metadata is on all seven tools.** Each carries `readOnlyHint`/`destructiveHint`/`idempotentHint`/`openWorldHint`, a title, a generated `outputSchema`, and structured content. Unknown arguments are rejected by request filter with the valid names; v1 is untouched.

## Verification

- `PolySurfaceV2Tests` 24/24, including a real stdio `initialize` + `tools/list` test for the default (7) and `POLY_MCP_SURFACE=1` (30, no v2 instructions), annotations/output schemas, unknown-argument rejection, and a reflection test that the filter's accepted names match each tool's parameters.
- `SurfaceParityTests` 4/4: dsl and JSON Schema are byte-identical to the v1 tools on four fixtures.
- Full suite 5048/5048.
- Live stdio sequence (`create_domain_session` → `poly_define` → `poly_check` → `poly_export` → `poly_simulate`) passes.

## Deterministic A/B (`scripts/mcp-surface-eval.py`)

Fifteen reference scenarios, each graded by code against both surfaces over real stdio. This is the replay gate, not a model-driven pass@k: it measures surface mechanics, not tool selection.

| | v1 | v2 | ratio |
|---|---|---|---|
| Scenarios passed | 15/15 | 15/15 | — |
| Tool calls | 48 | 47 | 0.98× |
| Text-response characters | 43,732 | 30,960 | 0.71× |
| Structured-content characters | 0 | 30,344 | — |
| Combined payload (text + structured) | 43,732 | 61,304 | 1.40× |
| Fixed surface (`tools/list` + instructions) | 17,102 | 8,996 | 0.53× |

After the shrink: the text gate passes (0.71×), the fixed surface is under the report's 9k-char cap (8,996), and scenario success is parity. The calls gate still misses (0.98× vs 0.7×) because the scripted reference transcripts use the optimal v1 sequence for each task; only a model-driven run can show selection savings. Structured content still duplicates the text payload (combined 1.40×); clients that surface both pay it. The largest per-task win is the ten-entity check: 2,117 text chars vs v1's 18,698. **v1 stays until the model-driven gates (pass@1/pass^5, wrong-tool, recovery) run and calls improve; the deterministic gates alone do not justify deletion.**

## Model-driven pilot

The first `opencode run` against the live v2 server found a real protocol bug: the compact output schema marked `errorCode` required, but the serializer omits nulls, so the client rejected every v2 call and the model fell back to a raw client. `errorCode` is now optional (the schema requires only `ok` and `summary`); a second run called `poly-local.poly_guide` and `poly-local.poly_open` directly. The full pass@k corpus is not run yet — it needs the MCP client connected and a token budget for 15 tasks × 2 surfaces × trials.

## Next

1. Run the model-driven A/B (pass@1/pass^5, wrong-tool rate, recovery) over the same corpus; the scripted replay is the gate before spending tokens.
2. Port the remaining v1 tests, then delete v1 (and its `add`/`remove` path) after non-inferiority.
3. Expose the analyzer's range/precondition facts as a real `poly_check` witness; shrink the remaining envelope overhead on small models.
