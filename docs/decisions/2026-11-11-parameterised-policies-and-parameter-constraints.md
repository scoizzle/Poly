# Parameterised policies and parameter constraints (Q18, #503, #504)

Status: **transcription of an owner ruling already made 2026-11-11; this file adds no new decision.**
Recorded by slice F-01 of theme F ([#690](https://github.com/scoizzle/Poly/issues/690)) before the slices
that implement it. The ruling is quoted verbatim below; prose between quotes is this file's framing only.
A clause the ruling does not settle appears under *Open* rather than being filled in.

## Context

Two authoring gaps made a parameter-dependent rejection impossible. [#503](https://github.com/scoizzle/Poly/issues/503)
shows a constraint on an action parameter (`qty: Number range(1, 100)`) rejected with the misleading
`Expected parameter name, got 'range'`, because parameter constraints were not part of the syntax.
[#504](https://github.com/scoizzle/Poly/issues/504) shows the consequence: a `require` may only name a
policy, a policy cannot see action parameters, and an `if` with no failing branch reports
`succeeded=true` - so a rejected business rule (double booking) is a silent success. The owner ruled on
both halves together.

## Decision

The ruling is the Q18 row of the 2026-11-11 owner rulings on epic [#659](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)
(`## Rulings — 2026-11-11`, [comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)):

> Parameterised policies (`CanWithdraw(amount: Number): policy { ... }`, `require CanWithdraw(amount)`);
> action parameters accept the same constraints as properties, checked before the action runs.

In the ruling's own clauses:

- A **policy may take parameters**: `CanWithdraw(amount: Number): policy { ... }`, called as
  `require CanWithdraw(amount)`.
- **Action parameters accept the same constraints as properties.**
- Those constraints are **checked before the action runs**.

## Consequences

### Failure shape and ordering

A parameter or policy rejection is a failed `DomainResult`, not an exception, and is reported through the
typed failure record (Q3 on the same ruling page). "Checked before the action runs" fixes the ordering:
a violating argument fails before **any** effect, so an action cannot half-write and then report a
constraint failure. Theme F's risk note R1 records that both slices emit their guard into L's action-body
builder and report through R's `FailureLowering` (`R-04`), so the ordering is structural, not per-host.

### Language surface

- Parameter constraints use the property constraint vocabulary and round-trip through the DSL printer;
  the misleading `Expected parameter name, got 'range'` diagnostic is gone (F-08's done-when).
- A policy call carries argument expressions: `Policy.Parameters` holds the declared parameters,
  `Policy.Arguments` the call-site expressions, and `PolicyInstantiation.Instantiate(policy, args)`
  substitutes them so analysis reasons about the guard (theme F *Contracts*, `ParameterisedPolicies`,
  provider F-09; `ActionParameterConstraints`, provider F-08).
- Unparameterised policies keep today's spelling and behaviour (F-09's done-when: "unparameterised
  policies unchanged").

### Slices that implement it

| Slice | What it lands | Source |
|---|---|---|
| F-08 | Constraints on action parameters; closes [#503](https://github.com/scoizzle/Poly/issues/503) | theme F slice table |
| F-09 | Parameterised policies and `require P(args)`; closes [#504](https://github.com/scoizzle/Poly/issues/504) | theme F slice table |

F-09 depends on F-08 in the plan, and F-08's own dependency list includes F-12. Consumers named in the
theme plan: V validates, L guards before any effect, P builds DTO validation from
`Action.Parameters[i].Constraints`, D emits the schema.

### Open

- **Allowed constraint kinds, and `optional` / `default` on a parameter.** The ruling says "the same
  constraints as properties" without enumerating them, and does not say whether `optional` and `default`
  are legal on a parameter. Open: the allowed kinds (range, length, pattern, enum, equals) and whether
  `optional` / `default` are rejected - decided in slice F-08 (SD4).
- **How a parameterised policy is evaluated.** Method call versus macro substitution at resolve time, and
  which name wins when a policy parameter shadows an entity property, are not in the ruling text. Open:
  the evaluation form and parameter shadowing - decided in slice F-09 (SD5).
- **Stage `require` of a parameterised policy, and the old require-only-policy rule.** A stage `require`
  has no arguments to bind. Open: whether that is a positioned error and what it does to the
  require-only-policy rule - decided in slice F-09 (SD5).

## What this file does not decide

Nothing here adds a keyword, a policy parameter, a constraint kind or a diagnostic. It records the ruling
and names the slices; the mechanisms do not exist yet, so `docs/CORE.md` is deliberately untouched.
