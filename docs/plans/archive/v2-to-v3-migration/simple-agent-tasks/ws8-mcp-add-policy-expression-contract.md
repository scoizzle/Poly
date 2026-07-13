# WS8 #7a — MCP add_policy Expression Contract

**Status:** ✅ Done — `PolicyExpressionContract` + `PolicyExpressionParser` exist in production  
**File:** `Poly/DomainModeling/Lowering/PolicyExpressionContract.cs`

## Contract

The MCP `add_policy` tool accepts a constrained JSON shape via `PolicyExpressionContract`. Agents never receive free-form AST JSON bags.

### Supported shapes

| Shape | Tool args | Example |
|-------|-----------|---------|
| Property comparison | `property`, `op`, `value` | `property: "Age", op: ">=", value: 18` |
| Composite AND | `and` | `and: '[{"property":"A","op":">","value":1},{"property":"B","op":"<","value":5}]'` |
| Composite OR | `or` | Same shape as AND |
| Composite NOT | `not` | `not: '{"property":"X","op":"==","value":true}'` |

### Comparison operators

`==`, `!=`, `>`, `>=`, `<`, `<=`

### Literal types

Numbers (mapped to `long`), strings, booleans.

### Unsupported (by design)

- Nested expressions beyond two-level and/or/not
- `DateOperation`, `RelationshipNavigation`, `OwnedAccess`
- Free-form AST JSON
- Multiple branches on one contract

### Mapping

```
add_policy(..., property: "Age", op: ">=", value: 18)
  → PolicyExpressionContract { Property = "Age", Op = ">=", Value = 18 }
  → PolicyExpressionParser.Parse(contract)
  → DomainExpression.GreaterThanOrEqual(Property("Age"), Literal(18L))
```
