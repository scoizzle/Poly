# Razor follow-ups — PR 58 `ab7f554a` Hotel dogfood — 2026-09-06

Source review: [`2026-09-06-pr58-ab7f554a-razor.md`](2026-09-06-pr58-ab7f554a-razor.md).

**Verdict context:** ship. Open items are post-merge probe/product hardenings — not ship-blockers for this tip.

---

## Open

- [ ] **F1** — `docs/probes/dogfood/hotel.poly:182-191` — Close `BookSection` hall/`section.hall` identity drift: require the passed hall is the section’s hall (policy/guard), **or** drop the `hall` parameter and initialize EventHold.`hall` from `section.hall` in create-in. Add a negative test for mismatched hall.

- [ ] **F2** — `docs/probes/dogfood/hotel.poly:96-98` — Guard `ClearChildBusy` so BusySections cannot go negative (`require` BusySections > 0 or equivalent). Aligns with known PR-body caveat; dogfood Occupy/Vacate path already stays non-negative.

- [ ] **F3** — `Poly.Tests/DomainModeling/HotelDogfoodTests.cs` — Add EventHold soft-create + Confirm `PartyFits` failure parity (oversized BookHall or BookSection), matching Reservation coverage at `:131-143`.

- [ ] **F4** — `docs/probes/dogfood/hotel.poly:315-320` (nit) — Comment that BookSection holds set both `hall` and `section`, so `StartSection` must not require `not HasHall` (asymmetry with `StartHall`’s `not HasSection` is intentional).

## Known caveats (track; do not re-open as PR58 blockers)

- [ ] **F5** — Exporter / product: last-writer `Occupied` on multi-subscription assign; `BusySections` underflow class (overlaps F2); public `Create` does not wire store subscribers. PR 58 dogs the wired BookStay/BookHall/BookSection + store path only. Keep on product backlog until exporter/runtime Create story is honest end-to-end.

## Out of scope this review

- PR 57 (`6cb49aba` LowerActionBody named-execute) present on full tip vs `origin/master` — already Razor ship-reviewed; re-open only if a hotel path regresses named execute.
