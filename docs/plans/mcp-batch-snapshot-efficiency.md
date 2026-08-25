# MCP Batch Operations & Snapshot Export

**Date:** 2026-07-13  
**Status:** Proposal — **SUPERSEDED 2026-08-08 by [`mcp-catalog-minify.md`](mcp-catalog-minify.md)** (minify suite: bulk via `apply_dsl`; incremental via unified `add`/`remove`; batch helpers `add_properties`/`add_stages`/`add_actions_to_stages` and `get_domain_snapshot` deleted). Do not re-admit per-type or batch tools without explicit suite admit.
**Source:** Agent feedback from ~150-call supply chain modeling session  
**Related:** `mcp-tool-surface-expansion.md` (DSL batch path), `mcp-guiding-principles.md` (token-efficient responses)

---

## Problem summary

The current MCP tool surface requires one round-trip per structural element. For a non-trivial domain model (~8 entities, ~40 properties, ~25 stages, ~30 actions, ~7 relationships), this produces ~150 sequential tool calls. Two issues:

1. **No bulk/plural operations.** Every property, stage, and action is a separate `add_property` / `add_stage` / `add_action` / `add_action_to_stage` call. Plural variants would cut call count by 5-10x.

2. **No full snapshot export.** To inspect the complete model, the agent must call `get_entity_detail` for every entity (N+1 calls). A single `get_domain_snapshot` would return all entities with full detail in one response.

---

## Relationship to existing work

| Existing doc | Overlap | Gap |
|-------------|---------|-----|
| `mcp-tool-surface-expansion.md` | Proposes a DSL batch-apply path ("Single parse + apply" for complex multi-step mutations) | DSL is a different approach — requires parser, grammar, and agent learning. Plural endpoints are simpler: same tool contract, array args. |
| `mcp-guiding-principles.md` | "Token-efficient responses", "outcomes over operations", "fewer high-impact tools" | Plural endpoints directly address these principles |
| `McpSessionStore.cs` | Single `DomainEvolution` per tool call today | Need to support multi-change evolve batches |

**Decision point: plural endpoints vs. DSL batch.** Both reduce call count, but plural endpoints are lower-risk (extend existing tool contracts with array overloads) and don't require the agent to learn a new grammar. The DSL path (proposed in `mcp-tool-surface-expansion.md`) can complement plural endpoints later — they share the same `DomainEvolution` underneath.

---

## Research questions

1. **Array arg semantics.** Should a batch of `add_properties` be atomic (all-or-nothing, one `DomainEvolution.Apply()`) or best-effort (each property individually, partial success possible)?
   - *Recommendation:* Atomic. Matches the existing single-mutation contract where any failure rolls back the entire evolve batch.

2. **Tool contract design.** Two options:
   - **Add new tools:** `add_properties`, `add_stages`, `add_actions_to_stages` — clean separation, easy discovery
   - **Overload existing tools:** `add_property` accepts either a single `{name, typeName}` or an array — backward compatible, fewer tools in the list

3. **`get_domain_snapshot` shape.** What should the snapshot include?
   - Full entity detail (properties with types, stages with actions, policies, relationships)?
   - Or a lighter overview with entity detail available on-demand?
   - *Recommendation:* Full detail. The whole point is to eliminate N+1 calls. Token budget is acceptable for typical domain sizes (8-15 entities).

4. **Pagination.** For very large domains (50+ entities), should `get_domain_snapshot` support pagination or entity-name filtering?

5. **Response size budget.** A full snapshot for an 8-entity model is ~2-4KB of JSON. At 50 entities, ~15-25KB. What's the MCP protocol guidance on response size limits?

---

## Proposed implementation

### Phase 1: Plural evolve tools

Add three new tools:

| Tool | Signature | Behavior |
|------|-----------|----------|
| `add_properties` | `(sessionId, entityName, properties: [{name, typeName}, ...])` | Atomic batch — single `DomainEvolution` with all properties |
| `add_stages` | `(sessionId, entityName, stages: [{name, parentStageName?}, ...])` | Atomic batch — all stages added in array order |
| `add_actions_to_stages` | `(sessionId, entityName, actions: [{stageName, actionName}, ...])` | Atomic batch — all actions placed on their respective stages |

Each tool wraps a single `DomainEvolution().Evolve()` chain:

