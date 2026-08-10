# C# export bugs in `create in Rel` lowering

**Date:** 2026-08-10  
**Status:** Findings + fixes. **Finding 1 (CS1501 arity) FIXED 2026-08-10** — call site now skips collection navs to match the CreateNav signature (guard test `Export_CreateInTargetWithCollectionNavs_SignatureMatchesCallArity`; verified by compiling the exact repro). **Finding 2 (auto-wire) remains open** — DSL guide §0.3 corrected to stop overclaiming; derived back-ref materialization is planned (ADR 2026-08-10). Finding 3 (CS8618) hygiene debt.  
**Origin:** Dogfood pass — modeled a realistic domain (`domain Orders`) via `apply_dsl`, exported via `export_domain_to_csharp`, and compiled the export. The export does **not** compile.

---

## Repro

```poly
Customer: entity {
  orders: many Order
  CheckOut: action (book: Text) -> Order {
    create in orders { Title: book }
  }
}

Order: entity {
  Title: Text required
  Total: Number range(0, )
  customer: Customer
  lines: many OrderLine
  notes: many owned Note
}

OrderLine: entity { Sku: Text required }
Note: entity { Body: Text }
```

The target of `create in Rel` (`Order`) has its own `many` navigation properties (`lines`, `notes`).

Generated C#:

```csharp
private Order CreateOrders(string title, long total, Customer customer)   // 3 params
{
    var orderResult = Order.Create(title, total, customer,
        new List<OrderLine>(), new List<Note>());                          // 5 args — matches
}
...
public DomainResult<Order> CheckOut(string book)
{
    ...
    return DomainResult<Order>.Success(this.CreateOrders(book, 0L, null, null, null)); // 5 args — CS1501
}
```

```text
error CS1501: No overload for method 'CreateOrders' takes 5 arguments
```

## Finding 1 — 🔴 Compile-breaking drift: call site vs CreateNav signature disagree on collection navs

The call site (`EffectLoweringPass.CreateEntityInRelationship`, `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:391-400`) emits **one arg per non-backref constructor param**, including collection navs (`null` via `DefaultForDomainType`).

The exporter's CreateNav factory (`DomainToCSharpExporter.AddCreateNavMethod`, `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:716-723`) emits **no method param** for collection navs — it hardcodes an empty `List<T>` in the body.

Both iterate the same `EntityStructureMetadata.ConstructorParameters`, but apply **different conventions for collections**. The export only compiles when the `create in` target has **no** collection navs — which is why existing export tests are green. This is the CS7036/CS1501 class of drift the `EntityStructureMetadata` monopath was meant to prevent; collection navs are the untested corner.

**Fix direction:** make both consumers agree — either drop collection navs from the call site's arg list (matching the exporter), or emit them as params in the CreateNav signature. One shared bag (e.g. the excluded-param set published on `EntityStructureMetadata`) should drive both.

## Finding 2 — 🟠 `create in Rel` auto-wire never fires for cross-entity relationships

The DSL guide (Poly.Mcp/Docs §0.3) claims:

> When you create an entity in a navigation property, the source entity reference is automatically set. … The generated C# sets `borrower` to `this` automatically.

That is dead code for cross-entity relationships. `EntityStructureAnalyzer.ComputeConstructorParameterOrder` (`Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:136-139`) marks `IsBackReference: true` **only when source == target** (self-relationship). For the standard `create in orders { ... }` case, the target's back-ref nav (`Order.customer`) is a plain singular nav:

```csharp
private Order CreateOrders(string title, long total, Customer customer)   // 'customer' is a real param
...
Order.Create(title, total, null, ...)                                     // call site passes null
```

Result: the created `Order.Customer` is `null`, not `this`. The guide's own `create in loans { book: book }` example would compile but leave `borrower` null.

**Fix direction:** a real back-reference detection — a nav on the target entity whose relationship source is the *creating* entity — so both `AddCreateNavMethod` (emit `this`) and `CreateEntityInRelationship` (skip the arg) agree. `IsBackReference` currently encodes "self-relationship", not "back-reference of the relationship being created".

## Finding 3 — ⚪ CS8618 on EF-materialization ctors (pre-existing)

Param-less ctors (`// EF materialization.`) leave non-nullable props (`Email`, `Name`, `Title`, `Sku`, `City`, …) uninitialized → 8 `CS8618` warnings on every export. Not a compile error, but noisy.

## Non-issues confirmed

- `IsDeleted`, `internal set`, stage-enum + `CurrentStage`, subscription registration (`Register…Subscriber` / `Notify…Subscribers` / `When…`) all emit coherently.
- The double `this.Status = OrderStatus.Active;` in `Submit` is faithful — the action assigns `Status` and the `entry` effect assigns it again. Not a bug.

---

## Proposed follow-ups

1. Fix Finding 1 (compile error) first — smallest change; add an export test with a collection-nav target.
2. Fix Finding 2 (auto-wire) — same `CreateEntityInRelationship`/`AddCreateNavMethod` seam; add a test asserting the created instance's back-ref.
3. Either suppress CS8618 on materialization ctors or accept as hygiene debt.
4. Update the DSL guide if the auto-wire claim is deliberately not implemented (i.e. pick truth over the guide).
