# MCP Mutation Safety & Diagnostics

**Date:** 2026-07-13  
**Status:** Proposal — research + implementation plan  
**Source:** Agent feedback from ~150-call supply chain modeling session  
**Related:** `mcp-tool-surface-expansion.md` (diff_state), `mcp-guiding-principles.md` (rollback safety)

---

## Problem summary

Three mutation-safety issues were discovered during an agent-driven domain modeling session:

1. **Parallel calls cause silent data loss (CRITICAL).** Concurrent `add_property`/`add_stage`/`add_action` calls trigger rollbacks that lose data on *some* entities while leaving others intact. Error messages are misleading ("Entity X not found" instead of "rollback due to concurrent mutation"). The agent is forced to go fully sequential and manually reconstruct lost work.

2. **No idempotency on structural mutations.** Re-adding an existing stage, property, or action triggers a rollback instead of being a no-op (or returning a clear 409 AlreadyExists). This punishes recovery attempts after a partial failure.

3. **Rollback diagnostics are opaque.** When a rollback occurs, there is no payload describing what was lost vs. what survived. The agent must call `get_entity_detail` on every entity after every failure to reconstruct state.

### Stage ordering corollary

Rollbacks also cause stages to return in non-logical order (e.g., `Blacklisted` appearing before `Active` on `Supplier`). The domain should preserve insertion order through rollbacks.

---

## Relationship to existing work

| Existing doc | Overlap | Gap |
|-------------|---------|-----|
| `mcp-tool-surface-expansion.md` | Proposes `diff_state` tool for before/after comparison | `diff_state` covers one use case but doesn't address the root cause (concurrency safety) or idempotency |
| `mcp-guiding-principles.md` | "Rollback on failure → safe agent loops" | Assumes rollbacks are safe and informative; this feedback shows they aren't |
| `future-platform-capabilities.md` | Mentions domain model diffs "partially supported via MCP tools" | No concrete diff payload exists |
| `McpSessionStore.cs` | Uses `ConcurrentDictionary` with monotonically increasing `Revision` | No concurrency control beyond the revision counter — parallel writes race |

---

## Research

### Root cause identified: lost-update race in `Evolve()` helper

The `Evolve` helper in `DomainTools.cs` (line ~382) performs an unprotected read-modify-write cycle:

```
1. McpSessionStore.TryGet(sessionId, out state)   // NO LOCK — reads current state
2. DomainEvolution(state.Domain).Evolve()...Apply() // mutates a copy
3. McpSessionStore.Update(sessionId, newRoot, ...)  // LOCKS — writes back
```

`McpSessionStore.TryGet` (line ~54 of `McpSessionStore.cs`) does `Sessions.TryGetValue()` **without acquiring `StoreLock`**. Only `Create` and `Update` acquire the lock. This means:

- Two concurrent `add_property` calls both read the same `state.Domain` (step 1)
- Both evolve independently from that base (step 2)
- First call writes its result, bumping revision to N+1 (step 3)
- Second call **blindly overwrites** with its result, bumping revision to N+2
- The first call's property addition is **silently lost** — no error, no conflict detection

The misleading "Entity X not found" errors occur when the second call's evolution references entities that the first call created (and which were subsequently overwritten/lost by the second call's write).

### Remaining research questions

1. **Concurrency model.** Two approaches:
   - **Pessimistic:** Move `TryGet` inside the `StoreLock`, or add a per-session `SemaphoreSlim(1,1)`. Simplest fix — serialize all mutations per session. Reads stay lock-free.
   - **Optimistic:** Pass expected `Revision` with every mutation; `Update` rejects if current revision doesn't match. More complex but better diagnostics (clear 409 Conflict).

2. **Idempotency semantics.** Should re-adding an existing element be a no-op (return success) or a 409 Conflict? No-op is safer for agent recovery; include `was_noop: true` in response.

3. **Rollback diff design.** What goes in the rollback diagnostics payload? Minimum: attempted change list, failure reason per change, surviving entity summary (entity names with property/stage/action counts).

4. **Stage ordering.** Where does ordering break — in `DomainEvolution`, the `Stage` collection, or rollback reconstruction? Inspect `Entity.Stages` type and `DomainEvolution` merge logic.

---

## Proposed implementation

### Phase 1: Concurrency safety (critical path)

Add a per-session write lock in `McpSessionStore`:

