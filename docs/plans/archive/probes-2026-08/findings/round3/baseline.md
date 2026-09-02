# Round 'round3' — probe baseline sweep

Run: 2026-08-12T03:25Z — every probe through scripts/run-probe.sh
(parse → export → Roslyn compile-check, 0 errors/0 warnings gate).

| probe | result | status |
|-------|--------|--------|
| `probes/discovery-a/ecommerce.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-a/issue-tracker.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-a/library.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/assign-param.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/constraints.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/enum-nonmember.poly` | errors: 1, warnings: 0 | FAIL |
| `probes/discovery-agent-b/f6-ranges.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/f7-multiinit.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/length-open.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/pattern-nontext.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/subscriptions.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-b/audit.poly` | errors: 8, warnings: 0 | FAIL |
| `probes/discovery-b/bookings.poly` | errors: 2, warnings: 0 | FAIL |
| `probes/discovery-b/loans.poly` | errors: 3, warnings: 0 | FAIL |
| `probes/discovery-c/accounts.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/catalog.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/enum-literal.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/inventory.poly` | errors: 0, warnings: 0 | PASS |

Sweep: 11 pass, 4 fail.
Failing probes are this round's compile-fail targets. Agents should ALSO hunt
non-compile findings (export/runtime divergence, silent gaps, guide drift) per
docs/agent/poly-discovery-loop.md.
