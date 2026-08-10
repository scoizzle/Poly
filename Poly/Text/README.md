# Poly Text

`Poly.Text` provides low-allocation text primitives and numeric parsing helpers.

## Modules

- `StringView`: span-like text view helpers for slicing, searching, extraction, and comparison
- `Parsers`: numeric parsing helpers (for example `Int32`, `Int64`, `Float32`, `Float64`)
- `Extensions`: text-related extension helpers

## Notes

- `StringView` is the core input abstraction across text modules.
- Parsers are organized as dedicated static modules by numeric type.
- `Matching/` was deleted 2026-08-09 (dead-dual cleanup) — Grammar is the pattern engine; see `docs/plans/dead-dual-inventory-2026-08-08.md`.