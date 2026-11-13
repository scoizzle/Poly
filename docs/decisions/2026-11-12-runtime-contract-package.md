# ADR: The runtime contract package - kind→status table, concurrency token (#677 R-06)

## Status

**Accepted** - slice R-06 of epic [#677](https://github.com/scoizzle/Poly/issues/677), theme R
([#682](https://github.com/scoizzle/Poly/issues/682)), plan lock 2026-11-12
([comment 6817868394](https://github.com/scoizzle/Poly/issues/682#issuecomment-6817868394)).
One clause is an **orchestrator ruling the owner may veto**: `Cascade` → 500 (O-4, below).

## Context

R-02 landed the closed failure vocabulary `Poly.Runtime.DomainFailureKind` and the typed
`DomainFailure` record. Two things were still missing before a host could consume a failure
without guessing:

- **No kind→status table.** The generated host reaches 409 by *parsing failure text*:
  `MinimalApiGenerator.Host.cs:34-53` emits `FailureStatus` as
  `message.Contains("Unique constraint") || message.Contains("already has a target") ? 409 : 400`.
  Anything else is 400, whatever its kind.
- **No concurrency token.** Q2 makes a conflicting save a 409, but nothing in `Poly.Runtime`
  names the optimistic token that a generated entity would expose.

R-06 ships the contract both consumers bind. It changes no generator: the text parser above stays
until **P-14** deletes it.

## Decision

### 1. One table: `Poly.Runtime.DomainFailureStatus.Http(DomainFailureKind) : int`

| Kind | Status | Authority |
|------|--------|-----------|
| `Unique` | 409 | Q2: the persisted unique index is the final authority for a duplicate key. Today's text parser already returns 409 for `"Unique constraint"`. |
| `Link` | 409 | Today's text parser returns 409 for `"already has a target"`. **An occupied to-one link only** - see below. |
| `Conflict` | 409 | Q2: a conflicting save returns 409. Reached through P-09's `DbUpdateConcurrencyException` → `Conflict`. **No producer exists today.** |
| `Cascade` | 500 | Today's behaviour: a subscription-cascade depth overflow is an `InvalidOperationException` (`DomainToCSharpExporter.Notify.Dispatch.cs:87-97`), so it surfaces as 500 - through the action's `catch (Exception)`, or unhandled on create. Orchestrator ruling O-4; the owner may veto. |
| `Required` | 400 | Q7: an omitted required value is a bad request. |
| `Stage` | 409 | **Ruled (owner, #677):** a stage failure conflicts with the resource's current state. |
| `Policy` | 422 | **Ruled (owner, #677):** a `require` guard refused a well-formed request; not authorisation, so not 403. |
| `Range`, `Length`, `Pattern`, `Enum`, `Arithmetic`, `Argument`, `Constraint` | 400 | Today's behaviour: every kind outside the two text markers is 400. |

Q3 is why the mapping lives on the kind at all: a host maps kind→status, and the kind is the
contract, not the message.

**Honest "today".** Only `Unique`, `Link` and the ten 400 rows have a producer that reproduces
today's observable status. `Conflict` is decided by Q2 ahead of its producer. `Cascade`'s 500 is
today's *incidental* status, ratified rather than derived. `Stage` and `Policy` were placeholders
until the owner ruled them (409, 422); changing a row is one arm plus one test row - exactly the
property this table buys.

**Narrow `Link`.** `Link` → 409 means an **occupied to-one link** and nothing else: today the only
such text is `"already has a target"` (`Relationship.cs:41`, `LinkViolationMessage`). R-04 must
emit `Link` for that case alone; a target that fails to *satisfy* a declared link is not a
conflict, and R-04 owns that distinction.

**Unknown kinds fail closed.** The switch arm is `_ => throw new ArgumentOutOfRangeException(...)`
naming the kind and its numeric value. It never falls back to 400.

The `_` arm is **required**, not optional: a `switch` expression over an enum without a default
arm is `CS8509`, and `Poly.Runtime` builds with `TreatWarningsAsErrors`. So the compiler does *not*
report a missing kind; a default arm would silently absorb a new one. The guard is the throw plus
`DomainFailureStatusTests.EveryKind_HasStatus_AndTheSplitIsExactlyFourEightOneOne`, which pins the
member count at 14 and compares every kind against an expected row written out as an independent
literal - never derived from `Http`.

### 2. `Poly.Runtime.IVersioned { long Version { get; } }`

The optimistic-concurrency token on a persisted entity. A shape only: no setter, no default
implementation, no behaviour. P-09 (`EfConcurrencyContract`) binds it and owns the shadow column
and the bump-on-save; generated plain C# entities are not thread-safe by contract (#574), so
concurrent writes stay the store's concern. If P-09's plan lock declares the interface unused,
R-07 deletes it rather than a host keeping a dead token.

### 3. What this file does not decide

- **422 vs 400.** O-R3 stayed with today's 400; a move to 422 for any row is a separate ruling.
- **`Stage` and `Policy` conflict-ness.** Ruled by the owner (#677): `Stage` 409, `Policy` 422 (P-14a).
- **`NotFound`.** Not a kind and not a row. R-02's lock made `NotFound` a host-level status for an
  unknown id; P-14 owns that 404 branch.
- **The generated host.** R-06 pins the contract; P-14 is what makes the emitted host honour it.

## Consumers and elimination

- **P-14 (`HttpFailureMapping`, wave 13)** calls `DomainFailureStatus.Http(kind)` - or reads it at
  generation time - and deletes `MinimalApiGenerator.Host.cs:34-53`. It must not print a second
  kind→status table (#677 g.10: message parsing is forbidden after R-07). Once P-14 removes the
  catch-all, an unknown kind's
  `ArgumentOutOfRangeException` reaches the ASP.NET pipeline as a 500.
- **P-09 (`EfConcurrencyContract`, wave 10)** binds `IVersioned`.

## Package decisions (`Poly.Runtime`)

Five properties the package holds, so a later slice does not re-derive them:

- **BCL-only, by test** - `PolyRuntimePackageTests.PolyRuntime_ReferencesNothingBeyondTheBcl` fails
  on any reference the shared framework does not ship, and on any `PackageReference` or
  `ProjectReference` in the project file. This is why `Http` returns `int` and not
  `Microsoft.AspNetCore.Http.StatusCodes`.
- **Packable, version tracking Poly** - `Poly 1.0.1` depends on `Poly.Runtime 1.0.1`, so the two
  versions move together; bumping either is an owner decision.
- **No type-forwarders** - a compiled consumer of `Poly 1.0.1` takes a binary break when the
  namespace moves; that is the accepted, recorded risk for this wave.
- **Kinds are closed and append-only** - `DomainFailureKind` is serialised by name; a kind is added,
  never renamed or removed, and a new kind fails the status test until the table gains an arm.
- **Namespace and assembly pairing holds until R-07** - the port and the status table are
  `Poly.Runtime.*`; `ConstraintFailureException` stays `Poly.DomainModeling.Runtime` until R-07
  completes the move.

**Versioning.** An additive member on this package (another status arm for a future kind, another
`{ get; }` on `IVersioned`) needs no new ADR - the R-09 A1 precedent. A new *type*, a changed
return type or a second table does.

## Addendum: the failure-construction seam (#677 R-04, #557)

Every lowered failure is built by `FailureLowering` and carries its kind, element path and guard. Per site:

| Site | Kind | `ElementPath` | `Guard` |
|------|------|---------------|---------|
| create/assign constraint leaf | `Required`, `Range`, `Length`, `Pattern`, `Enum`; an `equals` check is `Constraint` (the enum is closed; 400 matches today's status) | `Entity.Property` | none |
| storage unique (create guard, assign rewrite) | `Unique` | `Entity.Property` | none |
| `EnsureUnique` without the storage port | `Constraint`, not `Unique`: a missing port is not a duplicate key | `Entity` (the property is a run-time parameter) | none |
| stage gate | `Stage` | `Entity.Action` | the stage name |
| stage fallthrough | `Stage` | `Entity.Action` | none |
| `require P` / `require not P` / stage `require` | `Policy` | `Entity.Action` | the real policy name (`require not P` carries `P`, never `not_P`) |
| unlinked path-prefix hop, unlinked invoke target | `Required` | `Entity.Rel` | none |
| occupied to-one link | `Link` | `Entity.Rel` | none |
| rewrap of a callee (create-in, invoke, `for`, contract adapter) | the callee's | `Caller.Action` prepended with `At` | the callee's |

**Path rule.** The leaf names the entity that owns the member. Only a rewrap of a callee prepends `Caller.Action`, so a nested range failure is `Product.Hold.Reservation.Qty`. The `Create{Nav}` helper and the entity's own `EnsureUnique` are not callees and add no prefix. A unique assign therefore differs by storage in kind and path (`Constraint` at `Widget` when `EnsureUnique` itself runs, `Unique` at `Widget.Sku` after the storage rewrite) but never by an action prefix; `UniqueAssign_ElementPath_HasNoCallerPrefix_BeforeAndAfterTheStorageRewrite` pins it through `At`.

**`Guard` per kind** is pinned by `FailureTypingTests`: `Policy` (the real policy name) and `Stage` (the stage name) carry one; every other kind carries none. X-01's `FailedGuards` is a projection, not `Failure?.Guard`: it takes `Guard` only for the action's own `Policy` block, never a `Stage` guard, and for a path-prefix or unlinked-invoke `Required` (path `Entity.relation`, `Guard` null) it lists the action's policy names. A rewrapped callee's failure projects `[]`: the caller's policies did not fail.

**Void bodies** (constructor, `entry`, handler) keep `throw new ConstraintFailureException(message)` through `FailureLowering.Throw` / `ThrowFrom`; the kind is dropped there and L-04 deletes both. **`Poly` shadowing:** the factory receiver prints as `Poly.Runtime.DomainFailure` from expression position, so a member or type named `Poly` breaks any domain that prints a constraint failure. `Poly` is reserved (`ReservedNames`), as is `System`; `DomainResult` is reserved as an action parameter only for void actions. Not in scope: `Stages.cs:77` and the cascade overflow stay `InvalidOperationException`; `ParityOutcome.FailureKind` is populated by X-01, not here; #622's text belongs to M-09.
