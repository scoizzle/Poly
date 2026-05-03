# Poly Text

`Poly.Text` provides low-allocation text primitives and parser components used by matching and numeric parsing features.

## Modules

- `StringView`: span-like text view helpers for slicing, searching, extraction, and comparison
- `Matching`: pattern expression model, parser, linker, and runtime comparison delegates
- `Parsers`: numeric parsing helpers (for example `Int32`, `Int64`, `Float32`, `Float64`)
- `Extensions`: text-related extension helpers

## Matching Pipeline

`Poly.Text.Matching` includes:

- `Expression`: base type for match expressions
- `Parser`: parses text patterns into expression trees
- `Linker`: wires expression graph links
- `Evaluation`: reusable delegates/constants for compare/extract behavior
- `Expressions/*`: concrete expression kinds (group, range, wildcard, static, whitespace, extraction)

## Minimal Example

```csharp
StringView pattern = "{name}";
if (Poly.Text.Matching.Parser.TryParse(pattern, out var expression)) {
    // expression can be linked and evaluated by matching infrastructure
}
```

## Notes

- `StringView` is the core input abstraction across text modules.
- Matching expressions expose `MinimumLength` and `Optional` for fast checks.
- Parsers are organized as dedicated static modules by numeric type.