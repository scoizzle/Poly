# A `for` inside a subscription body emits a handler that does not compile

**Date:** 2026-10-17
**Status:** defect, open. Not on the board `CURRENT` — recorded as evidence, nothing admitted.
**Found while:** the empty-set `for` slice (branch `fix/for-empty-set`); the slice did **not** fix it.

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
      for lines as line invoke line.Mark
    }
  }
}
```

Analysis accepts it (`HasErrors == false`). The **export does not compile**:

```
(156,21): error CS0127: Since 'Order.WhenEachLineActive()' returns void, a return keyword must not be followed by an object expression
```

Confirmed on clean `origin/master` (`b40e694b`) with no other change applied, so it pre-dates the
empty-set slice.

## Mechanism

`Poly/DomainModeling/Lowering/EffectLoweringPass.cs`, `ForEachInvoke`, emits a per-item failure
return (`if (!result.IsSuccess) return <caller failure>;`). Inside a *subscription* body the tree is
emitted into a **`void`** handler (`DomainToCSharpExporter`, the notify/`when` path emits
`void When{Quantifier}{Target}{Stage}()`), so the `return DomainResult...` is invalid C#.

The same class of hazard applies to a zero-record failure — which the 2026-10-17 empty-set slice
removed from this effect, so the shape now has one offending return instead of two, not zero.

## What a fix has to decide

A subscription handler has no result channel. Two honest shapes:

1. **Reject the shape at analysis** until a handler can carry a failure (fail closed, tenet 5: do not
   ship a known-wrong shape). Cheapest, and consistent with the parser/analyzer's fail-closed style
   elsewhere for `for`.
2. **Give the handler a failure path** — e.g. the handler records the failure on the subscriber (a
   diagnostic property) or throws, matching whatever the notify path already does for a failing
   subscription effect.

Note the *interpreter* may execute this shape successfully (the handler body runs as ontology
effects), so option 1 is a **capability removal** for the simulator path while option 2 keeps it —
that choice is the reason this is written down rather than fixed in passing.

## Where it is not

- Not the empty-set slice: `EffectLoweringPass.ForEachInvoke`'s zero-check is gone
  (`a965d287`), and this failure is a *per-item* return.
- Not covered by any test or golden today — no probe, golden or dogfood domain puts a `for` inside a
  `when` body, which is why it survived.