```csharp
// add_properties example
var evolution = new DomainEvolution(state.Domain).Evolve();
foreach (var prop in properties) {
    evolution.AddPropertyToEntity(entityName, new Property(prop.Name, new DomainTypeReference(prop.TypeName), []));
}
var result = evolution.Apply();
```

**Keep existing singular tools** for simple use cases and backward compatibility.

### Phase 2: Full snapshot export

Add one new tool:

| Tool | Signature | Behavior |
|------|-----------|----------|
| `get_domain_snapshot` | `(sessionId)` | Returns all entities with full detail: properties (name + type), stages (name + parent + actions), policies, relationships |

Response shape:

```json
{
  "domain_name": "SupplyChain",
  "revision": 42,
  "primitive_types": ["Boolean", "Number", "Text", "Date", "Time", "DateTime", "Duration", "Uuid", "Binary"],
  "entities": [
    {
      "name": "Order",
      "properties": [
        {"name": "OrderId", "type": "Text"},
        {"name": "TotalAmount", "type": "Number"}
      ],
      "stages": [
        {"name": "Draft", "parent": null, "actions": ["Submit", "Cancel"]},
        {"name": "Confirmed", "parent": null, "actions": ["Ship", "Cancel"]}
      ],
      "policies": ["TotalPositive"],
      "actions": ["Submit", "Cancel", "Ship"]
    }
  ],
  "relationships": [
    {"name": "OrderLines", "source": "Order", "target": "OrderLine", "cardinality": "OneToMany"}
  ],
  "analysis": {
    "errors": 0,
    "warnings": 0
  }
}
```

### Phase 3: Agent guidance

Update the `domain-modeling.agent.md` to prefer batch endpoints for multi-element additions:

- Use `add_properties` when adding 2+ properties to one entity
- Use `add_stages` when defining all lifecycle stages up front
- Use `add_actions_to_stages` when placing all stage-level actions at once
- Use `get_domain_snapshot` instead of N× `get_entity_detail` for full model inspection

---

## Acceptance criteria

1. **Batch add_properties:** Adding 5 properties to an entity succeeds in one call; all 5 appear in `get_entity_detail`.
2. **Batch add_stages:** Adding 4 stages in order `[Draft, Review, Approved, Rejected]` — `get_entity_detail` returns them in that order.
3. **Batch add_actions_to_stages:** Placing 3 actions across 3 stages succeeds atomically.
4. **Atomic failure:** If one property in a batch of 5 fails (e.g., duplicate name), none are added and the error identifies which property failed.
5. **Snapshot completeness:** `get_domain_snapshot` returns all entities with properties, stages, actions, policies, and relationships in one response.
6. **Snapshot analysis:** The snapshot includes the current analysis diagnostics (error/warning counts).
7. **Backward compatibility:** All existing singular tools (`add_property`, `add_stage`, `add_action_to_stage`) continue to work unchanged.
8. **Call count reduction:** The supply chain model can be built in ~30-40 calls instead of ~150.

---

## Risks

- **Atomic batch failure UX.** If one property in a batch of 10 fails, the agent loses all 10. Mitigation: the error response must clearly identify which element caused the failure so the agent can exclude it and retry.
- **Snapshot size for large domains.** Mitigation: keep `get_entity_detail` for single-entity inspection; `get_domain_snapshot` is for full-model export. Consider optional `entityNames` filter parameter.
- **Redundancy with `get_domain_snapshot` and `get_relationships`.** If the snapshot includes relationships, `get_relationships` (from `mcp-domain-inspection-completeness.md`) may be unnecessary. Keep both — snapshot for bulk export, `get_relationships` for lightweight filtered queries.

---

## Related plans

| Plan | Relationship |
|------|-------------|
| [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | Batch operations are more likely to hit concurrency races if they're not atomic; safety fixes are prerequisite |
| [`mcp-domain-inspection-completeness.md`](mcp-domain-inspection-completeness.md) | `get_domain_snapshot` reduces the need for N+1 `get_entity_detail` calls during inspection |
| [`mcp-tool-surface-expansion.md`](v2-to-v3/mcp-tool-surface-expansion.md) | DSL batch-apply path is a complementary approach; plural endpoints are the simpler, lower-risk first step |
