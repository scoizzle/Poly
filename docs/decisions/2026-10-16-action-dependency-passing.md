# ADR: An action gains a dependency only from the calls it makes

**Date:** 2026-10-16
**Status:** Accepted direction (spike). No generator change.
**Deciders:** Spike on `docs/action-dependency-passing` at `a0922e64`.

**Related:** [`2026-10-15-produced-code-dependency-injection.md`](2026-10-15-produced-code-dependency-injection.md) · [`2026-09-05-lowered-module-is-domain-meaning.md`](2026-09-05-lowered-module-is-domain-meaning.md) · [`2026-09-04-frozen-core-pipeline.md`](2026-09-04-frozen-core-pipeline.md) · probe [`docs/probes/action-deps/`](../probes/action-deps/README.md)

## Decision

The produced module threads a dependency as a parameter along the calls that use it. The route is the one place the container supplies the value. An action gains the parameter when its lowered body uses the dependency, or when a call it makes does. It does not gain the parameter because some other action in the domain might.

`IServiceProvider` is not passed down the stack. Action handlers are not types the container activates. A field on the entity and an `AsyncLocal` both carry the value without a signature, and both drop it on a call that does not copy it. The database unique index still rejects a duplicate at `SaveChanges`. That rejection is after the action has returned.

## The call set is closed

`AddActionMethod` copies `action.Parameters` and no other parameter (`DomainToCSharpExporter.Actions.cs:42-73`). `EffectLoweringPass.InvokeAction` calls the target with those bindings only (`EffectLoweringPass.cs:671-699`), including a cross-entity `nav.Action(args)`. The capture route holds `CrmDbContext db` and calls `entity.Capture(dto.request)` (`EmitGolden/crm-dslcompiler/Program.cs.golden:912` and `:933`).

`Reservation.CheckIn` shows the downstream calls that exist today (`Reservation.cs.golden:120-127`):

```csharp
var create0 = this.CreateFolio(0L);
// ...
this.CurrentStage = ReservationStage.InHouse;
this.NotifyInHouseSubscribers(previousStage1);
```

`NotifyInHouseSubscribers` calls `WhenEachReservationInHouse()` with no arguments (`Room.cs.golden:91-93`). The compiler sees `CreateFolio`, `NotifyInHouseSubscribers`, and `WhenEachReservationInHouse`. If `CreateFolio` needs the context, `CheckIn` passes it. If the subscriber handler needs it, the notify method and `CheckIn` pass it. The set is that chain. It is not every action on `Guest` or `Room`.

The interpreter runs the same method. `InvokeAction` takes the argument dictionary the caller supplies (`DomainEntityInstance.cs:619-638`). A parameter the module added is an entry in that dictionary, supplied by the simulate host the way the route supplies `db`.

## What was compared

The probe is `docs/probes/action-deps/`. `dotnet run --project docs/probes/action-deps/Probe.csproj -p:NuGetAudit=false` printed six `ok` lines and `six shapes observed`, and exited 0. Each shape uses one Sqlite `Data Source=:memory:` context and `Widget.ExistsWithCode`.

### 1. Thread the parameter — this decision

`Lot.Issue(string code, LabDbContext db)` calls `Widget.Create(code, db)`. The first create of `"A"` succeeds. The second returns `Unique constraint violated: 'Code' value is already used on another 'Widget'.` The context the caller was given is the context the nested create checks, including `Local`.

This is the form already recorded for `Create`. The addition is the fixpoint: a method gets the parameter when it uses the context or when its callee's signature has it. Lowering writes the argument at the `Invoke` it already emits. The route passes `db` into the top action. No new `uses` id.

When a second dependency shows up (the context and a billing port), the same fixpoint can pass one value whose members are those two, if listing both on every frame is the worse signature. That value is a fixed type. It is not `IServiceProvider`. Nothing in the current bodies needs that wrapper. The context is the dependency this decision threads.

### 2. Pass the container down the stack — rejected

`Widget.CreateFrom(IServiceProvider, string)` calls `GetRequiredService<LabDbContext>()` and then `Create`. Inside one scope the second call sees the duplicate, because the scope returns the same context. The probe observed that. The mechanism works. It is still the wrong parameter.

The signature no longer says `Issue` uses the context. Any frame can resolve anything else registered in the scope. Two contexts in one host make the lookup ambiguous. `GetRequiredService<T>()` is an extension. The interpreter's instance lookup does not find it, so the printed call and the executed call are different methods. The route already holds `db`.

### 3. A container-activated handler — rejected

`IssueHandler(LabDbContext db)` is registered scoped and resolved from the provider. `Issue(string code)` calls `Widget.Create(code, db)`. The duplicate fails. The container filled the handler. It did not remove the parameter from `Create`.

The produced call is not a call to a handler. It is `this.CreateFolio(...)` and `nav.Action(args)` on an entity the context loaded. `Reservation` has a private parameterless constructor and a private constructor of its scalar values (`Reservation.cs.golden:23-25`). EF materializes that object. The container does not. Activating a handler per action, and injecting every handler that action might call, is the same transitive closure moved into constructors, plus a second object beside the entity method the module runs.

### 4. A field set on the instance — rejected

The route can set `parent.Db = db` after it loads the parent. A child that copies the field sees the duplicate. A child constructed without the copy returns success and does not check. The probe observed both. `Widget.Create` is static and has no `this` until after `new`. EF materializes a `Folio` without copying a field the route set on the `Reservation`. Copying the field at every `new` is parameter threading done by hand, and a missed copy is a silent success. This is the shape `DomainEntityInstance.Store` already has, which the previous decision stops using as the persistence runtime.

### 5. An ambient on the call — rejected

`AsyncLocal<LabDbContext?>` set before the call lets `CreateAmbient` see the duplicate with no new parameter. Clearing it makes the next call throw `No storage context.` The probe observed both. The dependency is not in the signature. A second operation on the same flow overwrites it. The interpreter has to set the same slot the route sets, or the module body the interpreter runs does not see a context. Two contexts in one process have no place to say which one.

### 6. Fail at SaveChanges only — not the action result

Adding two widgets with `Code` `"A"` and calling `SaveChanges` throws `DbUpdateException`. The probe observed that. The unique index remains the database backstop. It runs after `CheckIn` has assigned `CurrentStage` and returned `Success`. The in-memory action result is already the success path. `ExistsWithCode` is what makes the nested create return `Failure` before that assignment.

## Falsifiable claim

An action method gains the context parameter exactly when its lowered body uses the context or a method it calls has the parameter. The route passes the context it already binds. The module does not name `IServiceProvider`. No action is a type registered with the container.

The claim is false if a context parameter appears on an action that neither uses storage nor calls one that does, if a container type appears in the module, or if the interpreter runs a different body than the method the route calls.

## Kill criteria

| Condition | Result |
|-----------|--------|
| Open-world "every action, in case" | Not this decision. The chain is the lowered calls. |
| Container type in the module | Option 2. Rejected. |
| One handler type per action, activated by the container | Option 3. Rejected. |
| A missed copy of an instance field returns success | Option 4. Rejected. |
| The unique index is the only check | Option 6. The action has already returned. |

## Non-goals

- No generator change, no golden edit, no audit edit, and no `docs/plans/poly-eng/board.md` row.
- The probe is not an artifact-catalog member and not a `*.poly` sample.
- No second dependency is invented so that a unit-of-work wrapper has something to hold.
