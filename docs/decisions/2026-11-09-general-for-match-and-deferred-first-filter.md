# ADR: general `for`, expression `match`, and deferred `first`/`filter` (#409)

**Date:** 2026-11-09
**Status:** Accepted
**Answers:** #409 slice 1 — "ADR: use sites for entity/collection expressions".

## Context

#409 grew the expression/effect surface beyond the guard-shaped quantifiers, with each addition
gated on a product use site. It set the acceptance test for collection selection in its own
done-when:

> **`first` and `filter` are either shipped with a named consumer or explicitly deferred in the ADR.**

Slices 2–6 shipped general `for` and expression-level `match`. This record states what landed,
records the deferral the issue authorises, and does nothing else.

## What landed

### General `for` (slices 4–6)

`for x in Rel [where cond] { effects }` parses, prints back identically (`Write(Read(x))` is
idempotent), analyzes, lowers and executes in simulation and generated C#.

- `Rel` is a relationship name on the source entity; `x` binds each record. The optional `where`
  is an **inline expression** (unlike the fan-out form, whose predicate is a named policy or stage
  membership); a false `where` skips that record.
- The body runs once per surviving record in storage order. `invoke x.Action` targets the loop
  record. **Zero records is a no-op:** an empty relationship, or a `where` that matches none, runs
  nothing and the action succeeds (`2026-10-17-for-zero-records-is-a-no-op.md`).
- **Fail-fast:** the first failing body effect fails the whole `for`; later records are not
  visited. The simulator rolls back the failed action; generated C# keeps what earlier records
  already wrote.
- **Legality:** the body may write only the owning entity's own state. An `assign` through the
  binder or a relationship is another entity's state and is `DMEFF012`; mutate that entity with
  `invoke x.Action` instead.
- A `for` nested in a `for` body is a positioned parse error (no bound story for nested
  iteration). `create in` inside the body is not exercised by a test yet.
- **Lowering:** one `foreach` over the collection navigation, the same loop shape as the fan-out;
  `where` lowers to a negated conditional plus `continue`. No second iteration primitive.
- `DMEFF016` (`EffectForNotLowerable`) is retired from behaviour — general `for` no longer reports
  it; only the unused constant remains in the diagnostic table.
- The existing effect walkers descend into the body (invariants, topology, entity structure,
  subscriptions, the `DMREL002` unwritten-relationship warning), so the body is analyzed under the
  rules an action body gets.

### `match` (slices 2–3)

`match { cond -> value, ..., else -> value }` is an **expression with no scrutinee**: a guard
chain where the first true condition supplies the value.

- Arms are expressions, never effects, so a match works wherever an expression does (assign RHS,
  `if` conditions, policy bodies, `create` initializers, nested inside another `match`).
- **`else` is required and must be last**, so a match is always total; a missing, repeated or
  non-final `else` is a positioned parse error.
- Analysis: a non-Boolean condition is `DMMAT001`; arm values and the `else` must unify to one
  type (Text and an enum unify; Text and Number do not) — `DMMAT002`; two identical conditions are
  `DMMAT003` (the later arm is unreachable — overlapping ranges are the author's problem); a bare
  name that is no property, parameter, enum member or library identifier is `DMMAT004`.
- **Lowering:** a right-nested `Conditional` chain with the `else` value innermost, so there is no
  new VM primitive and simulation and printed C# share one conditional. The arm result is lazy;
  quantifier statements hoisted by an arm run up front.
- Not shipped: matching on a scrutinee, patterns, and folding of provably unreachable arms.

## Decision: `first` and `filter` are deferred

**Both are deferred because no product consumer has been named** in the repository or the issues.
#409 makes shipping conditional on exactly that — "only if a product use site exists" — and its
done-when accepts the deferral, so the deferral is the issue's own resolution, not a new ruling:

- `first Rel where cond` ("one related entity or null") has no consumer: nothing in the shipped
  surface takes an entity expression or compares one to null.
- `filter Rel where cond` ("the related subset") has no consumer: a collection value has no home —
  the shipped consumers are quantifiers over a relationship, not values.

#409's open decisions 3 and 4 (null semantics; the `filter` value shape) stay open for the same
reason and are not answered here. Whatever consumer is named first owns that decision.

## Decision scope

**This ADR adds no design decision beyond (a) recording what #409 shipped and (b) recording the
deferral #409 authorises.** It names no new consumer, changes no syntax, and rules on no open
question — not `first` null semantics, not `filter`'s value shape, not a scrutinee form of `match`.

## Consequences

- The guide (`Poly.Mcp/Docs/poly-dsl-guide.md` §6 and §8) documents the shipped `for` and `match`
  subset and lists `first`/`filter` as missing; it remains the authoring reference.
- `DMEFF016` stays declared but unreported; remove it with the next change that touches that table.
- `docs/CORE.md` documents pipeline machinery, not the expression surface; no line there is made
  false or incomplete by `MatchExpr` or `ForEachEffect` (checked 2026-11-09). No CORE change is
  owed by this ADR.
