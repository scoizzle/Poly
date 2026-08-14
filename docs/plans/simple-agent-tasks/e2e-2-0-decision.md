# e2e-2-0 — Lock A/B + 05-F5

**Difficulty:** S  
**Status:** `[ ]`  
**No product code.**

## Objective

Write `docs/plans/simple-agent-tasks/e2e-2-decision-lock.md` with **one** choice:

| Option | When |
|--------|------|
| **A** | Lower Q3′ in export against in-memory navs (LINQ/`Count`) |
| **B** | Do not prepend store-only policies as action guards; fail export/analysis if the action’s meaning requires them |

**Default (parent):** A when the quantifier target is an owned/in-graph navigation on the entity; B for store-only graph walks. Name the cases.

Also decide **05-F5**: keep entity-level policies gating every action (document as modeling) **or** move to per-action `require`. Do not invent a third rule.

## File ownership

| Edit | Do not edit |
|------|-------------|
| `e2e-2-decision-lock.md` | `Poly/**` |

## Status

**Status:** Not Started  
**Claimed by:**  
