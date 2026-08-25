# Owned/Nested Access Build Slice (`dogfood-owned-*`)

**Parent synthesis:** [`../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md`](../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md)  
**Source finding:** S3-B1 — OwnedAccess expression path IR-only, guide says "Pull"  
**Discovery queue:** [`dogfood-README.md`](dogfood-README.md)

Ship owned/nested field access in policies and effects — both DSL path-prefix and JSON `add_policy` form.

---

## How to pick

1. First `[ ]`.  
2. Open its task file + Required Reading only.  
3. Implement → verify → mark `[x]`.  
4. One micro-task per turn.

---

## Agent pick

```text
DONE:    owned-1 — guide honesty + path-prefix DSL confirmed working
DONE:    owned-2 — JSON expression format with "relationship" key
DONE:    owned-3 — to-one RelationshipNavigation resolution in EvaluatePolicy
THEN:    S3 re-run (after MCP server restart)
```

---

## Micro-tasks

| ID | File | Status | Diff | Prereq |
|----|------|--------|------|--------|
| **owned-1** | [`dogfood-owned-1-guide-honesty.md`](dogfood-owned-1-guide-honesty.md) | `[x]` | S | — |
| **owned-2** | [`dogfood-owned-2-json-path-prefix.md`](dogfood-owned-2-json-path-prefix.md) | `[x]` | S | — |
| **owned-3** | [`dogfood-owned-3-runtime-smoke.md`](dogfood-owned-3-runtime-smoke.md) | `[x]` | S | owned-2 |

---

## After slice

S3 re-run if owned-1..3 pass; otherwise triage failures.
