# Round 'round2' — probe baseline sweep

Run: 2026-08-11T19:55Z — every probe through scripts/run-probe.sh
(parse → export → Roslyn compile-check, 0 errors/0 warnings gate).

| probe | result | status |
|-------|--------|--------|
| `probes/discovery-a/ecommerce.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-a/issue-tracker.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-a/library.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-b/audit.poly` | errors: 8, warnings: 0 | FAIL |
| `probes/discovery-b/bookings.poly` | errors: 2, warnings: 0 | FAIL |
| `probes/discovery-b/loans.poly` | errors: 3, warnings: 0 | FAIL |
| `probes/discovery-c/accounts.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/catalog.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/enum-literal.poly` | errors: 0, warnings: 0 | PASS |
| `probes/discovery-c/inventory.poly` | errors: 0, warnings: 0 | PASS |

Sweep: 7 pass, 3 fail.
Failing probes are this round's compile-fail targets. Agents should ALSO hunt
non-compile findings (export/runtime divergence, silent gaps, guide drift) per
docs/agent/poly-discovery-loop.md.
