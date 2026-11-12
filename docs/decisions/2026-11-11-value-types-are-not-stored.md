# Value types are not stored (#406 decision 2 / #677 SD11)

Status: **decision recorded by slice F-22 of theme F
([#690](https://github.com/scoizzle/Poly/issues/690)) 2026-11-11.** Unlike the transcription ADRs of the
same date, this slice *makes* the decision the epic left open: [#677](https://github.com/scoizzle/Poly/issues/677)
**SD11** lists "stored value types" as `(ADR rejects, planned)`, and [#690](https://github.com/scoizzle/Poly/issues/690)'s
F-22 row says the slice records the decision "or, if the owner decides otherwise, a separate epic is filed".
The decision below is that planned default. The owner-override path appears under *Open*; nothing in this
file adds a new storage shape.

## Context

A domain `value` type is a named record **without identity or lifecycle**. Today it is legal exactly in the
payload positions - a contract payload, an action parameter
([`Poly.Mcp/Docs/poly-dsl-guide.md`](../../Poly.Mcp/Docs/poly-dsl-guide.md) §2, *Value types*) - and illegal
as a stored entity property. Three fail-closed gates enforce the illegal shape:

| Shape | Gate | Code | Evidence |
|---|---|---|---|
| `value` declared before the entity that stores it | analysis | `DMSTR006` | `Poly.Tests/DomainModeling/Analysis/StoredValueTypePropertyTests.cs` - `ValueTypeProperty_OnEntity_IsAnalyzeError`, `ValueTypeProperty_OnEntity_FailsEvolutionClosed` |
| entity declared **before** the `value` it stores | resolver, positioned | the property message below | `StoredValueTypePropertyTests.ValueTypeProperty_DeclaredAfterEntity_IsPositionedErrorNamingTheStorageDecision` |
| contract-declared `value` stored on an entity | analysis | `DMCON001` | `Poly.Tests/DomainModeling/Analysis/InternalDomainResolutionTests.cs` - contract-integration cases |
| model-API-built relationship whose target is a `value` | analysis | `DMSEM003` | `Poly.DomainModeling.Analysis.DomainCatalogPass` - unreachable from the DSL, which fails earlier |

[#406](https://github.com/scoizzle/Poly/issues/406) *Decisions needed* #2 asks whether to keep the rejection
or add an owned/JSON projection; [#205](https://github.com/scoizzle/Poly/issues/205) (slice T5) already built
the rejection, and this ADR answers the question it left open.

The two DSL-visible orderings used to disagree about *why*: a `value` declared first reached `DMSTR006`,
while an entity declared first queued its `Bill: Money` line as a navigation and failed with a message
claiming the name was "neither an entity in this domain nor a value type" - false, because the value type
exists by the time deferred resolution runs. F-22 corrects that message to name the storage decision, so
both orderings report one rule in one voice.

## Decision

**A value type is a contract payload or action parameter and is never stored on an entity.**

- Storing one - as a property, a navigation target, an owned member or a create initializer field - fails
  closed with a positioned message that names the decision.
- Stored state is a **nested entity** (`owned` / `many`), which has identity and lifecycle, or the value
  travels as an **action parameter** / **contract payload**.
- The rejection is a rule of the ontology, not of one host: the same doctrine holds in the simulator,
  generated C#, the DbContext and the HTTP surface.

## Rejected alternatives

- **(a) EF owned / complex-type projection.** An owned type is a second storage shape with no consumer
  today, and mapping it costs EF configuration, migrations and path-prefix support that
  [#406](https://github.com/scoizzle/Poly/issues/406) puts out of scope for this theme. It would also give a
  value type an implicit identity by table, contradicting "no identity".
- **(b) A JSON column.** Serialising a value type into one column loses the per-field `range`, `length`,
  `pattern` and `enum` constraints - and the DB CHECK emission - that the storage port relies on, and makes
  `unique` / `key` over its fields impossible. The projection would accept today and fail at work time,
  which is the failure mode the rejection exists to prevent.
- **(c) One table per value type.** A table with a key *is* an entity; the ontology's "no identity" clause
  would be fiction, and the DSL's two concepts would lower to the same artifact.
- **(d) Keep rejecting without a record.** That is the state before this slice: the behaviour ships and the
  *why* is unrecorded, so the next reviewer re-opens the projection question. Recording the decision is the
  smallest change that ends the re-litigation.

## Consequences

- **The gates above stay.** `DMSTR006` and `DMCON001` are not duplicates and must not be consolidated:
  different owners (same-domain value type vs contract-declared type), different remedies ("nested entity or
  parameter" vs "parent-domain types"), and their error-count assertions are independent. `DMSEM003` stays as
  the model-API gate.
- **Declaring order is not a semantic difference.** Both orderings reject; the resolver's message is
  positioned, `DMSTR006`'s is not, because `Diagnostic` carries a `Node` and `Node` has no source span. This
  asymmetry is recorded here rather than papered over; unifying the two belongs to the parser rewrite that
  gives `Node` a span, not to this slice.
- **Value fields stay unreadable in policies and path expressions** (`bill Amount`), per the guide's §2
  *Value types* note. Moving a value shape into behaviour means binding it as an action parameter.
- **Later slices inherit the constraint.** F-21 (pure functions and guards over value types) and F-23 (member
  `GetOperator`) add surface only; neither gains storage, and neither may introduce a stored-member path.
- **`docs/CORE.md` is untouched**: the mechanism exists and is already mapped; this file records why.

## Open

- **Owner override.** If the owner decides value types should be storable, that is a **new epic**, not a
  change here: EF mapping, migrations, path-prefix expressions and `--dbms` CHECK emission for the projected
  shape, plus the diagnostic removals. #690's F-22 row names exactly this escape ("or, if the owner decides
  otherwise, a separate epic is filed"); no such epic exists as of 2026-11-11.
- **Span-carrying diagnostics.** Whether `DMSTR006` ever becomes positioned depends on the AST span work, not
  on this decision.
