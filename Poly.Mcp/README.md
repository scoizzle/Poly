# Poly.Mcp — MCP Server for Poly Domain Modeling

**Role:** interactive **harness** for agents using Poly. Holds a `DomainSession` (it is not that session). Author and inspect. Named-policy/action **simulate** is `evaluate_policy(instanceId)` and `invoke_action` on session instances. `oracle_expression` is a fragment probe, not that lock. Not a product entry-point extension (REST is `uses http`). Not a second evaluator.

Lock: [`docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md`](../docs/decisions/2026-08-15-domain-library-extensions-mcp-harness.md). Frozen core: [`docs/CORE.md`](../docs/CORE.md) §0. Mechanisms: [`docs/CORE.md`](../docs/CORE.md) §3.6.

## Tool Surface

Tools live in `Poly.Mcp/Tools/` and use only `Poly.DomainModeling` types (no `Poly.Data.Modeling`).
**30 tools** registered via `Program.cs` (`SessionTool`, `QueryTool`, `EvolveTool`, `PolicyTool`, `DslTool`, `OracleTool`, `RuntimeTool`).

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
| `get_domain_report` | `QueryTool` | Human-readable Markdown report: terms, properties with constraints and requiredness, stages, actions, enums, value types, relationships |
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
| `apply_dsl` | `DslTool` | Applies a `.poly` DSL document, **replacing** the session domain. On failure, changes nothing and returns structured findings |
| `validate_dsl` | `DslTool` | **Dry run**: parses, evolves, and analyzes a `.poly` document without changing the session; returns severity, code, element path, message |
| `export_dsl` | `DslTool` | Exports the current session domain as `.poly` DSL text |
| `get_dsl_guide` | `DslTool` | Short DSL guide by default. `section` returns one heading; `all` returns `Docs/poly-dsl-guide.md` |

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
| `export_domain_to_json_schema` | `OracleTool` | Exports the domain as JSON Schema (draft 2020-12), read-only: `$defs` per entity/value type/enum, with range, length, pattern, equality, default, and enum constraints projected (`unique` and other non-schema facts as `x-poly-*`) |
| `export_domain_to_json_ld` | `OracleTool` | Exports the domain as JSON-LD: an `@context` plus an RDFS `@graph` (classes, properties with `rdfs:domain`/`rdfs:range`, enum members); declared term IRIs are used, untagged elements get stable `urn:poly:` IRIs |
| `export_domain_to_shacl` | `OracleTool` | Exports the domain as SHACL (JSON-LD syntax): `sh:NodeShape` per entity targeting its class, `sh:PropertyShape` per property/navigation with range, length, pattern, equality, and requiredness constraints |
| `export_domain_to_html` | `OracleTool` | Exports a self-contained HTML document (no server, no scripts) for a person: terms, properties/constraints/requiredness, stages, actions, enums, value types, relationships |

### Runtime

| Tool | Class | Purpose |
|------|-------|---------|
| `create_instance` | `RuntimeTool` | Creates a runtime instance and registers it in the session store |
| `get_instance` | `RuntimeTool` | Snapshot: stage, properties, deletion status, child count |
| `list_instances` | `RuntimeTool` | Lists runtime instances (skips deleted); optional entity filter |
| `link_instances` | `RuntimeTool` | Links two instances via a relationship (store-aware) |
| `unlink_instances` | `RuntimeTool` | Unlinks two instances via a relationship |
| `invoke_action` | `RuntimeTool` | Invokes an action: guards → shipped effects (transition, assign, create, create-in, invoke, for-invoke, if) → subscription fan-out. Link/unlink existing instances via `link_instances` / `unlink_instances` |

## Interop Artifacts

Read-only exports hand the analyzed domain to other toolchains without re-deriving it:

- **`export_domain_to_csharp`** — the lowered C# model.
- **`export_domain_to_json_schema`** — JSON Schema (draft 2020-12). Each entity, value type, and enum becomes a `$defs` entry; `range`, `length`, `pattern`, `equals`, `default`, and enum members are projected to `minimum`/`maximum`, `minLength`/`maxLength`, `pattern`, `const`, `default`, and `enum`. A property is `required` unless it is `optional`, has a `default`, or is initialized by the first stage's entry effect — the same create rule the DSL uses. Ontology term metadata is projected too: an entity's `iri` becomes `x-poly-iri`, its `label` becomes `title`, and its `description` becomes `description`; a property's terms become `title`/`description`/`x-poly-iri`; enum-member terms become `x-poly-enum`. The IRI is deliberately not a subschema `$id` — that would start an embedded resource and rebase local `#/$defs` refs. Facts JSON Schema cannot express are kept as `x-poly-*`: `unique` becomes `x-poly-unique`, a non-numeric range bound becomes `x-poly-minimum`/`x-poly-maximum`, and an unrecognized constraint type is named in `x-poly-constraints` rather than dropped.

The exporter is a pure projection over `Domain` (`Poly/DomainModeling/Queries/JsonSchemaExporter.cs`), so the MCP tool, a test, or a human CLI all read the same text.

