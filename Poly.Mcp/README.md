# Poly.Mcp — MCP Server for Poly Domain Modeling

**Role:** the agent harness for Poly. It holds a `DomainSession`, lets an agent describe a domain as one `.poly` document, proves it with the constraint solver, and produces an artifact or a simulation. It is not a product entry-point extension (REST is `uses http`) and not a second evaluator.

Lock: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md). Frozen core: [`docs/CORE.md`](../docs/CORE.md) §0. Mechanisms: [`docs/CORE.md`](../docs/CORE.md) §3.6.

Launch: `polyc mcp` (stdio). `dotnet run --project Poly.Mcp` runs the same host entry for one release.

## Tool Surface

Seven outcome tools. Server instructions carry the framing: use Poly when a task has a domain, write it down, prove it, then export or simulate.

| Tool | Job |
|------|-----|
| `poly_guide` | Learn the `.poly` surface: quickstart, workflow, unsupported constructs, section index |
| `poly_workspace` | Create, list, or close a workspace; the handle every stateful call needs |
| `poly_define` | Validate and apply one `.poly` document; `apply:false` is a dry run; a rejected document changes nothing |
| `poly_check` | Prove the model, one selected action/property, or an expression: `proven`, `violated`, or `unknown` |
| `poly_inspect` | Read the overview, one entity, or `Entity.Member`; brief by default, `detail:"full"` for the element |
| `poly_export` | One `format` enum: `dsl`, `json-schema`, `csharp`, `markdown`, `html`, `json-ld`, `shacl`; inline under 4 KB, otherwise a file path |
| `poly_simulate` | Ordered `create`/`link`/`unlink`/`invoke`/`evaluate`/`get`/`list` steps; one trace entry per step |

Every response is `{ok, summary, findings[], data?, next[], state{workspace, model, revision, entities, instances}, errorCode?}` in `structuredContent`. The text block is not a copy of that JSON: it carries the essentials as fixed prose lines (`ok — summary`, `workspace: <handle> model=… rev=…`, one `finding:` line each, `next:` call sketches) plus `data` flattened to `key: value` lines (guide body, export body, one line per simulate step, a witness count). This deliberately departs from the MCP SHOULD that a text block carry the serialized JSON; clients that read only text lose nothing they need to continue, except that `poly_check`'s witness is a count line in the text and full only in `structuredContent`; a dry-run edit's spliced document is in both. `structuredContent` keeps the full envelope. An inline export therefore doubles its bytes by design. A missing or expired handle returns `WORKSPACE_REQUIRED`; `next` entries are call sketches with the real handle. Arguments are validated against the tool's published `inputSchema` before the binder: an unknown name is `UNKNOWN_ARGUMENT` with the valid names, and a missing, wrong-typed or over-limit value is `INVALID_ARGUMENT` naming the argument and the expected type.

The legacy 30-tool v1 surface was removed. Its operation adapters remain internal to `PolySurfaceTools` (they are not registered as MCP tools). The historical A/B is the `Poly MCP agent surface implementation.md` note in `../reports/`.

## Workspaces

`poly_workspace` creates a process-local workspace (a GUID handle). Keep one per task. `poly_define` replaces the model, clears runtime instances, and bumps the revision. `poly_workspace(op:"list")` returns the current workspaces with model/revision/counts; `poly_workspace(op:"close", workspace:…)` removes the workspace and its instances when the task is done. Sessions are scratch; `poly_export(format:"dsl")` is how a model is kept.

## Authoring

One path: write the whole domain as one `.poly` document and call `poly_define`. Constraints and relationships are part of that document; there is no incremental element API. A rejected document changes nothing and returns findings with severity, code, element path, and message.

`poly_guide` returns a ~700-char quickstart by default and a section of [`Docs/poly-dsl-guide.md`](Docs/poly-dsl-guide.md) on request. The guide is the product-true syntax reference.

## Interop Artifacts

Read-only exports hand the analyzed domain to other toolchains without re-deriving it:

