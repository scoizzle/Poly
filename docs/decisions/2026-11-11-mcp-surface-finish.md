# ADR: MCP surface finish — schema-derived argument validation and one typed error boundary (#677 M-01)

## Status

**Accepted** — slice M-01 of epic [#677](https://github.com/scoizzle/Poly/issues/677), theme M
([#689](https://github.com/scoizzle/Poly/issues/689)), plan lock 2026-11-12
([comment 6818596017](https://github.com/scoizzle/Poly/issues/689#issuecomment-6818596017),
`LOCK-WITH-CHANGES`, `implementer: deepseek`). Closes [#533](https://github.com/scoizzle/Poly/issues/533);
part of [#488](https://github.com/scoizzle/Poly/issues/488) (array `steps` is M-07's).

## Context

Q15 rules that a caller mistake on the MCP surface is a typed answer, not a defect report. Two
gaps produced the wrong answer:

- **#533: the binder threw and the boundary called it internal.** A missing required argument or a
  wrong JSON type reaches the SDK binder, which throws `ArgumentException(…, "arguments")` or
  `JsonException`. `ToolExceptionBoundary` mapped every exception to `INTERNAL_ERROR`, so an agent
  was told the server had a bug when the call was malformed.
- **#488: `steps` as a JSON array.** `poly_simulate.steps` is declared `string`; the binder throws
  `JsonException` and the caller sees `INTERNAL_ERROR`. The repair is a typed
  `INVALID_ARGUMENT` until M-07 widens the parameter to array-or-string.
- **The hand-maintained allow-list drifted.** `UnknownArgumentFilter` held a literal
  `tool → argument names` table; it had to be updated by hand for every tool change and could not
  see types or required-ness.

The SDK already publishes the truth agents see in `tools/list`: every tool's `inputSchema` names
its properties, their JSON types, and the required set. The SDK also already applies the exclusion
rules (service parameters, `CancellationToken`) when it builds that schema.

## Decision

### 1. One validator, derived from the published `inputSchema` (F8)

`ArgumentValidationFilter` reads each tool's spec from `context.MatchedPrimitive` as
`McpServerTool.ProtocolTool.InputSchema`, the same schema `tools/list` advertises. The spec is
cached per tool name for the process. A schema that cannot be mapped **throws**, and
`ArgumentSpecCatalog.SelfCheck` runs the map for every registered tool before the host starts
serving, so an unmapped tool is a startup failure, never a silent pass-through. The hand-built
reflection table, its runtime skip, and its service-parameter exclusion list are gone: the SDK is
the source, and the filter cannot drift from what an agent was told.

### 2. Validation runs before the binder, in the old allow-list slot

The filter sits where `UnknownArgumentFilter` sat (inside `ToolExceptionBoundary`, outside
`EnvelopeTextFilter`; the text projection is unchanged by position). Precedence, pinned by test:

1. an unknown name → `UNKNOWN_ARGUMENT`, with the valid names;
2. a registered limit exceeded → `INVALID_ARGUMENT`, naming the argument, the limit and the size;
3. another missing required argument → `INVALID_ARGUMENT`;
4. a wrong JSON type → `INVALID_ARGUMENT`, naming the argument, the expected type and the actual
   kind. A JSON `null` is invalid unless the schema's type includes `null`; the required
   `workspace` is the one exception handed to the tool body;
5. only when **every other argument is well-formed**, a missing required `workspace` is a host
   status: the filter inserts `""` and lets the tool body's `TryWorkspace` produce the one
   `WORKSPACE_REQUIRED` envelope (no second producer, no wording drift). **A missing workspace is
   reported only when every other argument is well-formed** — a defect in another argument is
   reported first, so the missing handle never masks it. Blank (`""`) and explicit `null` already
   reach `TryWorkspace` on master and are characterised as `WORKSPACE_REQUIRED`; a non-null wrong
   type is `INVALID_ARGUMENT`.

Within one category the first defect in parameter order is reported.

### 3. Five codes, one boundary

| Code | Meaning | Producer |
|------|---------|----------|
| `UNKNOWN_ARGUMENT` | The tool does not declare that name. Text is byte-identical to the old filter (`Valid: topic`). | Pre-binder filter |
| `INVALID_ARGUMENT` | A declared name with a missing, wrong-typed, or over-limit value. | Pre-binder filter; the boundary's binder classifier |
| `WORKSPACE_REQUIRED` | No usable workspace handle. A host status, not an argument error. | `PolySurfaceTools.TryWorkspace` |
| `ELEMENT_NOT_FOUND` | `poly_inspect` select names no single element: `data.reason` is `unknown`, `ambiguous` or `notAddressable`, with `data.candidates`. | `InspectService` (tool body, like `WORKSPACE_REQUIRED`) |
| `INTERNAL_ERROR` | A defect. The call was well-formed and the server failed. | `ToolExceptionBoundary` |

`UNKNOWN_ARGUMENT` is not folded into `INVALID_ARGUMENT`: the wire, the README and the existing
stdio test all name it, and folding it would be an unruled relabel.

### 4. The binder classifier matches exactly two SDK shapes

`ToolExceptionBoundary.Failure(Exception)` keeps its signature. It maps `JsonException` and
`ArgumentException { ParamName: "arguments" }` — the two shapes the SDK binder throws — to
`INVALID_ARGUMENT`, and everything else to `INTERNAL_ERROR`. `Handle(ex, tool, logger)` logs a
binder failure at `Warning` naming the tool and "after validation"; a defect stays at `Error`.

**Residual classification risk.** The classifier is shape-based, not provenance-based: a tool
defect that throws an `ArgumentException` with `ParamName == "arguments"` would be relabelled
`INVALID_ARGUMENT`. There are bare `new ArgumentException(message)` sites in `Poly/` and
`Poly.Mcp/`; they are real defects and stay `INTERNAL_ERROR` because they carry no parameter name.
The pre-binder filter makes the binder path unreachable for every caller error the published
schema describes, so this path is a hole only if a future validator hole lets a malformed value
through; the warning line names the tool, which is the evidence to fix the hole.

**The `JsonException` half.** The other matched shape is the SDK's deserialisation failure. A
`JsonException` thrown by a tool body — `poly_simulate` parsing `steps`, for example — is caught
inside the tool and returned as `ok:false` with no `errorCode`, so it never reaches the boundary. If
a future tool body lets a `JsonException` escape, it would be relabelled `INVALID_ARGUMENT`; the
same warning line names the tool.

**Declined guard.** An optional extra guard — require that no stack frame belongs to a `Poly*`
assembly before calling an exception a binder failure — was considered and declined. It couples the
boundary to assembly names and would still misclassify a binder failure that passes through a
`Poly*` frame. The pre-binder filter is the real guard.

**Messages are prose, not contract.** The two `INVALID_ARGUMENT` producers emit different summary
texts — `Argument 'x' for tool must be a JSON string, not a number.` from the validator and
`Invalid argument: <binder message>` from the classifier. Only `errorCode` is the contract; the
summary is for a human or an agent reading it, and a client must not parse it.

### 5. Limits are one empty registry

`ArgumentLimits.Registry` maps `(tool, argument)` to `ArgumentLimit(int? MaxChars, int? MaxItems)`.
It is **empty today**: the slice that owns a number (G-09's `DslLimits.MaxDocumentChars`) adds its
entry, and the entry is visible in a diff. The check is pure and takes the map as a parameter, so a
synthetic map proves enforcement without a mutable static. `MaxItems` is measured on the raw
`JsonElement` array and is inert until an array-typed argument exists (M-07).

### 6. What is deleted

`Poly.Mcp/Surface/UnknownArgumentFilter.cs` is gone; `SurfaceRegistrationTests` asserts no type by
that name remains. `PolySurfaceV2Tests.FilterAllowedArguments_MatchTheToolParameters` is replaced by
`ToolSignatures_HaveAJsonKindMappingForEveryParameter`, the same invariant over the schema-derived
spec. No test is deleted or weakened.

## Consequences

- **The schema is the contract.** A tool change that renames or retypes a parameter changes the
  published schema and the validation together; there is nothing to update by hand.
- **A new CLR parameter type needs a mapping.** `ArgumentSpecCatalog` maps the JSON type names the
  SDK emits (`string`, `boolean`, `number`/`integer`, `object`, `array`, `null`). A schema shape
  outside that set fails the startup self-check; the slice adding it extends the map.
- **`steps`-as-array is pinned invalid.** `McpProtocolTests` names the assertion for M-07, so the
  flip is a deliberate edit, not a silent fossil.
- **No producer for a limit.** The registry is the seam, not the cap; M-01 claims the registry only.

## Evidence

`McpProtocolTests` (one stdio host) records the master reds — every caller error returned
`INTERNAL_ERROR` with the binder's real exception type — and the green typed answers, including
`MaxItems` on a raw array and the four-code boundary. `PolycMcpTests` adds one stdio test: a wrong
type is `INVALID_ARGUMENT` and the same host still serves the next call.

### M-03 note: one path grammar, one resolver
`poly_inspect` parses `ElementPath` and resolves it once through `ElementPathResolver` (`Poly/DomainModeling/Queries/`). The resolver is pure over the domain and returns a ref, a not-found (`unknown` / `notAddressable`, with candidates) or an ambiguity. `poly_check`'s select helpers and `VerificationSummary.SelectScope` are superseded copies that M-04/M-05 repoint.

**Contract change (M-03).** `poly_inspect` `data.kind` is now `navigation`, `entityAction` or `stageAction` (`relationship` and `action` are gone), alongside `entity`, `property`, `enum`, `enumMember`, `valueType`, `policy`, `stage`, `function`. Bare stage, action, policy and relationship names with no entity no longer resolve; write `Entity.Name`. Elements carry `data.path`; an `ELEMENT_NOT_FOUND` carries `data.reason`, `data.candidates`, and when present `data.partial` (deepest resolved path) and `data.matches` (`{kind, path}` per ambiguous match). `InspectService` still takes `QueryTool` for the overview and entity data until M-13 deletes the v1 adapters.

**Same-named entity and stage action (owner ruling 2026-11-12).** The bare `Entity.Action` is `ELEMENT_NOT_FOUND` with `data.reason` `ambiguous` and both matches listed, never a silent entity-level win. The stage action is `Entity.Stage.Action`. The entity-level action has no unambiguous two-segment form, so a leading domain name makes the path the exact analysis-path form (`Domain/Entity/Action` or `Domain.Entity.Action`): the bare stage-action shorthand and the collision are skipped, and every element's `AnalysisPath` resolves back to itself.
