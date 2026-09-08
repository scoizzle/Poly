# Final Boss phenomenal-review PR 58 — 2026-09-06

- **Target**: PR 58 https://github.com/scoizzle/Poly/pull/58 · branch `probe/hotel-dogfood` vs `origin/master` · PINNED worktree `/workspace/Poly-pr58-ab7f554a`
- **Mode**: re-verify (not rubber-stamp). Razor `docs/agent/reviews/2026-09-06-pr58-ab7f554a-razor.md` + follow-ups treated as claims to prove or reject from this SHA
- **Model**: grok-4.6
- **SHA**: `ab7f554aa12475f258a90be33fea0ce2387ccbb2` (`git rev-parse HEAD` matched `gh pr view 58` `headRefOid`; product HEAD not moved, not merged, not force-pushed)
- **Issue counts**: 0 bugs, 3 suggestions, 1 nit
- **Verdict**: **ship**
- **Process notes**: Independent re-read of `git show ab7f554a:` hotel.poly + HotelDogfoodTests + oracle/README; hotel-only delta vs merge-after-PR57 `705ae51c` is 4 files / +625. Full tip vs `origin/master` also carries PR 57 (`6cb49aba` LowerActionBody named-execute) — already ship-reviewed; hotel path uses named `InvokeAction` / nested `invoke` and does not regress that surface. Razor files not overwritten. Local `dotnet restore` needed `-p:NuGetAudit=false` (NU1903 advisory); TUnit `--treenode-filter` did not match the Hotel class in this environment. Primary green oracle: CI `Build & test` SUCCESS on this tip — **2791 / 2791**, failed 0. `--list-tests` on this SHA includes `Hotel_Export_Compiles`, `Hotel_Runtime_WalksStayFolioAndMeetingAirwall`, and `Compile_All_DemoDomains_EmitCompilableSolution(docs/probes/dogfood/hotel·poly)`.

## Summary

