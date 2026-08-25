# Plan: Grammar as the language cycle (stop the hoops)

**Date:** 2026-08-14  
**Status:** Proposal — not CURRENT until admitted. Supersedes leftover “bridge” work in [`pack-host-2026-08-13.md`](pack-host-2026-08-13.md) phase-1 leftovers and the parked “LeftAssoc live fold” line as the *Grammar* workstream. Does not reopen MEF / IDomainPack.  
**Suite:** [`simple-agent-tasks/gcyc-README.md`](simple-agent-tasks/gcyc-README.md)  
**Related:** [`grammar-pure-end-state.md`](grammar-pure-end-state.md); [`../decisions/2026-08-14-domain-libraries.md`](../decisions/2026-08-14-domain-libraries.md); [`domainmodeling-simplification-2026-08-14.md`](domainmodeling-simplification-2026-08-14.md)

---

## Why we jump through hoops

A language cycle is five objects: **tokens** (decode/encode pair) → **forms** → **recognize** → **fold** (form → IR) → **unparse** (IR → same form → tokens). One form table per compilation unit. Fold and unparse are inverses. Unprintable IR fails closed.

Poly’s engine already has tokens, forms, recognize, and emit-skeleton (`Matcher`, `Printer`, immutable `Grammar` + `GrammarBuilder`, `Language`). The product still treats Grammar as a **scanner with a printer attached**. Meaning and most spelling live beside it. Every library feature then needs a new side door:

| Hoop | Why it exists |
|------|----------------|
| `IExpressionPrimaryForm` RD | `ParsePrimary` does not `MatchRule("expr-primary")` then fold by pattern name |
| `IBinaryExpressionFold` | `ParseAdd` is still a method ladder; `DateOperation` is a post-hoc IR rewrite |
| `Optional(Comma)` on `Now` / `MAGIC` | Longest-match tie with `ident`; table cannot say “more specific” |
| `at++` print fills | `Value` / `Predicate` / `Ref` all fire the same anonymous callback |
| Dual `DslGrammar.For` | Parser and printer each rebuild the table; no session holds one `Language` |
| `DomainDslPrinter` `StringBuilder` | Structure (`domain`, `uses`, `entity`) never looks up a form |
| `ExpressionFormRegistry` | Kitchen sink: RD forms + folds + grammar contributors + print mappings |
| Process `*.Default` Temporal tables | Meaning is not on the session’s table |

gpure said: painful ⇒ evolve Grammar, do not grow RD. We grew bridges instead. This plan deletes the bridges by giving Grammar the two engine holes that force them, then moving product parse/print onto that cycle.

---

## Target shape

```text
Domain.Extensions                 facts only
        │
DomainSession.Open(domain, catalog)
        │
   GrammarBuilder ← Core + each library.Contribute(forms)
        │ Build once
   Language          recognize + emit  (same Grammar)
   FoldTable<TIr>    (rule, pattern) → IR     [product-typed, held by session]
        │
   apply_dsl   Matcher + FoldTable
   export_dsl  IR → (rule, pattern, named fills) → Printer
```

A library registers **on the builder**: pattern(s) + fold + named print mapping. It does not register a private parser or `ToString`.

`Domain.ResolveHost`, `IExpressionPrimaryForm`, `IBinaryExpressionFold` as a product API, and `ExpressionFormRegistry` die in that order.

---

## Non-goals (first admit)

- Rewrite `PolyDslParser` structure / effects (headers already `MatchRule`).
- Drive every expression layer from `LeftAssoc` (G3).
- MEF, `IDomainPack`, plugin host.
- `DomainExpression` types inside `Poly.Grammar`.
- Pretty-print the whole document (G4) as a Temporal prerequisite.
- Make Temporal optional in the product seed.

---

## Engine

### E1 — Named holes (first admit)

`Value(kind, name)`, `Predicate` label is the capture/fill key. Matcher writes `Captures[name]`. `PrintContext.Fill(name, text)`. Duplicate names in one pattern fail closed. Anonymous `onContent` remains for skeletons.

### E2 — Specificity (park if it fights longest-match)

Declared priority or predicate-led beats bare ident on a tie. Deletes `Optional(Comma)`. Do not invent a third disambiguator.

### E3 — Structured `LeftAssoc` (not first admit)

Flat span is enough until G3.

---

## Product

### G0 — One `Language` in `DomainSession`

`Open(domain, catalog)` builds once. Parser and printer take that instance. MCP holds the session. Reload only when `uses` changes.

### G2 — Primaries are forms

`ParsePrimary` = `MatchRule` + fold by pattern name. Delete `IExpressionPrimaryForm`. Temporal `Now` / `Duration` are folds + named fills.

### G3 / G4 / G5 (later)

Binary specialize on IR (delete parse-time `IBinaryExpressionFold`). Structure unparse through the table. Process-wide Temporal `*.Default` moves onto the session.

---

## First suite (`gcyc`)

```text
gcyc-0  This doc + CORE sentence
gcyc-1  E1 named captures + named print fills
gcyc-2  DomainSession + one Language
gcyc-3  Core primary folds; ParsePrimary uses MatchRule
gcyc-4  Temporal + E1 magic onto folds; delete IExpressionPrimaryForm
gcyc-5  Guide + CORE; gate
```

**Gate:** zero product `IExpressionPrimaryForm`; `Now` / `N Days` round-trip on the session `Language`; parser and printer share one `Grammar` reference; no `temporal` ⇒ `Now` does not parse; suite green.

**Admit:** park pack-2 `IDomainPack`. TokenWriter already shipped.
