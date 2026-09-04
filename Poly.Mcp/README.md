# Poly.Mcp — MCP Server for Poly Domain Modeling

**Role:** interactive **harness** for agents using Poly. Holds a `DomainSession` (it is not that session). Author and inspect. Named-policy/action **simulate** is `evaluate_policy(instanceId)` and `invoke_action` on session instances. `oracle_expression` is a fragment probe, not that lock. Not a product entry-point extension (REST is `uses http`). Not a second evaluator.

Lock: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md). Mechanisms: [`docs/CORE.md`](../docs/CORE.md) §3.6.

## Tool Surface

Tools live in `Poly.Mcp/Tools/` and use only `Poly.DomainModeling` types (no `Poly.Data.Modeling`).
**24 tools** registered via `Program.cs` (`SessionTool`, `QueryTool`, `EvolveTool`, `PolicyTool`, `DslTool`, `OracleTool`, `RuntimeTool`).

### Session

| Tool | Class | Purpose |
|------|-------|---------|
| `create_domain_session` | `SessionTool` | Creates a bootstrapped domain session with built-in primitive types |
| `list_sessions` | `SessionTool` | Lists active domain sessions |

### Query / inspect

| Tool | Class | Purpose |
|------|-------|---------|
| `get_domain_overview` | `QueryTool` | Returns domain overview with entity/primitive/relationship counts |
| `get_entity_detail` | `QueryTool` | Returns entity properties, stages, actions, policies, navigations, subscriptions |
| `get_domain_analysis` | `QueryTool` | Returns analysis diagnostics (errors, warnings, info, hintCount) |
| `get_domain_suggestions` | `QueryTool` | Authoring suggestions (advisory DMAS001 hints) |
| `get_relationships` | `QueryTool` | Lists relationships; optional entity filter |
| `get_constraints` | `EvolveTool` | Lists constraints on an entity's properties |
| `get_policy_expression` | `PolicyTool` | Inspect-only guard expression text |

### Unified evolve (incremental structure)

| Tool | Class | Purpose |
|------|-------|---------|
| `add` | `EvolveTool` | Creates one domain element: `kind` ∈ entity, property, stage, action, stage_action, relationship, constraint, policy + JSON `payload` |
| `remove` | `EvolveTool` | Removes one domain element by identity: `kind` + identity `payload` (constraint remove not supported — use apply_dsl) |

### DSL (bulk structure)

| Tool | Class | Purpose |
|------|-------|---------|
| `apply_dsl` | `DslTool` | Applies a `.poly` DSL document, **replacing** the session domain |
| `export_dsl` | `DslTool` | Exports the current session domain as `.poly` DSL text |
| `get_dsl_guide` | `DslTool` | Product-true Phase 1a/1b syntax guide (**embedded resource only** — pack must include `Docs/poly-dsl-agent-guide.md` as EmbeddedResource; no filesystem fallback) |

### Policy

| Tool | Class | Purpose |
|------|-------|---------|
| `evaluate_policy` | `PolicyTool` | Evaluates a named policy on an instance from `create_instance` (`instanceId` required) |

### Oracle

| Tool | Class | Purpose |
|------|-------|---------|
| `describe_domain_element` | `OracleTool` | Describes entity/stage/action/policy/relationship |
| `oracle_expression` | `OracleTool` | Fragment probe: evaluates a **DSL expression fragment** against a JSON property bag. **Not** named-policy evaluate |
| `export_domain_to_csharp` | `OracleTool` | Exports the domain session as C# record/class definitions |

### Runtime

| Tool | Class | Purpose |
|------|-------|---------|
| `create_instance` | `RuntimeTool` | Creates a runtime instance and registers it in the session store |
| `get_instance` | `RuntimeTool` | Snapshot: stage, properties, deletion status, child count |
| `list_instances` | `RuntimeTool` | Lists runtime instances (skips deleted); optional entity filter |
| `link_instances` | `RuntimeTool` | Links two instances via a relationship (store-aware) |
| `unlink_instances` | `RuntimeTool` | Unlinks two instances via a relationship |
| `invoke_action` | `RuntimeTool` | Invokes an action: guards → shipped effects (transition, assign, create, create-in, invoke, for-invoke, if) → subscription fan-out. Link/unlink existing instances via `link_instances` / `unlink_instances` |

## Dual Authoring Path

Poly.Mcp supports two complementary ways to build a domain model, each suited to different workflows.

### Bulk Path (`apply_dsl`)

Before authoring a large domain, call **`get_dsl_guide`** to retrieve the product-true syntax guide.
This avoids inventing unsupported lab constructs.

