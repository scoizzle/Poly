# An entity may designate its initial stage (Q21, #675)

Status: **transcription of an owner ruling already made 2026-11-11; this file adds no new decision.**
Recorded by slice F-01 of theme F ([#690](https://github.com/scoizzle/Poly/issues/690)) before the slices
that implement it. The ruling is quoted verbatim below; prose between quotes is this file's framing only.
A clause the ruling does not settle appears under *Open* rather than being filled in.

## Context

An entity's initial stage is implicit today: it is the first declared stage, and every consumer reads it
as `Stages[0]` - the theme F plan found seven such sites across lowering, analysis, the executor, the MCP
runtime tool, generated tests and the HTTP create response. [#675](https://github.com/scoizzle/Poly/issues/675)
asks for an explicit designation, and the earlier codegen review
([`docs/reference/research/2026-10-10-codegen-review.md`](../reference/research/2026-10-10-codegen-review.md),
finding G5) had already asked for one. Two other rulings lean on the answer: Q20 (a property assigned by
the initial stage's entry effect cannot be supplied on create) and the create path in every host.

## Decision

The ruling is the Q21 row of the 2026-11-11 owner rulings on epic [#659](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)
(`## Rulings — 2026-11-11`, [comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)):

> An entity may explicitly designate its initial stage; if none is designated the first declared stage is
> initial (today's implicit rule).

In the ruling's own clauses:

- **Explicit designation is allowed** - an entity *may* name its initial stage.
- **The default is unchanged**: with no designation, **the first declared stage is initial**, which is
  today's implicit rule.

The second clause is what makes this a non-breaking change: no model that exists today changes behaviour.

## Consequences

### One accessor before any new surface

F-02 replaces every `Stages[0]` read with a single `EntityStages.InitialStage` accessor, with no behaviour
change, so the rule lives in one method rather than seven call sites. Its consumers are named in the theme
plan: L (constructor start stage, create-in notify), S, X, M (`RuntimeTool`), Q (generated tests) and P
(the create response). F-02 is what lets the later slices rebase early.

### Language surface

- An explicit designation on the entity; `Entity.InitialStageName` carries it, and `S-14`, `D-04` and
  `Q-12` consume it (theme F *Contracts*, `EntityStages.InitialStage` provider F-02, `Entity.InitialStageName`
  provider F-03).
- The designation is honoured **on create in the simulator, generated C# and HTTP**, and it round-trips
  through the DSL printer (F-03's done-when).
- Fail-closed: [#675](https://github.com/scoizzle/Poly/issues/675)'s *Scope* names the two bad shapes - a
  second designation, and a designation naming an unknown stage - as positioned errors. That scope is the
  issue's, recorded here as its statement, not as an addition to the ruling.
- Q20 interaction: because the entry effects of the **initial** stage decide which properties cannot be
  supplied on create, "initial" must mean the designated stage everywhere.

### Slices that implement it

| Slice | What it lands | Source |
|---|---|---|
| F-02 | Initial-stage accessor: every `Stages[0]` replaced by `EntityStages.InitialStage`, no behaviour change | theme F slice table |
| F-03 | Explicit initial-stage designation `initial(Stage)`; closes [#675](https://github.com/scoizzle/Poly/issues/675) | theme F slice table |

F-03 depends on F-02 and on the G-00a / G-00b syntax-seam slices in the plan.

### Open

- **Spelling of the designation.** The ruling says an entity "may explicitly designate" its initial stage
  without fixing the syntax. Open: a header clause `initial(Stage)` mirroring `key(...)` versus a stage
  marker - decided in slice F-03 (SD1).
- **Where a duplicate designation is caught.** The ruling says nothing; #675's scope asks for a positioned
  error and the plan leaves parse-time versus analysis open. Open: the layer that reports the second
  designation - decided in slice F-03 (SD1).
- **What "first declared" means after a change list.** The ruling names declaration order; how a stage
  added by an evolution change interacts with the designation is not stated. Open: the ordering rule for
  an entity whose stages arrive through more than one declaration - decided in slice F-03.

## What this file does not decide

Nothing here adds a keyword, an accessor, a diagnostic or an ordering rule. It records the ruling and
names the slices; the mechanisms do not exist yet, so `docs/CORE.md` is deliberately untouched.
