# Key members are jointly unique; generated C# gains a typed `Key` (Q19, #573, #405)

Status: **transcription of an owner ruling already made 2026-11-11; this file adds no new decision.**
Recorded by slice F-01 of theme F ([#690](https://github.com/scoizzle/Poly/issues/690)) before the slices
that implement it. The ruling is quoted verbatim below; prose between quotes is this file's framing only.
A clause the ruling does not settle appears under *Open* rather than being filled in.

This file records the **enforcement and C# half** of key designation. The designation itself - `key(a, b)`
as a compile-time designation over existing members, the header-clause placement, the admission rules -
is [2026-11-09: domain identity is a compile-time key designation](2026-11-09-domain-identity-keys.md) and
is not re-decided here.

## Context

[#405](https://github.com/scoizzle/Poly/issues/405) recorded key-as-designation on 2026-11-06 and shipped
it as analysis metadata only: nothing was enforced at runtime and no surface form appeared.
[#573](https://github.com/scoizzle/Poly/issues/573) is the C# face of that gap: a `key(City)` entity
generates no key-based equality, its `Create` parameters are not key-first, and two entities with the same
key value are both creatable because identity is object reference. The owner ruled on both halves
together.

## Decision

The ruling is the Q19 row of the 2026-11-11 owner rulings on epic [#659](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)
(`## Rulings — 2026-11-11`, [comment 6801963063](https://github.com/scoizzle/Poly/issues/659#issuecomment-6801963063)):

> `key(...)` members are jointly unique, enforced like `unique` (needs a storage library per Q8);
> mutability deferred (members stay assignable); generated C# keeps reference identity and gains a typed
> `Key` value used for lookups, storage port and routes.

In the ruling's own clauses:

- `key(...)` members are **jointly unique** - the tuple, not each member separately.
- Enforcement is **like `unique`**, and it **needs a storage library** (per Q8).
- **Mutability is deferred**: key members **stay assignable**.
- Generated C# **keeps reference identity** (entity equality is not rewritten to value equality).
- Generated C# **gains a typed `Key` value**, used for **lookups**, the **storage port** and **routes**.

Q8, referenced by the ruling, is: one `DomainSession` rule decides the library set, and "a constraint no
loaded library compiles is a positioned error naming the library to add" (Q8 on the same ruling page).
OQ-1 fixes the implicit set at `[storage, persistence]` for sessions that load the documented set
([#659 implementation-plan rulings](https://github.com/scoizzle/Poly/issues/659#issuecomment-6805267269)).

## Consequences

### Failure shape

A duplicate key tuple is a failed `DomainResult` of kind `unique`, raised by the module through the
storage port, identical in the simulator and generated C# (F-12's done-when; Q1's atomicity ruling on the
same page means the state is unchanged on failure).

### Language surface

- No new syntax: `key(...)` is already parsed, printed and analysed as the entity header clause. This
  ruling makes the designation **authoritative at create and assign**; it adds no keyword.
- Storage: `Entity.KeyProperties` becomes public and the key maps to a unique storage index - the
  `StorageEntity.KeyIndex` twin P emits as a DB unique index (theme F *Contracts*, `KeyConstraint`
  provider F-12, consumer P-12; `TypedKey` provider F-13).
- Generated C#: a nested `readonly record struct Key` with members in key declaration order and a `Key`
  property on instances; **entity equality stays reference** (F-13). `R`'s storage port and `P`'s routes
  take `Key`.
- #573's "`key` is not first in `Create`" complaint is answered by **Q12** (declaration order everywhere),
  not by this ruling: F-13's red test asserts `Create` parameter order follows declaration and does not
  force the key first.

### Relation to the 2026-11-09 ADR

That file records, from the 2026-11-06 decision, admission rules that include *"immutable"* properties
and *"a keyed entity's key cannot be reassigned in v1 (a future key edit is a re-index, not a silent
move)"*. This ruling (2026-11-11, later, same owner) says "mutability deferred (members stay assignable)".

Open: whether "members stay assignable" supersedes the earlier "immutable" / "cannot be reassigned in v1"
admission clause, and what an assign that produces a colliding tuple does to the stored state - decided
in slice F-12, whose red test expects a colliding assign to fail with the state unchanged.

### Slices that implement it

| Slice | What it lands | Source |
|---|---|---|
| F-12 | Key enforcement: key members jointly unique, module-owned, needs a storage library; advances [#573](https://github.com/scoizzle/Poly/issues/573) and [#405](https://github.com/scoizzle/Poly/issues/405) | theme F slice table |
| F-13 | Typed `Key` value on generated C#, reference identity retained; closes [#573](https://github.com/scoizzle/Poly/issues/573) | theme F slice table |
| F-14 | Keys through navigations: link / unlink / reparent maintenance; closes [#405](https://github.com/scoizzle/Poly/issues/405) | theme F slice table |

Analysis-side consequences named in the theme plan: `C-05` (`ConstraintCoverageDiagnostic`) reports a key
that no loaded library covers, `R-05` (`KeyCodec`) carries the key through the port, and `R-08`
(`ActionScope`) scopes the guard to the action. OQ-8 requires **a unique-atomicity test for every `unique`
and every `key` property** in generated tests, so F-12's enforcement is covered from Q as well as from the
language-surface tests.

### Open

- **Navigation members and recursive keys.** The ruling says `key(...)` members are jointly unique; the
  plan puts key members that are navigations (and the recursive resolution across them) in F-14. Open:
  whether F-12's guard covers own-property tuples only - decided in slice F-14.
- **Placement of the typed `Key`.** Nested in the entity versus top-level is not in the ruling text; the
  theme plan's contract spells a nested struct. Open: nested versus top-level placement, and the naming
  of the type - decided in slice F-13 (SD6).
- **Scoped uniqueness.** `#405` also carries scoped `unique_by`; the ruling does not mention a scope, and
  the theme plan keeps scoped uniqueness out of #405's closure. Open: nothing - scoped `unique_by` stays
  outside this ruling and outside F-12 / F-13 / F-14 (theme F plan, #405 non-goal).
- **#405 acceptance criteria beyond the ruling.** The issue's "<5% EF create overhead" and "`EXPLAIN QUERY
  PLAN` uses the index" are acceptance checks, not decision clauses. Open: whether they are retained as
  F-14's acceptance - decided in slice F-14 (SD6).

## What this file does not decide

Nothing here creates a constraint, a storage index, a C# type or an equality rule. It records the ruling
and names the slices; the mechanisms do not exist yet, so `docs/CORE.md` is deliberately untouched.