Write the full domain in a single `.poly` DSL document and apply it in one shot. The session's domain is **replaced** entirely — not merged incrementally.

```
domain Orders

Customer: entity {
  Name: Text required
  Email: Text required unique
  Places: many Order
}

Order: entity {
  Total: Number
  Draft: stage {
    Submit: action { transition to Active }
  }
  Active: stage {}
}
```

**Use when**: bootstrapping from scratch, iterating in a text editor, or recreating a known state. Parse errors produce line/col diagnostics.

### Incremental Path (`add` / `remove`)

Use `add(kind, payload)` to create one element (entity, property, stage, action, stage_action, relationship, constraint, policy) and `remove(kind, payload)` to delete one by identity. Each call is a single `DomainChange` that goes through the full analysis pipeline, so errors are caught immediately. The tool Description embeds the kind → payload table.

**Use when**: exploring a model interactively, responding to user prompts in a chat UI, or programmatic construction where each step needs validation.

### Choosing Between Them

| Scenario | Preferred Path |
|----------|---------------|
| Starting a new model from a known definition | Bulk (`apply_dsl`) |
| Iterating on a DSL file in an editor | Bulk (`apply_dsl`) |
| Reproducing a bug or known state | Bulk (`apply_dsl`) |
| Interactive exploration | Incremental (`add` / `remove`) |
| AI agent building a model step by step | Incremental (`add` / `remove`) |
| Round-tripping (export → edit → re-apply) | Bulk (`export_dsl` → `apply_dsl`) |

Both paths converge to the same internal representation and produce identical models. There is **no third authoring language**: expressions are product DSL text only — never JSON IR bags.

## Tool Honesty Invariant

Every MCP tool's **Name + Description + Success** must match actual behavior:

| If the tool… | Then… |
|--------------|--------|
| Name/Description says evaluate / true-false | Must actually evaluate and return `data.result: bool` |
| Only inspects metadata | Must be named/described as inspect/get/describe — **never** as evaluate |
| Evaluation fails | `Success: false` (or explicit error), not success without a bool |

**Current policy tools:** `get_policy_expression` (inspect-only), `evaluate_policy` (named session policy on an `instanceId`). `oracle_expression` is an **expression probe** (DSL fragment + property bag, no session) — **not** named-policy evaluate. All satisfy the invariant for what they actually do.

**DSL tools:** `apply_dsl` (parses .poly text → analysis gate → replaces session domain; revision+1; clears runtime instances; action `when Stage` is not a separate runtime gate; subscriptions need instances + `invoke_action` to fire), `export_dsl` (printer round-trip, no side effects), `get_dsl_guide` (embedded product guide).

## Runtime Tools — Exercise Domain Lifecycle

The **RuntimeTool** family closes the final feedback loop: agents can create instances, inspect state, and execute actions — all within the MCP session.

### Create → Call → Observe

```text
1. apply_dsl or add/remove  →  model in session
2. create_instance          →  instanceId + initial snapshot
3. invoke_action              →  effects execute, stage transitions, subscriptions fire
4. get_instance             →  observe new stage + modified properties
5. list_instances           →  enumerate all instances (optionally by entity)
```

### Instance lifecycle

- Instances are **session-scoped** — each session has its own instance set.
- The **first defined stage** is the initial stage (if stages exist).
- `invoke_action` resolves from the **current stage** first, then entity-level actions.
- Guard policies (action-level, stage-level, entity-level) are evaluated before effects.
- Shipped action effects: **transition, assign, create, create-in, invoke, for-invoke, if**. Linking existing instances is `link_instances` / `unlink_instances` — not action-body effects.
- On **stage transition**: OnExit → set new stage → OnEntry → notify store subscribers.
- Stage subscription fan-out happens automatically for linked subscriber instances.
  - Subscriptions fire when the relationship **TARGET** entity enters a matching stage (not the source).
  - Example: `when orders Active { ... }` on Customer fires when a linked Order enters its Active stage.

### Honesty

- `create_instance` **writes** session instance state (creates + registers an instance).
- `get_instance` / `list_instances` are **inspect** tools — they read state, no execution.
- `invoke_action` runs the named action on the session instance (same path as the core library).
- Successful `apply_dsl` / evolve replaces the domain root and **clears** prior runtime instances (they held the previous entity graph).
- Related-policy expressions (`Rel.Prop`, `Rel exists`, `Rel where`) evaluate on store instances via `create_instance` + `link_instances` + `evaluate_policy`. `oracle_expression` is bag-only and fail-closed for those reads.
