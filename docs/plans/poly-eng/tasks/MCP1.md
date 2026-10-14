# TASK MCP1 - ModelContextProtocol 2.x upgrade

Status: implemented (PR #171). Not admitted — [`board.md`](../board.md) is the queue.
Branch `slice/MCP1`. PR #171.
Plan card: package pin only. Not the gap-map Tier 4 MCP-hardening item (session isolation, resources, and notifications at [`../research/2026-10-12-ontology-of-ontologies-gap-map.md`](../research/2026-10-12-ontology-of-ontologies-gap-map.md)), which is the board THEN line "MCP mut-safety". Not in [`pipeline-convergence-plan.md`](../../../domain-modeling/pipeline-convergence-plan.md).

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
| stdio handshake, legacy client | `1.4.0` server | `2.2.0` server negotiates `2025-11-25`, lists 24 tools | Tests: legacy `initialize` then `tools/list` |
| `initialize` with `2026-07-28` | n/a | `-32022`; supported versions unchanged | Tests: `initialize` with `2026-07-28` |

## Done when
- `ModelContextProtocol` is `2.2.0`. `Poly.Mcp` and `Poly.Tests` build with zero warnings under `TreatWarningsAsErrors`. No `MCP*` obsoletion diagnostics.
- Full suite green.
- stdio `initialize` with `protocolVersion` `2025-11-25` succeeds, and the following `tools/list` returns 24 tools.
- stdio `initialize` with `protocolVersion` `2026-07-28` returns JSON-RPC `-32022`, with supported versions `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25`.

## SHIP if / NOT SHIP if
SHIP if those four Done-when checks pass. NOT SHIP if any code change beyond the version string is needed (that is a real breaking surface and this slice must be re-scoped), or if a `2025-11-25` client can no longer complete `initialize`.

## Tests
```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet build Poly.Tests/Poly.Tests.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
stdio, against the built `Poly.Mcp` server:
- `initialize` with `protocolVersion` `2025-11-25`, then `tools/list` — 24 tools.
- `initialize` with `protocolVersion` `2026-07-28` — error `-32022`, supported versions `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25`.

## Hand-edit
The only change is a version string; Scot can bump or roll it back cold.

## Needs Scot
none (admission is the board's call).

## Follow-on (not this slice)
MCP2 - the harness/session refactor: DI (`ISessionStore` singleton + instance tools + `ILogger<T>`), one exception boundary that keeps the response envelope, per-session locking or an immutable state swap, and idle-TTL eviction. Session identity stays the id `McpSessionStore` already uses (`Create` / `TryGet` take `sessionId`). This process is stdio (`WithStdioServerTransport` in `Poly.Mcp/Program.cs`); it has no `Mcp-Session-Id` header, and MCP2 must not re-key isolation onto one. `subscriptions/listen` (2.1) can be its own door. Tasks (2.0) is on the wire only after negotiating `2026-07-28`, which this server's `initialize` does not offer.

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-13 | agent | - | - | planned (spike) | 2.2.0 builds clean, 0 warnings; legacy `2025-11-25` handshake lists 24 tools; `2026-07-28` is discovery-first |
| 2026-10-13 | agent | OpenCode (hand) | `b8f1ee7e` | pushed | bump `1.4.0` -> `2.2.0`; `Poly.Benchmarks` build succeeded; suite 3609/3609; stdio smoke `2025-11-25` -> 24 tools |
| 2026-10-13 | agent | grok | - | review | gates name the same four checks; this pin is not gap-map MCP hardening; MCP2 keeps `McpSessionStore` session ids |
