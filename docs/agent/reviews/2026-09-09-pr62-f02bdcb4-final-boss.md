# PR 62 Final Boss Review — 2026-09-09

- **Target**: PR 62 (https://github.com/scoizzle/Poly/pull/62), branch `cleanup/public-create-wire-inverses`, vs `origin/master` (`30c049cc5d15e6fd280d5d461a2e2f194996109e`)
- **Mode**: re-verify (not rubber-stamp). Prior Warden (`docs/agent/reviews/2026-09-08-pr62-d6e19721-warden.md`) claims treated as claims to prove or reject from this SHA.
- **Model**: grok-4.6
- **SHA**: `f02bdcb44606388db86b5c3749d8dbdfa23e0ade` (`git rev-parse HEAD` matched; product HEAD not rewritten)
- **PINNED worktree**: `/workspace/Poly-pr62-f02bdcb4`
- **Vs prior Warden tip**: `d6e197219e4aa18389d624170f325865b5fb8a7a...HEAD` — `Poly/Grammar/GrammarBuilder.cs` only (+10 XML docs; PR 67 merge-master). Product Create/Bind/CreateNav files byte-identical to Warden tip (`git diff d6e19721 HEAD -- Poly/DomainModeling/Lowering/DomainToCSharpExporter*.cs Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs docs/CORE.md` empty).
- **Issue counts**: 1 bug, 3 suggestions, 1 nit
- **Verdict**: **not ship** — MERGE BAR unique-inverse path is real, but named `create in Rel` / `BindCreateIn` / `CreateNav` fail closed when the parent has two collections of the child type. That is the case create-in exists to disambiguate. Warden “0 bugs / no sibling-path drift” is rejected.
- **Process notes**: Warden call graph claimed simulate runs exported `Type.Create`; at this SHA simulate still binds Store jobs through `DomainInstanceStore` / `HostAbi` (`DomainInstanceStore.cs:198-228`, `HostAbi.cs:875-915`). C# `Type.Create` is the emit factory `BindCreate`/`CreateNav` call. Sibling-path check must include named create-in when Type-create is ambiguous (F6 shape + `create in fines`), not only unique-inverse HotelStay/Fine.

## Summary

PR 62 moves unique-inverse collection `Add` + `Register*` into public static `Type.Create` via `peer.Attach*(created)`, and removes the BindCreate autoLink `_field.Add` plus CreateNav’s peer-`Attach*` loop. Unique-inverse Type-create (Fine/Patron) and the HotelStay CheckIn oracle hold on this SHA. Ambiguous **public** `Child.Create(peer)` fail-closed holds. Merge-master did not reopen STORE-bind dual-path.

The hole: `CreateNav` still auto-wires `this` into `Target.Create` whenever the child has a unique to-one back-ref (`FindAutoWireBackReference`), **even when** the parent has several collections of that child. `Create` then hits `inverseCount > 1` and returns Failure (`DomainToCSharpExporter.cs:726-732`) **before** CreateNav’s fallback `_field.Add` (`Notify.cs:227-252`) can run. Runtime `Store.CreateIn` still links the **named** relationship and skips the inverse (`HostAbi.cs:905-915`). Valid DSL; C# create-in wrong; tests never force that sibling.

## MERGE BAR disposition (this SHA)

| # | Claim | Disposition | Evidence |
|---|---|---|---|
| 1 | Public `Type.Create` wires unique inverse `Attach*` + subscription `Register*` | **proved** (unique inverse) | `DomainToCSharpExporter.cs:688-743` — for each singular ctor nav, unique collection inverse → `param.Attach{Inverse}(created)` when non-null. `AddAttachNavMethod` `Notify.cs:275-305` does `_field.Add` + `Register{Source}{Stage}Subscriber`. Oracle `Export_PublicCreate_CheckIn_SetsRoomOccupied_ViaGeneratedTypes` (this SHA, pass): `Room.Stays.Count==1` then `CheckIn` sets `Occupied`. |
| 2 | `BindCreate` calls `Target.Create` (no separate autoLink `_field.Add` outside Create for unique inverse) | **proved** | `StoreBind.cs:118-125` — `target.Create(args)` then `RewrapObjectResult` only. Removed autoLink `IfStatement` + `_field.Add` (diff vs `origin/master`). `autoLink` still used only as `wireUnambiguousBackRef` (`:113-116`) so unique Fine gets `this`. Test `Export_CreateType_UnambiguousManyRel_CreateAttaches_BindCreateDoesNotDoubleAdd` (pass): Fine.Create contains `AttachFines`; BindCreate does not contain `_fines.Add`. |
| 3 | `CreateNav` calls `Target.Create` and defers Add/Register when Create already wired this collection | **proved for unique inverse; sibling broken** | `Notify.cs:199-202` `target.Create(args)`. Defer gate `:222-225` + skip `:227-253` when `autoWireBackRef` and `FindInverseCollection(entity, targetTypeName)==rel`. Unique HotelStay/University path defers. When inverse is **not** unique, Create fail-closes first (`cs:726-732`) so the fallback Add at `:227-237` is dead for auto-wired `this`. See Issue 1. |
| 4 | No `Attach*` hot-wire bypassing shared Create for paths under review | **proved** | Only emit of `Attach{Name}` call is `DomainToCSharpExporter.cs:741` (inside `Create`). Method synthesis `Notify.cs:299`. CreateNav’s old peer-`Attach*` loop is gone (diff `Notify.cs`). University `section.AttachEnrollments` now comes from `Enrollment.Create` (`University_Export_Compiles` pass). |
| 5 | Ambiguous inverses fail closed | **proved for public Create; over-applied on CreateNav** | `DomainToCSharpExporter.cs:726-732` + test `Export_PublicCreate_AmbiguousInverse_FailsClosed` (pass): Child.Create body contains fail message, no `AttachPrimary`/`AttachSecondary`. That fail-closed is what kills named create-in (Issue 1). Runtime still skips, does not fail (`HostAbi.cs:912-914`). |
| 6 | Merge-master delta does not reopen STORE-bind dual-path | **proved** | `git diff --stat d6e19721...HEAD` → `Poly/Grammar/GrammarBuilder.cs` +10 (doc comments on `Kind`/`Value`/`Predicate`/…). No `StoreBind.cs` / BindCreate Add restoration. |

## Call graph (C# emit, this SHA)

```
BindCreate (StoreBind.cs:118-125)
  → Target.Create(args)             // public factory; autoLink passes this when outs.Count==1
      → peer.Attach*(created)       // unique inverse only
  → RewrapObjectResult              // no _field.Add

BindCreateIn (StoreBind.cs:193-196)
  → this.Create{Rel}(...)           // CreateNav
      → Target.Create(args)         // auto-wires this when unique to-one back-ref
          → fail closed if inverseCount>1 and this != null   // BUG on named create-in
          → else Attach*
      → createWiredThisCollection? skip Add/Register : fallback Add

Simulate Store.Create / CreateIn (NOT Type.Create)
  → DomainInstanceStore.Create/CreateIn
      → Link named rel; TryLinkInverseCollection no-op if count!=1
      → TryAutoLinkUnambiguousOutbound no-op if outs.Count!=1
```

## Sibling-path check

| Path | Unique inverse (1 collection) | Several collections of same child (F6 / Ambiguous DSL) | Test forces this sibling? |
|------|-------------------------------|------------------------------------------------------|---------------------------|
| Public `Type.Create(peer)` | Attach unique (`cs:737-743`) | Fail closed (`cs:726-732`) | Yes — CheckIn + `AmbiguousInverse_FailsClosed` |
| `BindCreate` Type-create | `this` into Create; Create Attaches; no extra Add (`StoreBind.cs:113-125`) | `autoLink=false`; `this` not passed; Create sees null peer; no fail; unlinked | Runtime F6 `TypeOnly_AmbiguousManyRel_ListsButDoesNotLink`; C# BindCreate arm not string-asserted for `_fines.Add` absence on two-nav domain |
| `CreateNav` / `BindCreateIn` named create-in | Create Attaches; CreateNav skips Add (`Notify.cs:222-253`) | Auto-wires `this` (`Notify.cs:138-141`); Create fail-closes; fallback Add never runs | **No** — Issue 1 |
| Runtime `Store.CreateIn` named rel | Link named + unique inverse | Link **named**; inverse skip (`HostAbi.cs:905-915`) | Rel-only Fine probes (unique). Several-match create-in **untested** |
| Runtime Type-create auto-link | `TryAutoLinkUnambiguousOutbound` (`HostAbi.cs:875-886`) | no-op `outs.Count!=1` | Yes — `SimulateCreateDogfoodTests.cs:183-227` |
| University `create in enrollments` + `waitlist: many WaitOffer` | Unique Enrollment inverse on Section; `section.AttachEnrollments` in Enrollment.Create | N/A (WaitOffer ≠ Enrollment) | `University_Export_Compiles` (pass) — does not cover two Enrollment collections |

## Optional oracles (this SHA, read-only)

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter "/*/*/*/*Export_PublicCreate*"
  total: 3  failed: 0  succeeded: 3
  Export_PublicCreate_WiresInverseAttachCalls
  Export_PublicCreate_CheckIn_SetsRoomOccupied_ViaGeneratedTypes
  Export_PublicCreate_AmbiguousInverse_FailsClosed

Export_CreateType_UnambiguousManyRel_CreateAttaches_BindCreateDoesNotDoubleAdd  pass
University_Export_Compiles  pass
```

`Export_PublicCreate_CheckIn` remains a valid unique-inverse + registry oracle on tip. It does not cover CreateNav or multi-collection create-in.

`DomainToCSharpExporterTests.cs` `[Test]` count this SHA: **91**.

## Prior Warden claims (re-dispositioned)

| Warden claim | This SHA |
|---|---|
| 0 bugs; MERGE BAR satisfied; ship | **Rejected** — Issue 1 |
| Simulate → `Entity.Create` → `Attach*` | **False** — simulate is Store/HostAbi; emit is Type.Create |
| No sibling-path drift: BindCreate and CreateNav both call Create | Both call Create; CreateNav + several-match inverse is a **new** fail-closed vs master CreateNav `_field.Add` and vs runtime CreateIn |
| F1–F3 open suggestions/nit | Still open (Issues 2–4 below; CORE nit = Issue 5) |

## Issues

### Issue 1 -- Severity: bug

- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:726-732` with `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:138-141` and `:199-253`
- Description: Named create-in is how a domain picks **one** collection when Type-create is ambiguous. CreateNav still passes auto-wired `this` into `Target.Create` whenever `FindAutoWireBackReference` is unique (`Notify.cs:126`, `:138-141`). That uniqueness is “one to-one back-ref”, not “one collection on the parent”. `Create` then counts parent collections of the child (`cs:707-719`); `inverseCount > 1` emits `return Failure("'peer' has no unique inverse collection…")` gated only on `param != null` (`cs:726-732`). CreateNav’s fallback `_field.Add` (`Notify.cs:227-237`) runs only if Create succeeded. Reachability on **valid** analyzed DSL (same shape as `Export_PublicCreate_AmbiguousInverse_FailsClosed` plus `create in primary`): analysis has no errors; generated `CreatePrimary` → `Child.Create(peer: this)` → Failure; collection empty. Runtime sibling `Store.CreateIn` / `HostAbi.TryLinkInverseCollection` (`HostAbi.cs:905-915`) links the named rel and skips the inverse — does not fail. Master CreateNav always `_field.Add` on the named collection and only Attach’d a unique inverse. Regression for BindCreateIn/CreateNav; dual-path vs simulate grew. HotelStay/Fine/University do not hit this (one collection per child type, or WaitOffer ≠ Enrollment).
- Suggestion: Do not fail-closed inside `Create` when the caller already named the collection. Options that preserve public-Create fail-closed for `Child.Create(peer)`: (a) Create **skips** attach on `inverseCount > 1` (HostAbi rule) and CreateNav fallback Add handles named create-in — then change or drop `AmbiguousInverse_FailsClosed` if public Create must still fail; or (b) CreateNav must not pass `this` into Create when `FindInverseCollection(source, child)` is null, set the back-ref another way, and keep the fallback Add; or (c) fail-closed only on the public Create entry, not when the child is constructed for a known `rel`. Add a test: Peer `{ primary: many Child; secondary: many Child; Make: action { create in primary {} } }` / Child `{ peer: Peer }` — generated `CreatePrimary` succeeds, `_primary.Add` (or AttachPrimary) runs, `_secondary` untouched, `Child.peer == this`. Mirror with BindCreateIn. Do not treat CheckIn as coverage of this sibling.
- Status: open
- Reachability: **valid domain, valid inputs** after normal analyze. The Ambiguous test domain already parses; adding `create in primary` is legal.

### Issue 2 -- Severity: suggestion

- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:675-677`
- Description: Warden F1 still true. Comment says “same seam as CreateNav/Attach.” Create is the attach source; CreateNav defers (unique) or is blocked (ambiguous). The comment still reads as parallel seams.
- Suggestion: Rephrase to state Create is the unique-inverse attach source; CreateNav/BindCreate defer here when that attach ran.
- Status: open

### Issue 3 -- Severity: suggestion

- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:702-719` vs `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:868-880`
- Description: Warden F2 still true. Inline inverse count is a copy of `FindInverseCollection` plus an `inverseCount > 1` distinction `FindInverseCollection` collapses to null. Drift risk if one rule changes.
- Suggestion: One helper that returns `(Relationship? unique, int count)` used by Create, CreateNav’s defer gate, and `FindInverseCollection`.
- Status: open

### Issue 4 -- Severity: suggestion

- File: `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:1976-2044`, `:2741-2902`
- Description: Unique-path tests never slice `CreateReservations` / `CreateFines` / `CreateOrders` to prove defer (`DoesNotContain("_reservations.Add")` in the CreateNav body). BindCreate is asserted; CreateNav defer is source-only. That is how Issue 1’s sibling stayed untested.
- Suggestion: Assert Guest.CreateReservations (HotelStay) contains `Reservation.Create` and does not contain `_reservations.Add`; keep Issue 1’s several-match create-in test separate.
- Status: open

### Issue 5 -- Severity: nit

- File: `docs/CORE.md:185`
- Description: Warden F3 still true. The new “public `Type.Create` wires unique inverse `Attach*`…” clause is appended to the existing lowering paragraph.
- Suggestion: Split into its own sentence/bullet in the Create/create-in row. No correctness impact.
- Status: open

## Checklist

- [x] Diff collected vs `origin/master` (5 files, +320/−97) and vs Warden `d6e19721` (GrammarBuilder only)
- [x] Stance: adversarial re-verify; not implementer; no product/test edits
- [x] Sibling-path check before severity (BindCreate, BindCreateIn/CreateNav, public Create, HostAbi Create/CreateIn, University)
- [x] Reachability → severity: Issue 1 reachable on valid analyzed domains
- [x] Invariant comments checked (`cs:675-677`, `cs:702`, `Notify.cs:219-221`, `StoreBind.cs:123-124`)
- [x] Counts from this tree: exporter tests 91 `[Test]`; PublicCreate filter 3/3 pass
- [x] Warden files not overwritten
- [x] HEAD SHA `f02bdcb44606388db86b5c3749d8dbdfa23e0ade` at review start
