# GI-preflight (GIP) — C99 Matcher dual-run notes

**Date:** 2026-08-07  
**Status:** **DONE / green** — closed 2026-08-07  
**Harness:** [`Poly.Tests/Integration/C99ParserInterpreterTests.cs`](../../Poly.Tests/Integration/C99ParserInterpreterTests.cs)  
**Plan:** [`grammar-integration.md`](grammar-integration.md) §3.4 · §11.1

---

## Exit criteria (all met)

| ID | Work | Exit |
|----|------|------|
| **GIP-0** | Inventory C99 subset | This doc §Inventory |
| **GIP-1** | `C99Grammar` structure patterns (top + block-item); expr = RD hybrid | Nested `C99Grammar` in harness |
| **GIP-2** | Dual-run vs hand `C99Parser` | Default `CompileC99` dual-compiles both paths; `DualRun_*` execute equality; throw cases via `AssertBothPathsThrowAsync` |
| **GIP-3** | Gaps → product strategy | §Gaps below → prefer **E2 hybrid** for product cutover |

**Not in scope (parked):** pure expression grammar (E1); C99 as a product language; AST structural equality (execute equality is the bar).

---

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

---

## What dual-run proves (GIP-1/2)

- Matcher + pattern table can drive **statement and declaration dispatch** under a real multi-construct language (control flow, structs, designators).
- C99 reader uses Peek-as-current (no dual-cursor); product DSL uses `Unread` — both models exercised in the tree.
- **Expressions stay RD** (precedence, ternary, member/index). Confirms product **E2 hybrid** under load; pure pattern-table left-assoc expr (**E1**) remains open for temporal pack.
- Full C99 integration corpus: success paths dual-compile; fail-closed messages dual-asserted.

---

## Gaps → product GI (GIP-3)

| Gap | Implication for product GI |
|-----|----------------------------|
| Left-associative binary ops are loops in RD, not table patterns | Prefer hybrid until dedicated E1; do not force pure pattern expr for cutover |
| Longest-match alone does not encode operator precedence | E1 needs layered rules and/or Pratt-in-handler |
| Handlers still own fail-closed validation | Same as product: grammar dispatches; handlers validate |
| Full AST structural equality not asserted | Dual-run is **execute equality** / dual-compile (customer signal) |

---

## How to re-run

```bash
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/Poly.Tests.Integration/*/*'
# or full suite
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

Look for `DualRun_*` and any `CompileC99` consumer (default dual-compiles).

---

## Decision

**Preflight is closed.** Product GI may proceed without a GIP gate. Residual E1 work is tracked under product GI-4 expression strategy / temporal pack admit — not a re-open of GIP.
