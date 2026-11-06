---
name: poly
description: Use Poly to describe the domain you are working in, prove it with the constraint solver, and produce the tools or artifacts the task needs. Use when a task involves domain rules, state, lifecycle, validation, contradictions, a schema, an API contract, or a simulation of the domain. The default MCP surface is seven outcome tools (`poly_guide`, `poly_open`, `poly_define`, `poly_check`, `poly_inspect`, `poly_export`, `poly_simulate`).
---

# Using Poly

Poly models a domain as an executable `.poly` document: entities, typed properties with constraints, stages, actions, policies, and navigations. The analysis gate proves the document is internally consistent; the same model then produces schemas, C#, or a simulation.

Use Poly when the task has a domain you would otherwise hard-code assumptions about. Describe the domain, prove it, then build from that one model.

## The loop

1. `poly_guide` — the quickstart and the section index. Call it before authoring.
2. `poly_open` — create the workspace handle (`op: "create"`, optional `name`). Keep one per task.
3. `poly_define` — write the whole domain as one `.poly` document. It validates and applies; `apply: false` is a dry run. A rejected document changes nothing and returns findings with `severity`, `code`, `path`, and `message`.
4. `poly_check` — `proven`, `violated`, or `unknown` for the whole model, one selected action/property, or an expression with bindings. Fix the model, not the constraint.
5. `poly_export` — `dsl`, `json-schema`, `csharp`, `markdown`, `html`, `json-ld`, or `shacl`. Small artifacts return inline; over 4 KB returns `{path, bytes, sha256}`.
   Or `poly_simulate` — an ordered `create`/`link`/`unlink`/`invoke`/`evaluate`/`get`/`list` scenario with one trace entry per step.

`poly_inspect` reads back the overview, one entity, or `Entity.Member` (action/stage/policy). It is brief by default; `detail: "full"` returns the element.

## Authoring rules

- **Author only shipped surface.** The guide's "Supported Effect Summary" lists what runs; "Do NOT Use" is fail-closed.
- **Round-trip safe.** Anything you author must survive `poly_define` → `poly_export(format: "dsl")`.
- **Relationships** are inline navigations (`orders: many Order`); the shipped `.poly` surface is N1-only.
- **Guards** use `require PolicyName`; cross-entity side effects use stage subscriptions (`when Rel Stage as name { … }`), not action coupling.
- **Properties are required unless `optional`.** `default(...)` and a first-stage `entry { assign Field to value }` also make a property omittable at create.
- **Term metadata** is core: `iri("https://…")`, `label("…")`, `description("…")` on an entity/enum header or property tail, and `iri`/`term` on an enum member.
- **Fail closed.** Never hand-edit generated artifacts to get past a diagnostic — fix the `.poly`.

## Workspace rules

- One workspace per task; pass the handle from `poly_open` to every stateful call.
- `poly_define` replaces the model; it does not merge. It clears runtime instances and bumps the revision.
- The state block in every response carries `workspace`, `model`, `revision`, `entities`, `instances`. An unknown/expired handle returns `WORKSPACE_REQUIRED`; call `poly_open`, do not guess.
- Sessions are process-local scratch. `poly_export(format: "dsl")` is how you keep a model.

## When not to use Poly

No domain, no rules, no state: use ordinary code or the task's native tools. Poly is not a database, a query language, or a general code runner.

## Reference

- In-repo guide: `Poly.Mcp/Docs/poly-dsl-guide.md` (`poly_guide` reads the same text).
- Design and implementation status: `reports/`.
