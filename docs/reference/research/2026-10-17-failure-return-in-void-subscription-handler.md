# A failure return inside a `when` body emits into a `void` handler and does not compile

**Date:** 2026-10-17
**Status:** defect, open. Not on the board `CURRENT` — recorded as evidence, nothing admitted.
**Found while:** the empty-set `for` slice (branch `fix/for-empty-set`); the slice did **not** fix it.
**Scope:** any failure-return lowered into a subscription handler — `for` **and** plain `invoke`.

## Repro

```poly
domain Test
Line: entity {
  Qty: Number
  Mark: action { assign Qty to Qty }
  Active: stage { }
}
Order: entity {
  lines: many Line
  Ready: stage {
    when lines Active {
      for lines as line invoke line.Mark   # variant A
    }
  }
}
```

Variant B is the same domain with `invoke Mark` instead of the `for` — and the *recommended*
subscriber style is to invoke from a `when` body, so B is the shape a domain is most likely to have.

Analysis accepts both (`HasErrors == false`). The **export does not compile**:

```
(156,21): error CS0127: Since 'Order.WhenEachLineActive()' returns void, a return keyword must not be followed by an object expression
```

Both variants reproduced on clean `origin/master` (`b40e694b`) with no other change applied, so the
defect pre-dates the empty-set slice. (Variant A was confirmed by the slice's own test attempt;
variant B by `scripts/run-probe.sh` on a scratch probe, 2026-10-17.)

## Mechanism

`Poly/DomainModeling/Lowering/EffectLoweringPass.cs` lowers a per-item failure as
`ReturnCallerFailure*` (`:723-730`), whose result type comes from
`CallerResultType = _context.ActionResultType ?? new TypeReference("DomainResult")` (`:720-721`).

- On the **runtime** path `ActionResultType` is deliberately omitted, and the doc on `CallerResultType`
  (`:712-718`) says so: the untyped `DomainResult.Failure` is what the interpreter fail-fasts on
  via `ExecuteEffect`.
- In a **subscription body** the export builds a `LoweringContext` with no `ActionResultType` and
  emits the handler as `void When{Quantifier}{Target}{Stage}()` (the notify path in
  `DomainToCSharpExporter`), so the same helper produces `return DomainResult.Failure(...)` inside a
  `void` method — invalid C#.

So `ActionResultType is null` means two different things (runtime: fail-fast untyped return; void
export: no return channel), and the lowering cannot tell them apart.

## The precedent a fix should follow

`AssignConstraintFailure` (`EffectLoweringPass.cs:443-455`) already implements exactly this split,
and documents it: *"`return DomainResult.Failure` when the caller has a result type; **throw
`ConstraintFailureException`** in void export contexts (ctor / stage entry) — same split as create-in
fail-closed."* It ships: the goldens show a throw inside a subscription handler
(`Patron.cs.golden`, `WhenEachLoanOverdue`), and the runtime catches it
(`DomainEntityInstance.cs:756`).

So the missing piece is *not* a decision about how a handler should report failure — the channel
exists. The missing piece is a way for the lowering to know it is in a void context: the null
`ActionResultType` is load-bearing for the runtime path, so a fix adds an explicit void-context
signal to `LoweringContext` and routes `ReturnCallerFailure*` through the same split
`AssignConstraintFailure` uses. That would fix `for` and `invoke` together.

Rejecting the shape at analysis instead would remove capability from the interpreter (which executes
this shape today) while leaving the `invoke` case broken — the worse of the two.

## Why it survived

No probe, golden or dogfood domain puts a failure-capable effect inside a `when` body: the golden
handler bodies hold only assigns and creates. The empty-set slice removed one of variant A's two
offending returns, not the hazard.
