# Final Boss follow-ups — PR 58 `ab7f554a` Hotel dogfood — 2026-09-06

Source review: [`2026-09-06-pr58-ab7f554a-final-boss.md`](2026-09-06-pr58-ab7f554a-final-boss.md).

**Verdict context:** ship. Open items are post-merge probe/product hardenings — not ship-blockers for this tip. Razor F1–F5 re-verified from `git show ab7f554a:`; none closed; none promoted to bugs.

---

## Open bugs (must close before ship)

None.

## Open suggestions

- [ ] **F1** — `docs/probes/dogfood/hotel.poly:182-191` — Close `BookSection` hall/`section.hall` identity drift: require the passed hall is the section’s hall (policy/guard), **or** drop the `hall` parameter and initialize EventHold.`hall` from `section.hall` in create-in. Add a negative test for mismatched hall.

- [ ] **F2** — `docs/probes/dogfood/hotel.poly:96-98` — Guard `ClearChildBusy` so BusySections cannot go negative (`require` BusySections > 0 or equivalent). Aligns with known PR-body caveat; dogfood Occupy/Vacate path already stays non-negative.

- [ ] **F3** — `Poly.Tests/DomainModeling/HotelDogfoodTests.cs` — Add EventHold soft-create + Confirm `PartyFits` failure parity (oversized BookHall or BookSection), matching Reservation coverage at `:131-143`.

## Open nits

- [ ] **F4** — `docs/probes/dogfood/hotel.poly:315-320` and ReleaseSection `:331-336` — Comment that BookSection holds set both `hall` and `section`, so `StartSection` / `ReleaseSection` must not require `not HasHall` (asymmetry with `StartHall` / `ReleaseHall` `not HasSection` is intentional).

## Known caveats (track; do not re-open as PR58 blockers)

- [ ] **F5** — Last-writer `Occupied` and unwired Create: (1) Room Occupied is a boolean Each-subscription last write — at **runtime** with store, Cancel/NoShow of a sibling stay on `room.stays` assigns Occupied false while another stay is InHouse (`hotel.poly:41-46`, `DomainInstanceStore.cs:527-529`); exporter Occupied is the same last-writer class; (2) BusySections underflow (overlaps F2); (3) public `Create` does not wire store subscribers (`Notify` no-ops without Store). PR 58 dogs the wired BookStay/BookHall/BookSection + store path only (single stay Occupied asserted). Keep on product backlog until Occupied is an honest aggregate or the probe documents the single-stay scope.

## Disposition of Razor items (this SHA)

| Id | Razor | Final Boss |
|----|-------|------------|
| F1 | open suggestion | **still open** — `hotel.poly:182-191` still stores caller `hall`; occupancy still uses `section.hall`; test `:236-243` still matching hall only |
| F2 | open suggestion | **still open** — `hotel.poly:96-98` still unguarded `- 1` |
| F3 | open suggestion | **still open** — EventHold Confirm `PartyFits` still untested |
| F4 | open nit | **still open** — StartSection `:315-320` still lacks `not HasHall`; ReleaseSection `:331-336` same (called out on re-verify, not a new F#) |
| F5 | known caveat | **still open** — runtime sibling-stay Cancel last-writer Occupied confirmed reachable; still not a ship-blocker for the single-stay walk |

## Out of scope this review

- PR 57 (`6cb49aba` LowerActionBody named-execute) present on full tip vs `origin/master` — already Razor/Final-Boss ship-reviewed; re-open only if a hotel path regresses named execute. This walk’s nested invoke (MarkChildBusy, PostRoomNight, Occupy CanBook message) did not.

## Process

None new. Razor F1–F5 were the right residual set; re-verify did not find a missed bug class that needs a protocol/gate change.
