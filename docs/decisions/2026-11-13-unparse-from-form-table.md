# ADR: Unparse from the form table is planned; the temporal fold is an operator-table claim (#677 G-20)

## Status

**Accepted** — slice G-20 of epic [#677](https://github.com/scoizzle/Poly/issues/677) (row in
[#678](https://github.com/scoizzle/Poly/issues/678)), plan lock 2026-11-13
([comment 6842875973](https://github.com/scoizzle/Poly/issues/678#issuecomment-6842875973)).
This record **advances** [#470](https://github.com/scoizzle/Poly/issues/470); it does not close it.
#470 carries `needs-scot`, so closing it is the owner's ruling, with this record as the evidence.

## Context

#470 had two parts: (1) drive the DSL printer and reader for entity, stage and effect forms from the grammar table,
and (2) retire the second seam for the binary expression fold. After G-10 the temporal fold was reachable two ways:
the operator table's `+`/`-` rows (document parse) and `DslExpressionFragment.Resolver.Fold` (the fragment `Resolve`),
both ending in `ExpressionFormRegistry.TryFoldBinary` through `IBinaryExpressionFold`.

## Decision

1. **Part one of #470 is planned, not parked.** It is planned in this epic's Grammar layer: the printer ordering requirement
   (Q12) was met in G-18, and the table-driven printer and reader are carried by #799 and #850 and the G-series rows that
   follow this one (the operator table's print half is G-34).
2. **The binary-fold bridge is retired.** `IBinaryExpressionFold`, `ExpressionFormRegistry.RegisterBinaryFold` and
   `TryFoldBinary` are gone. A library claims a binary operator through
   `ExpressionFormRegistry.RegisterBinaryClaim(DslTokenKind op, Func<…>)`, in registration order, first non-null wins.
   `TemporalLibrary` registers `DateOperationFold.Plus` and `DateOperationFold.Minus`.
3. **One claim and one fallback.** `DslOperatorTable.Core(forms)` wraps every binary row as its claim first and its plain
   node as the single fallback. The document parse and `DslExpressionFragment.Resolve` both fold through that row
   (`DslOperatorTable.Fold`), so the unclaimed fallback lives only in `Core`. `Claim` and both `With` overloads carry the
   claims. A claim on an operator with no row throws `InvalidOperationException` naming the operator, at registration and
   when the table is built; it is a configuration error and has no source position.
4. **The fragment parse stays session-free.** `ParseExpressionFragment` applies no library meaning by contract, so
   `Resolve` rewrites after the parse. It stays a call site into the same table, not a second seam.
5. **The fold is type-blind by design.** The parser has no types, so `Price + 3 Days` folds to a `DateOperation`; analysis
   (`DateOperationCheck`) rejects a left operand that is not a date ("temporal offset requires a date left operand").
   The match predicate moved verbatim.

## Consequences

- No baseline, golden or printed byte moved: the parse results are the same nodes.
- A pack that registers a claim on `*`, `<` or any other core binary operator gets it on that row with no parser edit.
- `DslExpressionParser(cursor, forms, folds, operators)` with an explicit `operators` table ignores the registry's claims
  (a claim-free parse that test code relies on); listed on #847, not changed here.
