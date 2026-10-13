# TASK MCP1 - ModelContextProtocol 2.x upgrade

Status: in review (proposal; not admitted - [`board.md`](../board.md) is the queue).
Branch `slice/MCP1`. PR #171. Lane TBD. Review TBD. Implement mill: TBD (review on the opposite mill).
Plan card: proposed from [`../research/2026-10-12-ontology-of-ontologies-gap-map.md`](../research/2026-10-12-ontology-of-ontologies-gap-map.md) (Tier 4, MCP hardening); not in [`pipeline-convergence-plan.md`](../../../domain-modeling/pipeline-convergence-plan.md).

## Scope
- `Poly.Mcp/Poly.Mcp.csproj:17` pins `ModelContextProtocol` `1.4.0`. Current line is `2.2.0` (2026-08-13); `2.0.0` implements the MCP `2026-07-28` revision (stateless-by-default HTTP, discovery-first negotiation, extensions first-class, Roots/Sampling/Logging deprecated). The SDK supports one previous MAJOR for critical fixes only, so staying on 1.4 costs the whole v2 surface.
- Spike (throwaway worktree off master `89fa8eb5`): bump to `2.2.0` -> `dotnet build Poly.Mcp/Poly.Mcp.csproj` and `dotnet build Poly.Tests/Poly.Tests.csproj` **succeed with zero errors and zero warnings** (no `MCP*` obsoletion diagnostics under `TreatWarningsAsErrors`). **No source change is required.**
- Wire: the v2.2.0 stdio server negotiates a legacy `2025-11-25` `initialize` and returns all 24 tools. `2026-07-28` is not available through `initialize` (discovery-first; `supported: 2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25`) - expected, and no impact for stdio/current clients.
- Therefore this slice is a pinned version bump plus a conformance smoke. Keep it separate from the harness/session work (MCP2) so a dependency bump cannot hide a behavior change.

## Files
- `Poly.Mcp/Poly.Mcp.csproj` (version `1.4.0` -> `2.2.0`)
- `docs/plans/poly-eng/tasks/MCP1.md` (this file)

## Shape matrix
| input kind | before | after | test that proves it |
|---|---|---|---|
| Build (`Poly.Mcp`, `Poly.Tests`) | `1.4.0` | `2.2.0`, warning-free | `dotnet build` clean; no `MCP*`/`CS*` |
| stdio handshake, legacy client | `1.4.0` server | `2.2.0` server negotiates `2025-11-25`, lists 24 tools | stdio smoke (below) |
| `initialize` with `2026-07-28` | n/a | `-32022` (discovery-first); client behavior unchanged | stdio smoke (below) |

## Done when
- `ModelContextProtocol` is `2.2.0` and both builds are warning-free (repo `TreatWarningsAsErrors`).
- Full suite green.
- `opencode mcp list` shows `poly-local connected` after `scripts/restart-poly-mcp.sh`, and a real tool round-trip succeeds (`create_domain_session` -> `apply_dsl` -> `get_domain_analysis`).
- No `MCP*` obsoletion diagnostics anywhere.

## SHIP if / NOT SHIP if
SHIP if the builds are warning-free, the suite is green, and the stdio smoke lists 24 tools on a `2025-11-25` handshake. NOT SHIP if any code change beyond the version string is needed (that would mean a real breaking surface and this slice must be re-scoped), or if a client can no longer connect.

## Tests
```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
# stdio smoke: legacy initialize -> tools/list must report 24 tools (see Log for the exact command)
scripts/restart-poly-mcp.sh && opencode mcp list
```

## Hand-edit
The only change is a version string; Scot can bump or roll it back cold.

## Needs Scot
none (admission is the board's call).

## Follow-on (not this slice)
MCP2 - the harness/session refactor from the 2026-10-13 review: DI (`ISessionStore` singleton + instance tools + `ILogger<T>`), one exception boundary that keeps the response envelope, per-session locking or immutable state swap, identity-scoped sessions, idle-TTL eviction, and the `subscriptions/listen` (2.1) + Tasks (2.0) doors. Re-scope it against the `2026-07-28` model: there is no `Mcp-Session-Id` to scope on, so isolation must key on request identity.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-13 | agent | - | - | planned (spike) | 2.2.0 builds clean, 0 warnings; legacy `2025-11-25` handshake lists 24 tools; `2026-07-28` is discovery-first |
| 2026-10-13 | agent | OpenCode (hand) | `b8f1ee7e` | pushed | bump `1.4.0` -> `2.2.0`; `Poly.Benchmarks` build succeeded; suite 3609/3609; stdio smoke `2025-11-25` -> 24 tools |
