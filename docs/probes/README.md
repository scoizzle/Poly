# Live probe fixtures

Thin set of `.poly` domains used by tests, `scripts/run-probe.sh`, and `scripts/live-demo.sh`.

Historical discovery rounds: [`docs/plans/archive/probes-2026-08/`](../plans/archive/probes-2026-08/README.md).

| Path | Consumer |
|------|----------|
| `dogfood/university.poly` | `UniversityDogfoodTests` |
| `dogfood/crm.poly` | `CrmDogfoodTests` |
| `dogfood/nested-invoke-type-mismatch.poly` | dogfood companion |
| `fleet-eval/09-transport/{warehouse,orders,clinic}.poly` | compile oracle + `scripts/live-demo.sh` |
| `fleet-eval/12-mcp/mcp-library.poly` | compile oracle |
| `smoke/smoke.poly` | smoke |

Author a new probe: `scripts/new-probe.sh <name>` → `docs/probes/<name>/<name>.poly`.  
Compile-check: `scripts/run-probe.sh docs/probes/<name>/<name>.poly`.
