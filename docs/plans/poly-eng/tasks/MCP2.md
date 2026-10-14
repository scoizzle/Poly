# TASK MCP2 - MCP harness: DI, session locking, eviction, one exception boundary

Status: in review (proposal; not admitted - [`board.md`](../board.md) is the queue).
Branch `slice/MCP2`. PR #174. Lane TBD. Review TBD. Implement mill: OpenCode (hand).
Plan card: [`MCP1.md`](MCP1.md) Follow-on (MCP2).

## Scope
- `Poly.Mcp/Sessions/McpSessionStore.cs` was a static class with one process-wide lock held across every mutation; `Poly.Mcp/Tools/*` were static classes, so the host's DI container was unused and there was no logger anywhere in `Poly.Mcp`.
- Introduce `ISessionStore` (singleton) and make `McpSessionStore` an instance with **one lock per session** (`ConcurrentDictionary<string, Entry>`, `Entry.Sync`). Reads stay lock-free; `TryGet` marks the session used. A slow mutation in one session no longer blocks another.
- **Idle-TTL eviction**: `McpSessionOptions` (`Mcp:Sessions:IdleTimeout` default 8h, `SweepInterval` default 15m; zero disables) plus `SessionEvictionService` (`BackgroundService`). `TimeProvider` is injected so the policy is testable.
- **Instance tools**: the seven tool classes take `(ISessionStore store, ILogger<T> logger)`; `Program.cs` registers the store and the hosted service. Session identity stays the `sessionId` argument - this process is stdio and has no `Mcp-Session-Id` header.
- **One exception boundary**: `WithRequestFilters(filters => filters.AddCallToolFilter(ToolExceptionBoundary.Filter))` turns an uncaught exception into `IsError` carrying the `DomainToolResponse` envelope (`success:false`, message, diagnostics) instead of a bare protocol error.
- Not in this slice: `subscriptions/listen` (2.1), Tasks (2.0), an HTTP transport. Tasks is on the wire only after negotiating `2026-07-28`, which this server's `initialize` does not offer.

## Files
- `Poly.Mcp/Sessions/ISessionStore.cs` (new)
- `Poly.Mcp/Sessions/McpSessionStore.cs` (instance; per-session locks; eviction)
- `Poly.Mcp/Sessions/McpSessionOptions.cs` (new)
- `Poly.Mcp/Sessions/SessionEvictionService.cs` (new)
- `Poly.Mcp/ToolExceptionBoundary.cs` (new)
- `Poly.Mcp/Tools/{DomainTools,OracleTool,RuntimeTool}.cs` (instance tools; `store.`; one log call per class)
- `Poly.Mcp/Program.cs` (DI, options, hosted service, filter)
- `Poly.Mcp/README.md` (Harness Runtime section)
- `Poly.Tests/TestHelpers/McpTestTools.cs` (new composition root)
- `Poly.Tests/Mcp/McpSessionStoreTests.cs` (new)
- `Poly.Tests/**` (tool call sites become `McpTestTools.*`)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Slow mutation on session A | holds the one global lock; B blocks | B mutates while A is held | `McpSessionStoreTests.TryModifyInstances_DifferentSessions_DoNotBlockEachOther` |
| Session idle > IdleTimeout | kept forever | evicted on the sweep; touched sessions kept | `EvictIdle_RemovesIdleSession_KeepsTouchedSession` |
| IdleTimeout = 0 | n/a | eviction disabled | `EvictIdle_WhenTimeoutDisabled_DoesNotEvict` |
| Uncaught tool exception | bare protocol error, no envelope | `IsError` + `DomainToolResponse` JSON | `ToolExceptionBoundary_Failure_CarriesEnvelope` |
| stdio client | 24 tools, envelope responses | unchanged | stdio smoke: 24 tools; `create_domain_session` returns `success:true` |

## Done when
- `McpSessionStore` is an instance registered as `ISessionStore`; no static session state.
- Tool classes are instance classes with `ISessionStore` + `ILogger<T>`; `git grep "McpSessionStore\." -- Poly.Mcp/Tools` is empty.
- Idle eviction works with an injected `TimeProvider`.
- Full suite green; the stdio smoke lists 24 tools and a real call returns the envelope.

## SHIP if / NOT SHIP if
SHIP if the suite is green, the smoke lists 24 tools with an envelope response, and the grep is empty.

NOT SHIP if a tool method stays static, if a global lock returns, if eviction cannot be tested (no `TimeProvider`), or if a thrown tool call loses the envelope.

## Tests
```
dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/McpSessionStoreTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/McpSmokeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
# stdio smoke: initialize 2025-11-25 -> tools/list 24 -> create_domain_session success
```

## Hand-edit
One store class, one options class, one filter, and a primary constructor per tool; Scot can open each cold.

## Needs Scot
none (admission is the board's call).

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-14 | agent | OpenCode (hand) | `06ce2216` | pushed | suite 3613/3613; stdio smoke 24 tools + envelope; per-session lock, eviction, boundary tested |
