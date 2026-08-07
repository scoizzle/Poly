# GI-preflight (GIP) — C99 Matcher dual-run notes

**Date:** 2026-08-07  
**Status:** Green — structure dual-run on existing C99 integration corpus  
**Harness:** [`Poly.Tests/Integration/C99ParserInterpreterTests.cs`](../../Poly.Tests/Integration/C99ParserInterpreterTests.cs)

## Inventory (GIP-0)

| Construct | RD | Grammar dispatch |
|-----------|----|------------------|
| Struct definition (top) | yes | `top` / `struct-def` |
| Function header (primitive / struct return) | yes | `top` / `function`, `function-struct` |
| Namespace reject | yes | `top` / `namespace-reject` |
| Decl (int/float/double/struct, arrays) | yes | `block-item` / `decl`, `decl-struct` |
| return / if / while / for / nested block | yes | matching `block-item` patterns |
| Expr / assign / ternary / logical / member / index | yes | **RD only** (hybrid E2) |
| Designated struct/array initializers | yes | via decl → RD initializer |

## What dual-run proves (GIP-1/2)

- Matcher + pattern table can drive **statement and declaration dispatch** under a real multi-construct language (control flow, structs, designators).
- C99 reader already used Peek-as-current (no dual-cursor bug); product DSL needed `Unread` — both models exercised.
- **Expressions stay RD** (precedence layers, ternary, member/index). Confirms product **E2 hybrid** is viable under load; pure pattern-table left-assoc expr (E1) is still open work for temporal pack.

## Gaps → product GI (GIP-3)

| Gap | Implication for product GI |
|-----|----------------------------|
| Left-associative binary ops are loops in RD, not table patterns | Prefer hybrid until GI-4 E1; do not force pure pattern expr for cutover |
| Longest-match alone does not encode operator precedence | E1 needs layered rules and/or Pratt-in-handler |
| Handlers still own fail-closed validation | Same as product: grammar dispatches; handlers validate |
| Full AST structural equality not asserted | Dual-run is **execute equality** on shared corpus (stronger customer signal) |

## Exit

- Dual-run tests green (`DualRun_*` in C99 integration suite).
- Product GI-1..3 already landed; preflight no longer blocks further GI slices.
- Optional follow-on: pure expression grammar spike (not required for structure cutover).
