# MCP Domain Inspection Completeness

**Date:** 2026-07-13  
**Status:** Proposal — research + implementation plan  
**Source:** Agent feedback from ~150-call supply chain modeling session  
**Related:** `mcp-tool-surface-expansion.md` (relationship = deferred), `mcp-guiding-principles.md` (discoverable surface)

---

## Problem summary

Two domain concept categories lack MCP inspection and creation tools:

1. **Relationships are invisible after creation.** `get_domain_overview` shows relationship counts, but there is no tool to list them or inspect individual relationships. After rollbacks, the agent cannot verify which relationships survived.

2. **No constraint tool exists.** The domain model supports `Constraint` subtypes (Range, Length, Pattern, etc.), and `add_policy` exists for policy guards, but there is no `add_constraint` to attach validation rules to properties. For business domains, constraints like `UnitCost > 0`, `QuantityOnHand >= 0`, or `LeadTimeDays >= 0` are essential.

---

## Relationship to existing work

| Existing doc | Overlap | Gap |
|-------------|---------|-----|
| `mcp-tool-surface-expansion.md` | "Slice 5 (relationship) is deferred pull-only" | No inspection tools planned for relationships |
| `DomainTools.cs` | `add_relationship` exists and works | No corresponding `get_relationships` or `get_relationship_detail` |
| `Poly/DomainModeling/Constraint.cs` | Constraint types exist in the core library | No MCP tool surface for them |
| `Poly/DomainModeling/Constraints/` | Directory exists with constraint subtypes | MCP tools don't reference them |

---

## Research questions

### Relationships

1. **What does `add_relationship` actually store?** Inspect `DomainTools.cs` to confirm the relationship is persisted in the domain model and survives rollbacks.

2. **What should `get_relationships` return?** Options:
   - Flat list of all relationships with source, target, cardinality, ownership
   - Grouped by entity (relationships where entity is source)
   - Full graph with both directions

3. **Is `get_relationship_detail` needed separately, or can `get_relationships` return enough detail?** If relationships only have name + source + target + cardinality + ownership, `get_relationships` alone suffices.

### Constraints

4. **Are constraints on properties or on entities?** In the current domain model, are `Constraint` objects attached to `Property` definitions, or are they free-standing like `Policy`? Inspect `Property.cs` and `Constraint.cs`.

5. **What constraint types exist?** Catalog the `Constraints/` directory and identify which are production-ready vs. placeholder.

6. **How do constraints interact with analysis?** Does `get_domain_analysis` already flag constraint violations? If not, should it?

7. **Constraint vs. Policy distinction.** A `Policy` is a boolean `DomainExpression` guard on an entity. A `Constraint` is a validation rule on a property. Should they share the same MCP evaluation path, or are they orthogonal?

---

## Proposed implementation

### Phase 1: Relationship inspection tools

Add two tools:

| Tool | Signature | Behavior |
|------|-----------|----------|
| `get_relationships` | `(sessionId, entityName?)` | If `entityName` provided: relationships where that entity is source OR target. If omitted: all relationships in the domain. |
| `get_relationship_detail` | `(sessionId, relationshipName)` | Full detail for one relationship: source, target, cardinality, ownership, any metadata |

**`get_relationships`** response shape:

```json
{
  "relationships": [
    {
      "name": "OrderLines",
      "source_entity": "Order",
      "target_entity": "OrderLine",
      "cardinality": "OneToMany",
      "source_owns_target": true
    },
    {
      "name": "SupplierProducts",
      "source_entity": "Supplier",
      "target_entity": "Product",
      "cardinality": "ManyToMany",
      "source_owns_target": false
    }
  ]
}
```

**`get_relationship_detail`** may be unnecessary if `get_relationships` returns sufficient detail. Defer until a use case demands it.

### Phase 2: Constraint tools

Add two tools:

| Tool | Signature | Behavior |
|------|-----------|----------|
| `add_constraint` | `(sessionId, entityName, propertyName, constraintType, constraintConfig)` | Attach a validation constraint to a property |
| `get_constraints` | `(sessionId, entityName?, propertyName?)` | List constraints, filterable by entity and/or property |

**`add_constraint`** contract:

```json
{
  "sessionId": "abc123",
  "entityName": "Product",
  "propertyName": "UnitCost",
  "constraintType": "Range",
  "constraintConfig": {
    "min": 0,
    "max": null,
    "minInclusive": false,
    "message": "UnitCost must be greater than 0"
  }
}
```

**Constraint types to support (Phase 2):**

| Type | Config | Example |
|------|--------|---------|
| `Range` | `min`, `max`, `minInclusive`, `maxInclusive` | `UnitCost > 0` |
| `Required` | (none) | `Name` must not be null/empty |
| `Length` | `min`, `max` | `SKU` between 5 and 20 characters |
| `Pattern` | `regex` | `Email` matches email pattern |
| `Unique` | (none) | `OrderId` must be unique |

**Research needed before implementing:** Confirm which constraint types actually exist in `Poly/DomainModeling/Constraints/` and which need to be built.

### Phase 3: Analysis integration

If constraints aren't already checked by `get_domain_analysis`, add a constraint validation pass so that:

- `get_domain_analysis` returns constraint-specific warnings (e.g., "Property 'UnitCost' has a Range constraint but no min or max value specified")
- `get_entity_detail` includes constraint information in the property display

---

## Acceptance criteria

1. **`get_relationships` (all):** Returns all 7 relationships from the supply chain model with correct source, target, and cardinality.
2. **`get_relationships` (filtered):** `get_relationships(sessionId, "Order")` returns only relationships where Order is source or target.
3. **`add_constraint`:** Adding `Range(min: 0)` to `Product.UnitCost` succeeds; `get_entity_detail("Product")` shows the constraint on `UnitCost`.
4. **`get_constraints`:** Returns all constraints across all entities; filterable by entity and property.
5. **Constraint survival:** Constraints survive rollbacks — after a failed add_property on an unrelated entity, existing constraints remain.
6. **Analysis integration:** `get_domain_analysis` flags malformed constraints (e.g., Range with min > max).
7. **No regression:** Existing 15 MCP tools and smoke tests pass unchanged.

---

## Risks

- **Constraint implementation depth.** If constraint types in `Poly/DomainModeling/Constraints/` are placeholder stubs, this expands from MCP tool work into core domain modeling work. Mitigation: Phase 2 explicitly only supports constraint types that already exist in the core library.
- **Relationship tool redundancy.** If `get_domain_snapshot` (from `mcp-batch-snapshot-efficiency.md`) already returns relationships, `get_relationships` may be redundant. Mitigation: keep `get_relationships` as a lightweight filterable alternative to the full snapshot.

---

## Related plans

| Plan | Relationship |
|------|-------------|
| [`mcp-mutation-safety.md`](mcp-mutation-safety.md) | Constraints and relationships must survive rollbacks; idempotency applies to `add_constraint` and `add_relationship` too |
| [`mcp-batch-snapshot-efficiency.md`](mcp-batch-snapshot-efficiency.md) | `get_domain_snapshot` includes relationships; `get_relationships` is the lightweight filtered alternative |
| [`mcp-tool-surface-expansion.md`](v2-to-v3/mcp-tool-surface-expansion.md) | Slice 5 (relationships) is deferred there; this plan defines the concrete inspection tools |
