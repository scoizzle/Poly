# CLI Domain Interaction — Design Thinking (via transport discovery)

**Date:** 2026-08-09
**Status:** Thinking / design sketch — not a committed plan
**Companion:** [`rest-api-experiment.md`](rest-api-experiment.md)

## The idea

The REST experiment proved a transport surface can be **derived from the domain
model instead of hand-written**: entities → CRUD routes, relationships → child
routes, actions → action routes, stages/policies → `DomainResult` mapped to HTTP
409s. Call that **transport discovery**: walk the domain model, emit the
transport shape, map domain outcomes to transport outcomes honestly.

Question: can the same discovery drive a **command-line interface** for
interacting with domain models at runtime — the human twin of the MCP runtime
tools?

Short answer: yes, and it is the *same* idea with a different emitter. But the
insight worth capturing is that **discovery is transport-independent**; REST and
CLI are just two projections of one operation catalog.

## What "transport discovery" already means here

From `demo/Poly.RestApi` + `src/Poly.DslCompiler/MinimalApiGenerator.cs`:

| Domain fact | REST projection |
|---|---|
| Entity + key | `GET /api/books`, `GET /api/books/{isbn}`, `POST /api/books` |
| Relationship (N1 nav) | `GET /api/patrons/{email}/loans` |
| Action (with params) | `POST /api/patrons/{email}/checkout` |
| Stage / policy guards | `DomainResult` failure → HTTP 409 + message |
| Not found | → 404 |

The discovery is the **operation catalog** (entity → key args, action → params,
guard → failure mode). The emitter turns it into routes. That catalog is what a
CLI wants too.

## CLI as a second emitter

A command tree is the CLI's route table:

| Domain fact | CLI projection |
|---|---|
| Entity + key | `instance create <Entity> --json {...}` · `instance list [Entity]` · `instance get <Entity> --key v` |
| Action (with params) | `action invoke <Entity>.<Action> --key v --param x` |
| Policy | `policy evaluate <Entity>.<Name> [--instance id]` |
| Stage | `stage list <Entity>` (implicit in `action invoke` semantics) |
| Relationship | `link` / `unlink` (defer — see slice below) |

This is literally `RuntimeTool`'s surface (`create_instance`, `invoke_action`,
`evaluate_policy`, `list_instances`, `get_instance`, `link_instances`) — which
already wraps core `Poly/DomainModeling/Runtime/` (`DomainEntityInstance`,
`DomainInstanceStore`). A CLI is a **thin third wrapper over the same direct
API**.

## Codegen-time vs runtime CLI

- **(a) Codegen-time**: `DslCompiler` emits a command tree alongside the REST
  routes. Static, discoverable at build, but requires regeneration and can't
  operate a live store.
- **(b) Runtime**: `poly run --domain library.poly <command>` loads the model,
  analyzes it, builds the command tree **at runtime**, and drives
  `DomainInstanceStore` directly — same as MCP sessions, zero codegen.

**Recommendation: (b).** The machinery exists in core; the CLI then proves the
direct API end-to-end (the REST demo proved it at the EF boundary; this proves
it at a process boundary). (a) is a later nicety.

## The CLI's "HTTP codes"

Transport discovery maps domain outcomes to transport outcomes. For a CLI:

| Domain outcome | CLI outcome |
|---|---|
| Success | exit 0, data on stdout |
| Guard / validation failure | exit 1, message on **stderr** (fail loud, no vacuous success) |
| Usage error | exit 2, usage on stderr |
| Not found / missing instance | exit 3 (or fold into 1 — keep the v1 set small: 0/1/2/3) |
| `--json` flag | machine-readable stdout (jq-friendly); default = human table |

No "vacuous success": a policy that evaluates false exits non-zero.

## What this is NOT

- **Not the DSL `domain Name: cli` kind.** That's a *lowering* concept — the
  generated artifact *is* a CLI app with the domain's own business logic (e.g.
  `grep`). This doc is about a *generic* domain-interaction harness, like MCP
  but for humans. Keep the two concepts named apart.
- **Not an authoring CLI.** `entity add`, `apply_dsl`, etc. stay MCP-owned
  (agents author; `add`/`remove`/`apply_dsl` already cover it).
- **Not the validation CLI** from
  `docs/plans/anti-pattern-007-single-point-dependency.md` (load → analyze →
  diagnostics summary). Related and worth building, but a different slice.
  The DslCompiler CLI is also already a thing (`.poly → C#`); this is distinct
  from codegen.

## Why this matters (beyond "nice UX")

1. **Breaks the MCP single-point dependency** (anti-pattern-007). MCP is
   currently the *only* shipping consumer of the runtime direct API. A CLI is an
   independent signal that changes are safe — same argument the REST experiment
   made for the EF boundary.
2. **Dogfood seam.** The MCP wave-2 dogfood already found runtime pain
   (append-only graph, invisible children, link/unlink). A CLI surfaces the same
   class of issues in a second harness for cheap.
3. **Discovery stays in one place.** The operation catalog should be shared, not
   re-walked per transport. Today there are effectively three hand-rolled
   walkers (MinimalApiGenerator, RuntimeTool, and a future CLI would be a third).
   Per the "abstraction after a second consumer" principle: build the CLI v1 by
   walking the domain directly (cheap, mirrors RuntimeTool), and extract a
   shared catalog when a second *human* transport (e.g. runtime-reflective REST
   hosting) actually appears.

## Minimal vertical slice (v1)

- `poly run --domain <file.poly> <command>` (runtime path, `--json` output)
- Commands: `instance create|list|get`, `action invoke`, `policy evaluate`
- Arg parsing: action params as flags (`--bookId x`), initial props as `--json`
- Exit-code contract above (0/1/2/3), all failures on stderr
- TUnit tests on the direct API (same shape as `DomainInstanceStoreFailClosedTests`)
  + a couple of process-level tests
- **Not in v1**: REPL, sessions/persistence, authoring commands, completions,
  `link`/`unlink` (MCP S2 proved that's a pain magnet — defer until the store
  story is fixed)

## Open questions

- Where does it live? A new host project (`Poly.Cli/` or `src/Poly.Cli`) —
  placement should follow the MCP-host pattern (thin host over `Poly` core).
- Does `--domain` take a `.poly` file or a session workspace? File-first
  (stateless, one-shot) matches the CLI's scriptability.
- Should instance state persist (SQLite like the demo) or is in-memory
  process-lifetime fine for v1? In-memory for v1; persistence is the REST/EF
  domain's job.
- Do action params map by name or position? Name-only (`--param`) — matches
  `add`'s payload conventions and avoids positional mistakes.

## Related

- `docs/experiments/rest-api-experiment.md` — the REST discovery proof
- `docs/plans/anti-pattern-007-single-point-dependency.md` — why a non-MCP consumer exists
- `Poly.Mcp/Tools/RuntimeTool.cs` — the surface a CLI mirrors
- `Poly/DomainModeling/Runtime/` — the direct API a CLI drives
