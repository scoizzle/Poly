# TypeExpression Vocabulary (v1)

This document defines the canonical TypeExpression grammar used by V2 contracts.

## Allowed Patterns

### Primitive scalars
- `string`
- `int`
- `long`
- `decimal`
- `bool`
- `date`
- `datetime`
- `guid`

### Nullable scalar
- Append `?` to a primitive scalar.
- Examples: `string?`, `int?`, `datetime?`

### Type reference
- Format: `{BoundedContextName}.{TypeName}`
- Examples: `Billing.Invoice`, `Security.User`

### Nullable type reference
- Append `?` to a type reference.
- Example: `Billing.Invoice?`

### List variants
- Append `[]` to a primitive or type reference.
- Examples: `string[]`, `Billing.Invoice[]`

## Not Allowed in v1
- Nested list types such as `string[][]`
- Nullable list types such as `string[]?`
- Whitespace in any expression
- Unqualified type names such as `Invoice`

## ABNF
```abnf
type-expression = primitive / primitive-nullable / type-ref / type-ref-nullable / primitive-list / type-ref-list

primitive = "string" / "int" / "long" / "decimal" / "bool" / "date" / "datetime" / "guid"
primitive-nullable = primitive "?"
primitive-list = primitive "[]"

type-ref = identifier "." identifier
type-ref-nullable = type-ref "?"
type-ref-list = type-ref "[]"

identifier = ALPHA *(ALPHA / DIGIT / "_")
```

## Parser Contract
Use `TypeExpression.TryParse(string input, out TypeExpressionKind kind, out string? referencedTypeName)`.

- Returns `true` only for expressions matching this vocabulary.
- `referencedTypeName` is populated only for type-reference kinds.
- Returns `false` for any expression outside this grammar.
