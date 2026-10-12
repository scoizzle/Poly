---
name: poly
description: Author, validate, and run Poly domain models (ontologies) through the Poly MCP server (`poly-local`). Use when a task involves Poly, the `.poly` DSL, domain or ontology modeling, entities, stages, actions, policies, subscriptions, or the Poly MCP tools (`create_domain_session`, `apply_dsl`, `get_domain_analysis`, `export_dsl`, `export_domain_to_csharp`, `create_instance`, `invoke_action`, `evaluate_policy`). Read this before hand-writing a `.poly` file or guessing DSL syntax.
---

# Using Poly

Poly models a domain as a library of legal operations: author `.poly`, analyze fail-closed, run it on the interpreter, and emit lowered C#. The Poly MCP server (`poly-local`) is the tool surface. Its DSL guide is the source of truth — this skill is the workflow, not a syntax reference.

## Golden workflow

1. `get_dsl_guide` before authoring. Pass a `section` number for depth (0 principles … 13 example) or `all`.
2. `create_domain_session` (domain name) → returns a `sessionId`.
3. Write the whole domain as one `.poly` document and `apply_dsl` in one shot (batch). `apply_dsl` **replaces** the session domain; it does not merge.
4. `get_domain_analysis` → require **0 errors** before continuing. Read the diagnostics, fix the `.poly`, re-apply. `get_domain_suggestions` adds advisory gaps (not errors).
5. `export_dsl` to confirm round-trip; `export_domain_to_csharp` when you need the lowered C#.
6. Run it: `create_instance` → `evaluate_policy` / `invoke_action`; wire existing instances with `link_instances` / `unlink_instances`; inspect with `get_instance` / `list_instances`.

## Authoring rules

- **Author only shipped surface.** The guide's "Supported Effect Summary" (§9) lists what runs; "Do NOT Use" (§10) is fail-closed (`actor`, `function`, `schedule`, top-level `relationship … from … to`, event/publish/subscribe).
- **Round-trip safe.** Anything you author must survive `apply_dsl` → `export_dsl`. The §13 example is the reference shape.
- **Relationships:** navigation properties live inside entity blocks (`orders: many Order`); the `.poly` surface is N1-only. The incremental `add relationship` tool accepts `OneToOne` / `OneToMany` / `ManyToMany` / `ManyToOne` when you need other cardinalities.
- **Guards:** use `require` gates, not nested `if`. Cross-entity side effects use stage subscriptions (`when Rel Stage as name { … }`), not action coupling.
- **Linking existing instances** is a tool/store operation (`link_instances`), not an effect; `create in Rel { … }` links at runtime.
- **Fail closed.** Analysis errors block Lower/Emit. Never hand-edit generated artifacts to get past a diagnostic — fix the `.poly`.

## Sessions

Sessions are scratch and the server is shared: `list_sessions` lists every session and there is no ownership check. Work only in the `sessionId` you created; do not apply to or mutate another session's domain.

## Deeper inspection

`get_domain_overview`, `get_entity_detail`, `get_constraints`, `get_relationships`, `get_policy_expression`, `describe_domain_element`, `oracle_expression`.

## Reference

- MCP DSL guide: `get_dsl_guide` (any section) — product-true, and updated in the same change as the parser, printer, or `apply_dsl`.
- In-repo guide: `Poly.Mcp/Docs/poly-dsl-guide.md`.