- **`export_domain_to_json_ld`** — JSON-LD + RDFS. The `@context` carries `rdf`/`rdfs`/`xsd` plus one term key per declared name (a repeated name is qualified as `Owner.Name`); the `@graph` has an `rdfs:Class` per entity/value type/enum, an `rdf:Property` per property and navigation with `rdfs:domain`/`rdfs:range`, and an instance of the enum class per member. Declared term IRIs win; an untagged element gets a stable `urn:poly:<Domain>/...` IRI instead of being dropped. Pure projection (`Poly/DomainModeling/Queries/JsonLdExporter.cs`).

- **`export_domain_to_shacl`** — SHACL in JSON-LD syntax over the same IRIs (`Poly/DomainModeling/Queries/ShaclExporter.cs`). Each entity and value type is a `sh:NodeShape` with `sh:targetClass` and `sh:closed true`; each property/navigation is a `sh:PropertyShape` with `sh:path`, `sh:datatype`/`sh:class` (and an RDF `sh:in` list for enums), `sh:minCount`/`sh:maxCount` from the create rule, and `sh:minInclusive`/`sh:maxInclusive`, `sh:minLength`/`sh:maxLength`, `sh:pattern` from the constraints. `equals` becomes a single-value `sh:in` list (typed literal, so RDF-term equality holds), `unique` and create-time defaults become the context-defined `x-poly-unique` / `x-poly-default`, and a constraint with no mapping is named in `x-poly-constraints`.

### Human Report

`get_domain_report` returns Markdown for a person: the domain header and counts, each entity with its IRI/description and a property table (type, constraints, requiredness, IRI), stages and actions, enum members with IRIs/terms, value types, and relationships. `export_domain_to_html` renders the same facts as one self-contained HTML file — no server, no scripts — so an agent can hand a person a document they can open directly. Both are the same projection discipline (`Poly/DomainModeling/Queries/DomainReportExporter.cs`, `DomainHtmlExporter.cs`): reading views over the facts, not a second summary.

### Term Metadata

`iri`, `label`, `description`, and (on enum members) `term` are **core** annotations, not a vendor pack. They attach to entity/enum headers, property tails, and enum members, round-trip through `export_dsl`, and are read in code through `TermFacets.Read(...)` / `TermMetadata` (`Poly/DomainModeling/Ontology/TermMetadata.cs`). The parser accepts exactly one non-empty string argument — `iri` must be an absolute `http(s)` or `urn` IRI — and fails closed on anything else.

```poly
Color: enum iri("https://example.com/Color") {
  Red iri("https://example.com/Color/Red") term("red"),
  Green,
}

Product: entity iri("https://example.com/Product") label("Product") {
  Sku: Text iri("https://example.com/Product/sku") label("SKU")
}
```

## Dual Authoring Path

Poly.Mcp supports two complementary ways to build a domain model, each suited to different workflows.

### Bulk Path (`apply_dsl`)

Before authoring a large domain, call **`get_dsl_guide`** to retrieve the product-true syntax guide.
This avoids inventing unsupported lab constructs.

Write the full domain in a single `.poly` DSL document. Iterate with `validate_dsl` until it
reports zero errors — the dry run leaves the session untouched — then apply it in one shot.
The session's domain is **replaced** entirely — not merged incrementally.

```
domain Orders

Customer: entity {
  Name: Text
  Email: Text unique
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

**Use when**: bootstrapping from scratch, iterating in a text editor, or recreating a known state. Parse errors produce line/col diagnostics. A rejected `apply_dsl` returns the same structured findings as `validate_dsl` (severity, code, element path, message) and changes nothing.

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

**DSL tools:** `validate_dsl` (dry run: parse → analysis gate, no session mutation, structured findings), `apply_dsl` (parses .poly text → analysis gate → replaces session domain; revision+1; clears runtime instances; a rejected document changes nothing and returns the same findings as `validate_dsl`; action `when Stage` is not a separate runtime gate; subscriptions need instances + `invoke_action` to fire), `export_dsl` (printer round-trip, no side effects), `get_dsl_guide` (embedded product guide).

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

## Harness Runtime

`Program.cs` composes the server through the host's DI container:

- **Store:** `ISessionStore` is a singleton (`McpSessionStore`). Sessions live in memory only — a restart loses them (keep a model with `export_dsl`). Each session has its own lock, so a slow mutation in one session never blocks another.
- **Eviction:** sessions idle longer than `Mcp:Sessions:IdleTimeout` (default 8h) are removed by `SessionEvictionService`; `SweepInterval` (default 15m) controls the sweep. Set `IdleTimeout` to zero to disable eviction.
- **Boundary:** one `AddCallToolFilter` catches uncaught exceptions and returns a failed call that still carries the `DomainToolResponse` envelope (`success:false`), so an agent sees the same shape as a tool-level failure.

Tool classes are instance classes resolved from DI with `ISessionStore` and `ILogger<T>`. Session identity stays the `sessionId` argument — this process is stdio and has no `Mcp-Session-Id` header.
