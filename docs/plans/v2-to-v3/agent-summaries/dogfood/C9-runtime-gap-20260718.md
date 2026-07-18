# C9 — Runtime Gap Confirmation

**Date:** 2026-07-18
**Status:** ✅ Complete — runtime gap confirmed and documented

## Investigation

Searched for MCP runtime tools and APIs that would allow:

1. **CallAction** — invoke an action on a domain entity instance
2. **DomainInstanceStore** — create, list, or manage entity instances
3. **Stage subscriptions** — auto-fan-out when entities change stage
4. **Domain entity lifecycle** — live state machine execution

## Evidence

### MCP tool surface (checked against registered tools)
The following tools would be expected in a Runtime MCP surface but do **not exist**:
- `call_action` or `invoke_action` — ❌ not found
- `create_instance` or `create_entity_instance` — ❌ not found  
- `list_instances` — ❌ not found
- `get_instance_state` or `get_instance_stage` — ❌ not found
- `subscribe_stage` — ❌ not found
- `observe_subscription` — ❌ not found

### Core library API (Poly/DomainModeling/)
The following runtime machinery exists in the **core library** but has **no MCP tools**:

| API | Location | Has MCP tool? |
|-----|----------|---------------|
| `DomainEntityInstance.Create(...)` | `Poly/DomainModeling/DomainEntityInstance.cs` | ❌ |
| `DomainEntityInstance.EvaluatePolicy(...)` | `Poly/DomainModeling/DomainEntityInstance.cs` | ❌ (evaluate_policy uses it internally) |
| `DomainInstanceStore` | `Poly/DomainModeling/DomainEntityInstance.cs` | ❌ |
| `Stage subscriptions` (parsed+stored) | Domain model | ❌ (no runtime enforcement) |

### Honesty Notes already in apply_dsl
The `apply_dsl` tool description already documents the gap honestly:
> "Action `when Stage` is parsed and stored but NOT runtime-enforced"
> "Stage subscriptions are parsed and stored but do NOT auto-fan-out"

## Findings

### C9-F1: Runtime MCP — No CallAction tool (Category: R)
- **PainScore:** 18 (S=5 F=2 B=5 C=3)
- Models that use lifecycle stages, actions with effects, or stage subscriptions cannot be exercised through MCP.
- **Severity = 5** (Cannot complete real task — runtime execution is the goal of domain modeling)
- **Blocker = 5** (No workaround exists in MCP — would need custom C# code)
- **Bucket:** Runtime-MCP

### C9-F2: Runtime MCP — No instance management (Category: R)
- **PainScore:** 16 (S=4 F=3 B=4 C=2)
- Cannot create entity instances, observe state transitions, or validate lifecycle behavior through MCP.
- **Bucket:** Runtime-MCP

### C9-F3: Runtime MCP — Stage subscriptions in model only (Category: R)
- **PainScore:** 14 (S=4 F=2 B=4 C=2)
- Subscriptions are parsed and stored in the domain model but have no executor.
- The apply_dsl honesty note covers this, but it's a significant gap for any agent expecting "when" to work.
- **Bucket:** Runtime-MCP

## Honesty assessment
The `apply_dsl` and tool descriptions are **honest** about the runtime gap.
No overclaiming detected. The gap is documented but not hidden.

## Conclusion
Runtime MCP is **confirmed absent**. This is the single largest gap in the
MCP surface for any agent expecting to exercise spawn-and-wire behavior.
