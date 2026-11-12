# Checked arithmetic region (Q6, #674, #624)

Status: **transcription of an owner ruling already made 2026-11-11; this file adds no new decision.**
Recorded by slice F-01 of theme F ([#690](https://github.com/scoizzle/Poly/issues/690)) because the ruling
had to reach `docs/decisions/` before the slices that implement it. The ruling is quoted verbatim below;
prose between quotes is this file's framing only. A clause the ruling does not settle appears under
*Open* rather than being filled in.

## Context

`Number` arithmetic wraps silently at 64 bits while `Decimal` arithmetic throws: [#624](https://github.com/scoizzle/Poly/issues/624)
reports `Big + 1` on `long.MaxValue` succeeding with `-9223372036854775808` in both the simulator and the
generated C#, and `Decimal` overflow escaping as an `OverflowException` instead of a `DomainResult`
failure. The owner had to choose between failing every overflow closed and keeping C# parity with an
opt-in region; [#674](https://github.com/scoizzle/Poly/issues/674) asks for the language feature.

## Decision

The ruling is the Q6 row of the 2026-11-11 owner rulings on epic [#659](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)
(`## Rulings — 2026-11-11`, [comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)):

> New `checked` region (like C#): overflow inside returns an arithmetic failure, outside it wraps;
> Number / Number is integer division (+ warning when assigned to Decimal); Decimal -> Number rounding
> only when analysis proves it safe, else an error.

In the ruling's own clauses:

- A `checked` region is a **new language feature**, modelled on C#'s checked context.
- **Inside** the region, arithmetic overflow **returns an arithmetic failure**.
- **Outside** the region, arithmetic **wraps**.
- `Number / Number` is **integer division**, with a **warning** when the result is assigned to a `Decimal`.
- `Decimal -> Number` **rounding happens only when analysis proves it safe**; otherwise it is an **error**.

## Consequences

### Failure shape

The overflow failure is a failed `DomainResult`, never an exception, and carries kind `arithmetic` on the
typed failure record (Q3 on the same ruling page; #674's *Interactions* says the same). It is not a new
failure mechanism: it reaches hosts through the typed-failure lowering slice `R-04` (theme R) like
every other guard.

### Language surface

- The DSL spells the region as an **effect region**, `checked { ... }` (#674's *Scope*: grammar table
  entry, parser, printer, guide section, and a fail-closed case for malformed use, all in lockstep).
- C# parity is spelled `checked(e)` and `checked { }`; the Syntax layer carries it as
  `CheckedExpression(Node Operand)` and `CheckedBlock(Block Body)` with a VM
  `Context.CheckedArithmetic` flag (theme F *Contracts*: `CheckedExpression` provider F-06, consumer
  L-18; `CheckedEffect` provider F-07, consumer T-11).
- `Number / Number` integer division and the Decimal-assignment warning are **not** this ruling's
  implementation surface: they belong to the numeric model theme (SD16 in the theme F plan; the `%`,
  Round and Duration slices F-05, F-10 and F-11 follow whatever that model decides). This file records
  the clause so the numeric slices are held to it.

### Slices that implement it

| Slice | What it lands | Source |
|---|---|---|
| F-06 | Syntax layer: `CheckedExpression` / `CheckedBlock` nodes, VM checked arithmetic, C# `checked` read/write, `NodeFacts` rows and `CompileNodeInner` arms in lockstep | theme F *Contracts*, slice detail F-06 |
| F-07 | `checked { ... }` DSL region end to end; closes [#674](https://github.com/scoizzle/Poly/issues/674) and [#624](https://github.com/scoizzle/Poly/issues/624) | theme F slice table |

F-07 is also where the ruling's failure kind is proven: an overflow inside the region returns kind
`arithmetic` in the simulator and in generated C#, and the same arithmetic outside the region wraps in
both.

### Open

- **Expression-form region in the DSL.** The ruling says "checked region" without fixing whether the DSL
  also spells an expression form. The plan lists the effect region and leaves the expression question
  open (SD2). Open: whether the DSL also spells `checked(e)` in addition to the effect region - decided
  in slice F-07.
- **Region placement and nesting.** Whether the region is legal outside an action body (policy bodies,
  defaults, stage entry effects) and whether a nested region is a positioned error are not in the ruling
  text; the plan's fail-closed case covers the malformed shape only. Open: the legal positions and the
  nesting rule - decided in slice F-07.
- **Decimal inside a region.** `Decimal` does not wrap at all (OQ-6: `ExactNumber` is decimal-backed and
  saturates, never throws, never wraps), so "outside it wraps" has no Decimal reading. Open: whether a
  Decimal overflow fails identically inside and outside the region - decided in slice F-07.

## What this file does not decide

Nothing here creates a node, a keyword, a failure kind or an analysis rule. It records the ruling and
names the slices; the mechanisms do not exist yet, so `docs/CORE.md` is deliberately untouched.
