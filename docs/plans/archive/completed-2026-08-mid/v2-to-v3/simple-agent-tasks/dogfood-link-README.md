# Link/Unlink Runtime + MCP Slice (`dogfood-link-*`)

**Parent synthesis:** [`../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md`](../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md)  
**Source findings:** DOGFOOD-S2 (S2-B1 no unlink, S2-B2 create-in store reg, S2-B3 get_instance navs)  
**Discovery queue:** [`dogfood-README.md`](dogfood-README.md)

Complete the runtime graph model: unlink, instance registration, and navigation visibility.

---

## How to pick

1. First `[ ]`.  
2. Open its task file + Required Reading only.  
3. Implement → verify → mark `[x]`.  
4. One micro-task per turn.

---

## Agent pick

```text
DONE:    link-1 — unlink_instances MCP tool + tests
DONE:    link-2 — create-in children registered in InstanceMap
DONE:    link-3 — nav property IDs in get_instance
CURRENT: (S-tier complete — link-4/5 deferred)
THEN:    link-4 (DSL link/unlink effect syntax), link-5 (runtime eval for link/unlink)
PULL:    —
```

---

## Micro-tasks

| ID | File | Status | Diff | Prereq |
|----|------|--------|------|--------|
| **link-1** | [`dogfood-link-1-unlink-instances.md`](dogfood-link-1-unlink-instances.md) | `[x]` | S | — |
| **link-2** | [`dogfood-link-2-createin-store-registration.md`](dogfood-link-2-createin-store-registration.md) | `[x]` | S | link-1 |
| **link-3** | [`dogfood-link-3-get-instance-navs.md`](dogfood-link-3-get-instance-navs.md) | `[x]` | S | link-1 |
| **link-4** | [`dogfood-link-4-dsl-link-effect.md`](dogfood-link-4-dsl-link-effect.md) | `[ ]` | M | link-1+2+3 |
| **link-5** | [`dogfood-link-5-link-unlink-runtime.md`](dogfood-link-5-link-unlink-runtime.md) | `[ ]` | M | link-4 |

---

## After slice

Return pick to synthesis triage or next concept cluster.