PR 58 adds `docs/probes/dogfood/hotel.poly` (stay/folio + hall/section airwall), `HotelDogfoodTests` (C# export Roslyn compile + runtime walk), enrolls `hotel.poly` in `DslCompilerCompileOracleTests`, and lists it in `docs/probes/README.md`. Re-verify of the stay walk, airwall (`BusySections` / `CanBook` / `ParentFree` / `StartHall` vs `StartSection`), compile oracles, and soft oversized BookStay `PartyFits` matches Razor: the tests exercise the wired BookStay/BookHall/BookSection + store-notify path, not the admitted unwired public-Create / last-writer Occupied holes. No fail-open found on that path. F1–F5 remain open as post-merge hardenings, not ship-blockers. No new bug.

## Scope / primary evidence

| Claim | Evidence (this session) |
|-------|-------------------------|
| Tip SHA | `git rev-parse HEAD` = `ab7f554aa12475f258a90be33fea0ce2387ccbb2`; matches PR `headRefOid`; `MERGEABLE` |
| CI | check `Build & test` SUCCESS, run `34243833518`, **total 2791 / failed 0 / succeeded 2791** |
| Hotel-only vs `705ae51c` | `hotel.poly`, `HotelDogfoodTests.cs`, oracle +1 arg, README +1 row — **4 files, +625** |
| Full tip vs `origin/master` | 13 files, +746/−47 — includes PR 57 runtime/lowering/test delta (noted, not re-reviewed) |
| Merge-base `origin/master...HEAD` | `8a0339cb` (PR 51 merge) |
| `git show ab7f554a:docs/probes/dogfood/hotel.poly` | 340 lines; matches worktree (no local hotel.poly diff) |
| Oracle enroll | `git show ab7f554a:Poly.Tests/DomainModeling/Lowering/DslCompilerCompileOracleTests.cs` **:168** `[Arguments("docs/probes/dogfood/hotel.poly")]` |
| README | `git show ab7f554a:docs/probes/README.md` **:11** hotel → `HotelDogfoodTests` |

## Checklist

- [x] Diff collected; hotel-only vs `705ae51c`; PR57 noted on full tip; no hotel.poly/test scope drift
- [x] Stance: adversarial re-verify; assume wrong; split-context (reviewer not implementer); no product edits
- [x] Producer/consumer keys traced (create-in navs, inverse `stays`/`events`, store notify, GetProperty)
- [x] Null / partial / not-found: missing BookStay args → InvalidArguments; missing hall on `where` → GetRelatedOne throw (fail-loud, not vacuous true)
- [x] Sibling-path check (table below); tests force the wired store+action path, not public-Create Occupied
- [x] Fail-loud / nested invoke: StartHall → hall.Occupy CanBook is InvalidArguments + ErrorMessage, not outer FailedGuards — reachability proven by test
- [x] Invariant-stating comments: none added that lie; F4 is the missing comment
- [x] Counts from this SHA greps/reads/`git show`; Razor quotes not chain-trusted
- [x] Oracles not weakened (hotel enrolled; no skip/delete of prior probes)
- [x] Review + follow-ups written; Razor files left intact

## Razor claim disposition (re-verified this SHA)

| Razor claim | Disposition | Evidence (path:line this SHA) |
|-------------|-------------|-------------------------------|
| Stay/folio walk: BookStay → Confirm → CheckIn → Occupied/Dirty/folio settle | **holds** | `hotel.poly:161-171` BookStay create-in copies rates/occupancy; `:218-224` Confirm `DatesValid`/`PartyFits`/`HasRoom`; `:228-234` CheckIn `RoomReady` + create folio; `:41-48` Room Occupied/Dirty via `when stays`; `:238-249` PostStayCharge / CheckOut `BillSettled`; test `HotelDogfoodTests.cs:117-172` asserts Occupied true, Balance 220, unpaid `BillSettled`, Settle, Occupied false, Dirty true, StayCount/NightCount, Inspect `IsReady` |
| Soft oversized BookStay Confirm `PartyFits` negative | **holds** | `hotel.poly:211` PartyFits; test `:131-143` party 8 vs MaxOccupancy 3, BookStay succeeds, Confirm FailedGuards contains `PartyFits` |
| Hall/section airwall: BusySections / CanBook / ParentFree / StartHall vs StartSection | **holds on wired path** | Hall `CanBook` `:80`, Occupy `:83-87`, Mark/ClearChildBusy `:93-98`; Section Occupy `:126-132` + ParentFree `:123`; EventHold StartHall `:308-314` (`HasHall` + `not HasSection`), StartSection `:315-320` (`HasSection` only). Test `:236-281`: StartHall blocked `not_HasSection`; StartSection → BusySections=1, CanBook false; salonB Occupy → 2; StartHall ErrorMessage contains `CanBook`; ReleaseSection+Vacate B → 0; StartHall Occupied; salonC Occupy fails `ParentFree` |
| Compile oracle enroll + README; `Hotel_Export_Compiles` | **holds** | Oracle `:168` hotel.poly; README `:11`; `Hotel_Export_Compiles` `:56-81` (no legacy `void Notify(string stageName)`, reservation/section bags, `WhenEachReservationDeparted` / `WhenEachEventHoldReleased`, Roslyn errors empty) |
| F1 BookSection hall identity | **still open (suggestion)** | `hotel.poly:182-191` stores caller `hall` beside `section`; occupancy uses `section.hall` (`:131`, `:137`). Test always passes linked hall (`:236-243`) |
| F2 ClearChildBusy underflow | **still open (suggestion)** | `hotel.poly:96-98` unconditional `- 1`; Vacate requires `IsOccupied` so the paired Occupy/Vacate path cannot double-clear; unpaired/direct Clear can go negative. Test `:252-274` stays non-negative |
| F3 EventHold PartyFits test gap | **still open (suggestion)** | EventHold `PartyFits` `hotel.poly:293,298-304` same soft-create/Confirm shape as Reservation; no oversized BookHall/BookSection Confirm assertion in `HotelDogfoodTests.cs` |
| F4 StartSection asymmetry nit | **still open (nit)** | `hotel.poly:308-320` StartHall requires `not HasSection`; StartSection does not require `not HasHall`. Same pattern on ReleaseHall/ReleaseSection `:324-336`. Intentional because BookSection sets both links |
| F5 exporter/Create caveats | **still open (known; not a PR58 blocker)** | See sibling-path. Runtime last-writer Occupied is reachable on a second stay Cancel/NoShow while another is InHouse (Each subscriptions `hotel.poly:41-46`); PR dogs single-stay store path. Public Create without store skips `when`. BusySections underflow overlaps F2 |
| PR57 named-execute on full tip | **not re-litigated; hotel path does not regress it** | Nested `invoke hall.MarkChildBusy` / `folio.PostRoomNight` / `hall.Occupy` succeed or fail with `CanBook` in ErrorMessage on this walk; BusySections=1 and Balance 220 would not hold if named execute dropped the module body |

## Walk notes (claims ↔ this SHA)

### Stay / folio

BookStay is Active-only create-in (`hotel.poly:161-171`); Confirm is a later guard (`:218-224`). Room occupancy is **subscription + store**: `when stays InHouse` / `Departed,Cancelled,NoShow` / `Departed` (`:41-48`). Create-in `room: room` hits `Store.Link` + `TryLinkInverseCollection` (`DomainEntityInstance.HostAbi.cs:611-612,708-717`) so `room.stays` is linked; stage transition `Notify` → `Store.NotifyTransition` (`HostAbi.cs:20-22`, `DomainInstanceStore.cs:434-529`, default quantifier Each). `GetProperty<bool>` is `value is T` (`DomainEntityInstance.cs:327-330`); Occupied/Dirty defaults and `assign … to true|false` are bool literals — the CheckIn true / CheckOut false+Dirty true assertions would fail if Occupied were the wrong CLR type or if notify never fired.

Folio: CheckIn `create in folio { Balance: 0 }` (`:232`); PostStayCharge `invoke folio.PostRoomNight(amount: NightlyRate)` once (`:242`) — a 2-night stay posting 220 is the per-call action, not nights×rate; the test (`:155-157`) matches that contract. Unpaid CheckOut fails `BillSettled` (`:244-246`, test `:159-161`); Settle then CheckOut. Guest `when reservations Departed as stay` increments StayCount/NightCount (`:153-156`, test `:168-169`). Blocked guest cannot BookStay (action not on Blocked; test `:181-190`). First `InvokeAction("BookStay")` without args (`:114-115`) is missing-argument InvalidArguments (`DomainEntityInstance.cs:499-502`), not a hotel invariant — weak opener, not theater for the rest of the walk.

### Hall / section airwall

Hall Occupied is **action** (Occupy/Vacate), not subscription. `CanBook` = not Occupied, not Offline, `BusySections is 0` (`:80`). Section Occupy requires `CanBook` (section vacant) **and** `ParentFree` (`hall where Occupied is false and Offline is false`, `:123-128`), then `invoke hall.MarkChildBusy`. `ParentFree` lowers to `GetRelatedOne("hall")` (`DomainExpressionLoweringPass.cs:136-144`); zero links throw (`HostAbi.cs:55-60`) — missing hall is fail-loud, not vacuous true.

StartHall vs StartSection: BookSection sets both `section` and `hall`, so `not HasSection` blocks StartHall (`FailedGuards` contains `not_HasSection` — parser names `require not X` as `not_X`, `PolyDslParser.cs:317`). StartSection nested Occupy is the airwall. Nested Occupy `CanBook` failure is **not** outer FailedGuards: `InvokeNamed` maps Blocked → `DomainResult.Failure("invoke 'Occupy' blocked by guards: CanBook")` (`DomainEntityInstance.InvokeNamed.cs:56-62`); StartHall returns `InvalidArguments` with that ErrorMessage (`DomainEntityInstance.cs:565-571`, `DomainEntityInstance.Runtime.cs:676-679`). Test `:268-270` Contains `CanBook` on ErrorMessage is the correct sibling of `:249` FailedGuards `not_HasSection`.

### Compile oracles

`Hotel_Export_Compiles` is DomainToCSharpExporter + CSharpGenerator + Roslyn (not the sqlite All-mode solution). Compile oracle `Compile_All_DemoDomains_EmitCompilableSolution` reads hotel.poly from disk and `AssertSolutionCompiles` (`DslCompilerCompileOracleTests.cs:106-147,168,172-175`). Both are real oracles. Export test does not assert Occupied last-writer bodies (F5); it is a compile/shape smoke, honestly named.

## Sibling-path check

| Semantic | Paths | Invariant hold? | Test forces this sibling? |
|----------|-------|-----------------|---------------------------|
| Room Occupied | `when stays InHouse` true / `Departed,Cancelled,NoShow` false (`hotel.poly:41-46`) via store Each notify | Holds for **single** stay on store+BookStay. Last-writer boolean: Cancel/NoShow of a *other* stay on `room.stays` assigns Occupied false while an InHouse stay remains (Each, `DomainInstanceStore.cs:527-529`). Public Create without store: `Notify` no-ops (`HostAbi.cs:20-22`) — Occupied stays default | Yes: CheckIn Occupied true, CheckOut false (`HotelDogfoodTests.cs:147-167`). **No**: multi-stay Cancel while InHouse; **No**: Create without store |
| Room Dirty | `when stays Departed` (`:47-48`) | Holds on CheckOut | Yes (`:167`) |
| Hall Occupied | Occupy/Vacate actions (`:83-92`), not subscriptions | CanBook includes Occupied + BusySections + Offline | Yes: StartHall Occupied true (`:277-278`); StartHall nested CanBook fail (`:268-270`) |
| Section Occupied | Occupy/Vacate (`:126-138`) | CanBook is only `Occupied is false` (`:124`); ParentFree is a **separate** Occupy guard, not inside Section.CanBook | Occupy+ParentFree forced (`:250-251`, `:279-281`). Section.CanBook-alone is not the airwall |
| BusySections | MarkChildBusy / ClearChildBusy (`:93-98`) from Section Occupy/Vacate only | Paired Occupy/Vacate stays ≥0. No `BusySections > 0` on Clear (F2). Mark has no upper bound. Body is assign-then-invoke (no rollback on nested fail) — MarkChildBusy fail after Occupied=true is unreachable on valid linked InService hall (ParentFree already required hall + not Offline) | Yes: 1 after StartSection, 2 after salonB Occupy, 0 after Release+Vacate (`:252-274`) |
| EventHold start | StartHall (`HasHall` + `not HasSection` → `hall.Occupy`) vs StartSection (`HasSection` → `section.Occupy`) | Asymmetry intentional (F4). BookSection cannot StartHall | Yes: `not_HasSection` (`:247-249`); StartSection (`:250`) |
| EventHold release | ReleaseHall vs ReleaseSection (`:324-336`), same asymmetry | ReleaseSection tested (EventCount). **ReleaseHall / hall.Vacate via EventHold not forced** | ReleaseSection yes (`:272`); ReleaseHall **no** |
| Confirm PartyFits | Reservation vs EventHold | Same soft-create + Confirm shape | Reservation yes (`:131-143`); EventHold **no** (F3) |
| Nested invoke failure | Outer FailedGuards vs InvalidArguments ErrorMessage | Outer StartHall guards vs nested Occupy CanBook | Both: `not_HasSection` FailedGuards; CanBook ErrorMessage |
| Inverse link / notify | create-in + store vs public Create | TryLinkInverseCollection unique many-inverse; Notify requires Store | Dogfood uses store.Add + BookStay/AddHall/BookSection. Public Create Occupied **not** forced (F5) |
| Named execute (PR57) | `Invoke(Member)` module body vs LowerActionBody | Hotel nested invoke works on this walk | Indirect: BusySections/Balance/CanBook message. Not a hotel regression |

## Reachability → severity

- Nested StartHall `CanBook` fail: **valid domain, valid inputs** (BusySections=2). Product behavior is InvalidArguments + ErrorMessage, asserted. Not a bug.
- `GetRelatedOne("hall")` throw on ParentFree with unlinked section: **valid DSL** (Section.hall not required) but not the dogfood walk; fail-loud, not silent Occupied. Suggestion-class identity hole, overlaps F1.
- ClearChildBusy negative: **reachable** by direct Clear or desynced Occupied without Mark; **not** on paired Occupy/Vacate. Suggestion (F2), admitted in PR body.
- Occupied last-writer on sibling Cancel: **reachable** on valid multi-BookStay + store. PR claims a single-stay walk, not “Occupied iff any InHouse”. Tracked as F5, not a lying test.

## Issues

### Issue 1 -- Severity: suggestion
- File: `docs/probes/dogfood/hotel.poly:182-191`
- Description: `BookSection` takes a separate `hall: Hall` and stores it on EventHold alongside `section`. Occupancy and BusySections use `section.hall` (`Occupy`/`Vacate` → Mark/ClearChildBusy at `:131`, `:137`). A caller can pass a hall that is not `section.hall`; EventHold.`HasHall` is true for the wrong space while BusySections move on the real parent. StartHall remains blocked by `not HasSection`, so the walk does not Occupy the wrong hall. Test always passes the linked hall (`HotelDogfoodTests.cs:236-243`). Incomplete identity contract, not a false-green airwall.
- Suggestion: `require` hall identity with `section.hall`, or drop the `hall` parameter and bind EventHold.`hall` from `section.hall` in create-in. Add a negative test for mismatched hall.
- Status: open (Razor F1)

### Issue 2 -- Severity: suggestion
- File: `docs/probes/dogfood/hotel.poly:96-98`
- Description: `ClearChildBusy` assigns `BusySections - 1` with no lower bound. Direct invoke (or Occupied-true without a matching Mark) can drive BusySections negative — admitted PR-body caveat. Vacate requires `IsOccupied`, so the dogfood Occupy/Vacate pair cannot double-clear (`HotelDogfoodTests.cs:252-274`).
- Suggestion: `require` BusySections > 0 (or `not ChildrenFree`) on ClearChildBusy.
- Status: open (Razor F2)

### Issue 3 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/HotelDogfoodTests.cs:131-143` (gap vs EventHold)
- Description: Reservation soft-create then Confirm `PartyFits` is proven. EventHold has the same shape (`hotel.poly:293,298-304`) with no oversized BookHall/BookSection → Confirm failure assertion. Coverage asymmetry, not a lying stay test.
- Suggestion: Mirror the oversized Confirm negative for EventHold (hall or section).
- Status: open (Razor F3)

### Issue 4 -- Severity: nit
- File: `docs/probes/dogfood/hotel.poly:315-320` (and ReleaseSection `:331-336`)
- Description: `StartHall`/`ReleaseHall` require `not HasSection`; `StartSection`/`ReleaseSection` only require `HasSection` (HasHall may be true). Intentional for BookSection dual-link. Undocumented, so a later “symmetrize StartSection” edit would break section holds.
- Suggestion: Comment on EventHold Confirmed/InUse that BookSection holds are dual-linked and StartSection/ReleaseSection must not require `not HasHall`.
- Status: open (Razor F4; Release* included on re-verify)

## Verdict rationale

Zero bugs on the claimed Hotel dogfood path at `ab7f554a`. Export + compile-oracle + runtime walk oracles are real (CI 2791/2791; three hotel test names present). F1–F5 remain open and do not falsify BookStay/BookHall/BookSection store+notify dogfood. **Ship.**
