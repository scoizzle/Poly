# Mission LOG — evolutionary fact-finding 2026-10-13

Branch: `mission/evolve-2026-10-13` (worktree `/Users/scoizzle/Projects/Poly/mission-evolve-2026-10-13`, off master `a6203103`)
Agent start: Tue Oct 13 23:20 CDT. Budget: ~8h.

Archive baseline consulted: `docs/plans/archive/probes-2026-08/findings/` (rounds 2–5).
Known-bad from archive (to re-check as regressions): multi-hop PascalCase export; `Rel exists`
on many divergence; CS0162 unreachable after quantified invoke; CS8602 null deref; date
arithmetic lowering gaps (now partially shipped per guide §8); unique no-op; create-in-conditional
drop; for-binder args crash; entity-level policy gating.

| Time | Concept | Stages reached | Findings | Next |
|------|---------|----------------|----------|------|
| 00:40 | library | v1→v3, VM, round-trip, compile, host walk | F1 Today local/UTC split; F2 dup route→500; F3 loans/{id} ignored; F4 run-probe vs uses http; F5 live-demo NU1903; F6 unique⇒PK; F7-9 minor | ✅ done |
| 00:41 | university | | | next |
| 01:20 | university | v1, VM, round-trip, compile, host (dup-email proof) | F10 second unique dropped; F11 Program entity breaks http; F12 selective constraint projection; archive F1 fixed | ✅ done |
| 01:20 | smarthome | | | next |
| 02:30 | smarthome | v1→v2, VM (fail-fast, any/all, binder args, defaults, entry/exit), round-trip, compile, export review | F13 annotations unreachable; F14 partial-state + subscription cross-fire | ✅ done |
| 02:30 | billing | | | next |
| 03:45 | billing | v1→v2, VM, round-trip (broken), compile 0/0, export review | F15 Number=long truncation; F16 VM crash if+create-in ≥256; F17 default(Now) round-trip break; F18 Text-name initializer | ✅ done |
| 03:45 | claims | | | next |
| 04:30 | claims | v1, VM, compile gate (caught F19), export review, round-trip | F19 C# keyword names break export | ✅ done |
| 04:30 | disputes | | | next |
| 05:15 | disputes | v1, VM, gate (6 warnings), round-trip, export review | F20 value-type warnings; F21 enum-member expression gap; F22 value sub-field gap | ✅ done |
| 05:15 | saas | | | next |
| 05:55 | saas | v1 (3 failures: Date<Now, NRE, aggregate warn) → fixed shapes, VM clean, gate 0/0 | F23 Date<Now passes analysis; F24 aggregate-root heuristic; F25 NRE on unset date | ✅ done |
| 01:15 | ambig-backref | scratch probe: VM + export | guide contract verified: ambiguous back-refs stay unset, link wired | ✅ note only |
| 01:15 | wrap | archive regressions checked (10 items), rankings, PR | 7 concepts, 25 findings, 0 abandonments | done |
| 01:20 | ship | PR opened: https://github.com/scoizzle/Poly/pull/173 | blockers: none · abandoned: none | ✅ |
| 2026-10-14 | address | all 25 findings fixed on this branch | see findings file "Addressed" table | local, not pushed |