```csharp
// McpSessionStore.cs
private readonly ConcurrentDictionary<string, SessionGuard> _guards = new();

private sealed class SessionGuard {
    public McpSessionState State;
    public readonly object WriteLock = new();
}
```

Every mutation tool acquires `_guards[sessionId].WriteLock` before reading state, evolving, and writing back. Read-only tools (`get_*`) do not acquire the lock.

**Alternatives to evaluate:**
- `SemaphoreSlim(1, 1)` per session instead of `lock` — supports async, but all MCP tool handlers are synchronous today
- Revision-token approach: each evolve tool returns new revision; next evolve must include expected revision

### Phase 2: Idempotent structural operations

Add existence checks before evolving:

| Tool | Check | Behavior on duplicate |
|------|-------|----------------------|
| `add_entity` | Entity name already exists | Return success with existing entity (no-op) |
| `add_property` | Property name already exists on entity | Return success (no-op) |
| `add_stage` | Stage name already exists on entity | Return success (no-op) |
| `add_action` | Action name already exists on entity | Return success (no-op) |
| `add_action_to_stage` | Action already on stage | Return success (no-op) |
| `add_relationship` | Relationship name already exists | Return success (no-op) |

This is safe because the domain is immutable — re-adding an identical structural element produces the same result.

### Phase 3: Rollback diagnostics

When `DomainEvolution.Apply()` fails:
- Include `AttemptedChanges` (what was requested)
- Include `SurvivingState` (what entities/stages/properties exist post-rollback)
- Include a human-readable `FailureSummary` explaining which change caused the failure

Design a `RollbackDiagnostics` record attached to the error response:

```json
{
  "error": "rollback",
  "message": "Analysis failure on add_stage 'Blacklisted' for entity 'Supplier'",
  "rollback_diagnostics": {
    "attempted_changes": ["add_stage(Supplier, Blacklisted)", "add_action(Supplier, Blacklist)"],
    "failed_change": "add_stage(Supplier, Blacklisted)",
    "analysis_errors": ["Stage 'Blacklisted' references undefined parent stage 'Suspended'"],
    "surviving_entities": {"Supplier": ["Active", "Suspended"], "Order": ["Draft", "Confirmed"]}
  }
}
```

### Phase 4: Stage ordering preservation

Ensure `DomainEvolution` preserves insertion order of stages through rollback. If the underlying collection is a dictionary/hash set, change to an ordered representation (e.g., `ImmutableArray` or `ImmutableList` with ordered merge).

---

## Acceptance criteria

1. **Parallel safety:** Two concurrent `add_property` calls on the same session do not cause data loss. The second call either waits for the first or gets a clear 409 Conflict.
2. **Idempotency:** Calling `add_stage("Order", "Draft")` twice returns success both times; the second call is a no-op.
3. **Rollback diagnostics:** A failed `add_action_to_stage` returns which change failed, why, and what survived.
4. **Stage ordering:** After a rollback, `get_entity_detail("Supplier")` returns stages in insertion order (`Active`, `Suspended`), not alphabetically or in random order.
5. **No regression:** Existing 15 MCP tools all pass `McpSmokeTests` without modification.
6. **Agent-verifiable:** The domain-modeling agent can recover from a rollback without calling `get_entity_detail` on every entity.

---

## Risks

- **Write lock contention:** If a human UI and agent share a session, writes serialize. Acceptable for current usage (single agent per session); document the limitation.
- **Idempotency masking real errors:** If the agent accidentally re-adds a stage thinking it's new, the no-op silently succeeds instead of alerting. Mitigation: include a `was_noop: true` flag in the response so the agent can detect this.
- **Rollback diagnostics payload size:** Large domains could produce large diff payloads. Mitigation: cap entity-level detail at entity names + property/stage/action counts, not full property definitions.

---

## Related plans

| Plan | Relationship |
|------|-------------|
| [`mcp-batch-snapshot-efficiency.md`](mcp-batch-snapshot-efficiency.md) | Batch operations reduce the frequency of parallel-call races; snapshot export reduces inspection calls after rollbacks |
| [`mcp-domain-inspection-completeness.md`](mcp-domain-inspection-completeness.md) | Relationship/constraint tools benefit from the same idempotency and rollback safety guarantees |
| [`mcp-tool-surface-expansion.md`](v2-to-v3/mcp-tool-surface-expansion.md) | `diff_state` tool proposed there is the observability side of what this plan fixes at the mutation layer |
