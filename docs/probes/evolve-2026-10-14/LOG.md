# Mission LOG — real-app core utilities 2026-10-14

Branch: `mission/evolve-2026-10-14` (worktree `/Users/scoizzle/Projects/Poly/mission-evolve-2026-10-14`, off master `bd416609` "CPM1: central package management (#172)")
Agent start: Wed Oct 14 06:50 CDT. Budget: ~8h. **Full send.**

Prior mission (evolve-2026-10-13, PR #173) covered the domain-modeling staples; THIS mission targets
real-app core utilities and the seams it did NOT probe:
stage-scoped subscriptions · owned navs · §12 incremental authoring (add/remove) · introspection
tools · fail-closed negative shapes · DateTime/Time/Duration/Guid types · sum-of-related
expressiveness · reverse-direction linking.

Prior findings NOT to re-report (open on master): F1 Today local/UTC, F2/F3 API dup route + {id},
F4 run-probe vs http, F5 live-demo NU1903, F6/F10 unique⇒PK/drop, F7 hints, F8 stage msg, F9
demo.http, F11 Program entity, F12 projection matrix, F13 annotations, F14 partial fan-out, F15
long truncation, F16 VM ≥256 crash, F17 default(Now) round-trip, F18 Text-name init, F19 C#
keywords, F20 value-type warnings, F21 enum member expr, F22 value sub-field, F23 Date<Now,
F24 aggregate heuristic, F25 unset-date NRE.
Fix watch: master was bd416609 at start; re-run quick repros at wrap if master moved.

| Time | Concept | Stages reached | Findings | Next |
|------|---------|----------------|----------|------|
| 07:55 | ratelimit | v1→v4 VM + gate + round-trip + lowering source read | F26 policy-eval drops DateOperation fold (rate limiter no-op); multi-require ✓, Boolean default ✓, ISO DateTime create ✓ | ✅ done |
| 07:55 | notifications | | | next |
| 08:40 | notifications | v1→v5 VM: stage-scoped placement ✓, create-in no-fire, double-fire matrix | F27 double-fire regression (create-in vs tool-link); F28 no-fire-on-create | ✅ done |
| 08:40 | jobqueue | | | next |