- **`export_domain_to_csharp`** is folded into `poly_export(format:"csharp")` — the lowered C# model.
- **JSON Schema** (`format:"json-schema"`) — `$defs` per entity/value type/enum, with range, length, pattern, equality, default, and enum constraints. A property is `required` unless it is `optional`, has a `default`, or is initialized by the first stage's entry effect. Term metadata projects to `title`/`description`/`x-poly-iri`; enum members to `x-poly-enum`. The IRI is deliberately not a subschema `$id` — that would rebase local `#/$defs` refs.
- **JSON-LD + RDFS** (`format:"json-ld"`) — an `@context` plus an RDFS `@graph`: `rdfs:Class` per type, `rdf:Property` with `rdfs:domain`/`rdfs:range`, enum members typed by their class. Declared IRIs win; untagged elements get stable `urn:poly:` IRIs.
- **SHACL** (`format:"shacl"`) — `sh:NodeShape` per entity/value type with `sh:targetClass`, `sh:closed`, and `sh:PropertyShape` per property/navigation. Range/length/pattern map to SHACL keywords; `equals` is a one-value `sh:in` list; `unique`/defaults are context-defined `x-poly-*`.
- **Markdown** (`format:"markdown"`) and **HTML** (`format:"html"`) — the same facts for a human.

All are pure projections over `Domain` under `Poly/DomainModeling/Queries/`.

## Term Metadata

`iri`, `label`, `description`, and (on enum members) `term` are **core** annotations, not a vendor pack. They attach to entity/enum headers, property tails, and enum members, round-trip through `poly_export(format:"dsl")`, and are read in code through `TermFacets.Read(...)` / `TermMetadata` (`Poly/DomainModeling/Ontology/TermMetadata.cs`). The parser accepts exactly one non-empty string argument — `iri` must be an absolute `http(s)` or `urn` IRI — and fails closed on anything else.

```poly
Color: enum iri("https://example.com/Color") {
  Red iri("https://example.com/Color/Red") term("red"),
  Green,
}
```

## Simulation

`poly_simulate` takes steps as a JSON array:

```json
[
  {"op":"create","entity":"Product","properties":{"Total":42},"as":"p"},
  {"op":"invoke","instanceId":"p","action":"Submit"},
  {"op":"get","instanceId":"p"}
]
```

`as` aliases a created instance for later steps. Each step is atomic; a guard-blocked invoke is a failed step, not a failed call, and earlier steps stay committed. `reset:true` starts from a clean instance state. No mid-run property writes: a scenario cannot repair data the model forbids.

## Honesty

- `poly_define` writes the model; a rejected document changes nothing.
- `poly_check` with `select` naming an action also returns `data.witness` — the analyzer's per-stage preconditions, narrowed ranges and postconditions (omitted when none were published; the verdict is unchanged).
- `poly_check` reports `proven` only when it has no error findings; `unknown` when the request is not modelled (argument-specific proofs) or no selector matches.
- `poly_export` fails closed when an exporter returns no artifact.
- `poly_workspace`/`poly_inspect`/`poly_check`/`poly_export` are read-only (export may write a temp file); `poly_define`/`poly_simulate` are destructive.
- Tool annotations, output schemas, and structured content are on every tool (structured = the full envelope; text = essentials plus flattened data); unknown arguments fail with the valid names.

## Harness Runtime

`Program.cs` composes the server through the host's DI container:

- **Store:** `ISessionStore` is a singleton (`McpSessionStore`). Sessions are in memory only; a restart loses them. Each session has its own lock.
- **Eviction:** sessions idle longer than `Mcp:Sessions:IdleTimeout` (default 8h) are removed by `SessionEvictionService`; `SweepInterval` (default 15m) controls the sweep. Set `IdleTimeout` to zero to disable eviction.
- **Boundary:** one `AddCallToolFilter` validates the raw argument bag against the tool's published `inputSchema` (`UNKNOWN_ARGUMENT`, `INVALID_ARGUMENT`); another catches uncaught exceptions (as the advertised envelope: a binder failure is `INVALID_ARGUMENT`, every other exception `INTERNAL_ERROR`); the innermost, `EnvelopeTextFilter`, replaces the text block with the prose projection of the structured envelope. All keep the `PolyResponse` envelope.
- **Transport:** stdio; session identity is the workspace handle, not an HTTP header.

See the `Poly MCP agent surface design.md` note in `../reports/` for the rationale and the gate results.
