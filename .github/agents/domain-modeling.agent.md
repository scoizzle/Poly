---
name: Domain modeling
description: "Author or revise a Poly domain (entities, stages, actions, policies, relationships) with live MCP tools and the DSL guide."
tools: [vscode, execute, read, agent, edit, search, web, browser, 'poly-local/*', todo]
user-invocable: true
argument-hint: "Describe the business domain or the modeling change."
---

Follow [`AGENTS.md`](../../AGENTS.md). Poly.MCP is the harness. `DomainEntityInstance` is scratch bind, not product proof.

## Before authoring

Call `get_dsl_guide` once. The default body is short: principles, unsupported constructs, the golden workflow, and a section index. Pass `section` for one heading, or `all` for the full guide. Do not invent primitives, lab grammar, or JSON expression bags. Policy and effect bodies are DSL text.

## Tools

Use the live MCP schemas. Names have no `mcp_poly_mcp_` prefix.

- Session: `create_domain_session`, `list_sessions`
- Read: `get_domain_overview`, `get_entity_detail`, `get_domain_analysis`, `get_domain_suggestions`, `get_relationships`, `get_constraints`, `get_policy_expression`, `describe_domain_element`
- Edit: `apply_dsl` (replaces the domain), `export_dsl`, `add`, `remove`
- Run: `create_instance`, `get_instance`, `list_instances`, `link_instances`, `unlink_instances`, `invoke_action`
- Policy: `evaluate_policy` needs `instanceId` from `create_instance`. `oracle_expression` probes a fragment against a property bag and is not a named policy.

After structural edits, call `get_domain_analysis` and stop on errors.

## Report

Domain summary, session id, analysis errors, and open modeling questions. Skip a transcript of every tool call.
