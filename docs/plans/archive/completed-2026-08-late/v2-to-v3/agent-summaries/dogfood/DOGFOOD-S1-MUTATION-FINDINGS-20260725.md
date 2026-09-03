# Dogfood -- S1 Mutation-Adversarial Findings

**Date:** 2026-07-25
**Session:** 353f260cde2c4f959771d1ad14c00d37 (after apply_dsl with Fine)
**Scenario:** Supplemental to S1 -- random mutation of an already-applied domain via MCP
**Result:** PASS (exploratory)

## Summary

After the S1 happy-path domain was applied (4 entities, 5 relationships, 7 actions, 3 policies), the MCP micro-tool surface was exercised with mutations and adversarial inputs.

## What worked

| Operation | Example | Result |
|-----------|---------|--------|
| Add entity | `add_entity("Fine")` | ✅ |
| Add properties batch | `add_properties([Name, Amount, ...])` | ✅ |
| Add property | `add_property("ISBN")` on Book | ✅ fail-closed (duplicate name) |
| Add relationship | `add_relationship("fines", OneToMany)` | ✅ |
| Add stage | `add_stage("Fine", "Unpaid")` | ✅ |
| Add constraint | `add_constraint("Amount", Required)` | ✅ |
| get_constraints | After mutations | ✅ shows all constraints |
| get_relationships | After mutations | ✅ 5 relationships listed |
| get_domain_suggestions | After mutations | ✅ 2 suggestions (Book no stages, Fine no actions) |
| describe_expression | Compound and + < | ✅ clear plain English |
| simulate_policy | Amount > 0 with Amount=5 | ✅ true |
| simulate_policy | Amount > 0 with Amount=0 | ✅ false |
| get_policy_expression | IsOverdue on Fine | ✅ AST dump returned |
| apply_dsl re-batch | Full domain including Fine | ✅ clean, zero errors |
| Duplicate entity detection | Two "Book" declarations | ✅ fail-closed |
| Duplicate property detection | Two "Title" on Book | ✅ fail-closed |
| Missing entity target | Property on "NonExistent" | ✅ fail-closed clear message |

## Observations / gaps

### G1 -- simulate_policy with non-existent property returns true (fail-open)

When `simulate_policy` is called with a property name that doesn't exist in the properties bag (`{"property":"NonExistent","op":"==","value":1}` with `{"Something":5}`), it returns **true** instead of failing or warning.

| Field | Value |
|-------|--------|
| Bucket | R (Runtime surprise) |
| Score | F2 + B3 + N3 = 8 |
| Smallest fix | Return false or diagnostic when property access cannot resolve |
| Workaround | None -- can silently produce wrong results |

### G2 -- get_policy_expression returns raw AST dump

The expression string is a C# `ToString()` of AST nodes with internal IDs and collection types rather than a concise JSON or DSL representation. Hard for agents to consume programmatically.

| Field | Value |
|-------|--------|
| Bucket | W (Workaround only) |
| Score | F3 + B2 + N1 = 6 |
| Smallest fix | Serialize expression as JSON using the same format as add_policy input |
| Workaround | Use describe_expression instead |

### G3 -- StoragePass noise in rollback diagnostics

When a non-storage-related error causes rollback (e.g. duplicate property), the StoragePass "requires EffectTopology and OwnershipAggregate" diagnostic leaks into the user-facing error message, making the real issue harder to spot.

| Field | Value |
|-------|--------|
| Bucket | A (Analysis noise) |
| Score | F3 + B2 + N2 = 7 |
| Smallest fix | StoragePass should not produce errors when no priorAnalysis is available -- should silently skip instead |
| Workaround | Parse the message for the first relevant diagnostic |

### G4 -- Tool disabled state is inconsistent

Across the session, different tools became disabled at different points: `invoke_action`, `add_action_to_stage`, `add_action`, `remove_relationship`, `export_dsl`, `add_policy`, `get_policy_expression` were all intermittently disabled but then became available after activating different tool groups.

| Field | Value |
|-------|--------|
| Bucket | S (Agent skill) |
| Score | F4 + B3 + N1 = 8 |
| Smallest fix | Document which tool groups need activation, or make tool groups a server-side concern |
| Workaround | Re-activate tool groups periodically |

## What worked (keep)

- Micro-tools add_entity, add_properties, add_relationship, add_stage, add_constraint all work cleanly and produce consistent state
- apply_dsl re-batch after micro-tool mutations works -- replaces domain, clears instances, zero analysis errors
- describe_expression oracle tool gives clear plain English
- simulate_policy oracle tool works for real properties
- Fail-closed on structural issues (duplicate names, missing entities) is strong
- Suggestions after mutations are relevant and actionable
