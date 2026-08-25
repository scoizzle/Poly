# Round 'round5' — probe baseline sweep

Run: 2026-08-12T20:31Z — every probe through scripts/run-probe.sh
(parse → export → Roslyn compile-check, 0 errors/0 warnings gate).

| probe | result | status |
|-------|--------|--------|
| `probes/discovery-a/ecommerce.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-a/issue-tracker.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-a/library.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/assign-param.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/constraints.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/enum-nonmember.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/f6-ranges.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/f7-multiinit.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/length-open.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-agent-b/pattern-nontext.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-agent-b/subscriptions.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-b/audit.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-b/bookings.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-b/loans.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/accounts.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/catalog.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/enum-literal.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/inventory.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-dates/date-edges.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-dates/date-now-confusion.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-dates/date-rejects.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-dates/dates.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-dates/guid-on-text.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-for/export-edges.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-for/store-predicate.poly` | errors: ? | FAIL(no-result) |
| `probes/discovery-xinvoke/invoke-edges.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-xinvoke/invoke-orders.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-xinvoke/subscriptions.poly` | errors: 0, warnings: 0 | PASS |

Sweep: 20 pass, 0 fail.
Failing probes are this round's compile-fail targets. Agents should ALSO hunt
non-compile findings (export/runtime divergence, silent gaps, guide drift) per
docs/agent/poly-discovery-loop.md.
