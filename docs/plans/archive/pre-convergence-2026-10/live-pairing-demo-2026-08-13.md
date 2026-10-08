# Live pairing demo — human guides agent → running API

**Date:** 2026-08-13  
**Parent:** [`live-demo-reliability-2026-08-13.md`](live-demo-reliability-2026-08-13.md)  
**Bar:** A human and an MCP agent build a domain in conversation, then a browser hits a generated API. Repeatable in one sitting.

## The product is one loop. We have two.

```text
TODAY
  A. MCP session     add / apply_dsl / create_instance / invoke_action
                     (in-memory store — no HTTP)
  B. DslCompiler     .poly file → Program.cs → Kestrel
                     (no session)

THE DEMO
  Human talk → agent updates one .poly → apply_dsl (analysis) → optionally
  invoke_action in-session → “go live” → CompileMode.All → dotnet run → curl
```

`export_domain_to_csharp` is **entities only**. It does not produce `Program.cs`. Incremental `add` creates **empty** actions (no effects). Effects and subscriptions require `apply_dsl`, which **replaces** the session domain and **clears instances**.

## Protocol (do this, not 40 `add` calls)

**Source of truth is a file**, not `export_dsl` (printer is not inverse of parser yet — `not (…)`, mixed `require`, create-in commas).

```text
demo/live/domain.poly     ← the document the human can watch
MCP session               ← apply_dsl that file after every accepted edit
```

### Agent rules (read this before the session)

1. `get_dsl_guide` first. No DateOperation authoring (`Now` / `N days` is p1). `value { }` and `contract`/`bind` are shipped.
2. After every change: write the **full** `.poly`, then `apply_dsl`. Never patch via `add` for behavior.
3. Then `get_domain_analysis`. Errors → fix the file. Do not continue dirty.
4. Do not put entity-level `any`/`all`/`none`/`count` policies on the demo domain (they become throwing action guards in C#). Local property policies are fine.
5. Prove meaning in-session when useful: `create_instance` → `invoke_action` → `get_instance`. That is **not** the HTTP API. Say so to the human.
6. When the human says go live: `scripts/serve-poly.sh demo/live/domain.poly` (or pass `--walk` only for the warehouse fixture). Return the URL. Do not dump entity C# from `export_domain_to_csharp` and call it the API.

### Human rules

- One domain, one sitting. Library / warehouse / orders-shaped. Not “model the enterprise.”
- Guide in capabilities: “patron can check out a book,” not “add a ValueType.”
- After go-live, click or curl. If 400, the body is wrong — generated `demo.http` samples still ignore `pattern`/`range`. Use values that satisfy the constraints you just authored.

### Demo-safe subset (compiles today)

Entities, enums, properties, required/unique/range/length/pattern, stages, assign/transition/create/create-in/invoke, one/many/owned navs, local policies, subscriptions (watch notify-create).  
**Avoid:** entity-level collection quantifier policies, `Now`/units, `delete` effect, printer round-trip as input.

## What we will not pretend

| Claim | Truth |
|-------|--------|
| Incremental `add` is how the agent builds a live app | It builds a skeleton. Behavior is `apply_dsl`. |
| MCP runtime **is** the generated API | Two hosts. Session store vs EF + Minimal API. |
| `export_dsl` → `apply_dsl` is safe | Not until printer slice P. |
| Seed/`demo.http` just works | Samples still violate patterns. |

## Missing product (next, when this protocol hurts)

One MCP verb, e.g. `compile_session` → write `CompileMode.All` + csproj to a session dir, return `dotnet run` / URL. Do **not** add it by making `Poly.Mcp` swallow `Poly.DslCompiler` casually — generation stays the compiler; MCP should call that API, not re-export entities. Until that verb exists, the script **is** the go-live tool.

Printer P + seed/http (e2e-4-7) are what make the pairing loop stop needing workarounds.
