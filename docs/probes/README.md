# Live probe fixtures

Thin set of `.poly` domains used by tests, `scripts/run-probe.sh`, and `scripts/live-demo.sh`.

Historical discovery rounds: [`docs/plans/archive/probes-2026-08/`](../plans/archive/probes-2026-08/README.md).

| Path | Consumer |
|------|----------|
| `dogfood/university.poly` | `UniversityDogfoodTests` |
| `dogfood/crm.poly` | `CrmDogfoodTests` |
| `dogfood/hotel.poly` | `HotelDogfoodTests` |
| `dogfood/nested-invoke-type-mismatch.poly` | dogfood companion |
| `dogfood/simulate-create-{type,in,create-in}.poly` | `SimulateCreateDogfoodTests` + compile oracle |
| `fleet-eval/09-transport/{warehouse,orders,clinic}.poly` | compile oracle + `scripts/live-demo.sh` |
| `fleet-eval/12-mcp/mcp-library.poly` | compile oracle |
| `smoke/smoke.poly` | smoke |
| `grep-2026-10-17/grep.poly` | grep probe — `VmAnalyzerReportTests` canary + `ParityTests` `PrintedCSharpCompiles` (findings: [`findings/grep-2026-10-17.md`](findings/grep-2026-10-17.md)) |
| `produced-di/` | spike only ([decision](../decisions/2026-10-15-produced-code-dependency-injection.md)); `shop.poly.txt` is not a `*.poly` sample |

Author a new probe: `scripts/new-probe.sh <name>` → `docs/probes/<name>/<name>.poly`.  
Compile-check: `scripts/run-probe.sh docs/probes/<name>/<name>.poly`.
