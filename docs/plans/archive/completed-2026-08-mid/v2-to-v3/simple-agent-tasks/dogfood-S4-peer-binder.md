# Dogfood S4 — Peer binder (`when Rel Stage as name`)

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Wave:** 2 (shipped SPE / peer surface)  
**Status:** `[x]` PASS — [report](../agent-summaries/dogfood/DOGFOOD-S4-20260806.md)  
**Difficulty:** Medium  
**Prereq:** Wave 1 green enough; runtime tools enabled  
**Est. session time:** 45–75 min  

---

## Goal

Author and **run** a multi-entity domain where a subscriber uses **`when Rel Stage as name`**, a peer transitions into that stage, and the subscription body can refer to the peer via the binder name — all via MCP.

## Concept under test

**Peer binding:** analysis + runtime rewrite of binder path-prefix; product DSL form; store notify dispatches the plan.

---

## Domain sketch (minimum)

- **Order** and **Payment** (or Order / Shipment) — two entity types.  
- Relationship Order → Payment (to-one or OneToMany; prefer clear singular path for binder).  
- On Order (stage or entity — prefer stage-scoped `when` first):  
  `when payment Received as pay { assign … using pay … }` or equivalent guide-legal form.  
- Action on Payment that **transitions to Received**.  
- Success path: create Order + Payment, **link**, invoke Payment transition, observe Order side effect (property assign or stage change).

Use guide spelling only. If guide uses different relationship/nav names, follow the guide.

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain authors cleanly via MCP (`apply_dsl` / evolve); analysis errors = 0 | |
| 2 | DSL includes **`when … as <name>`** peer binder (not invented syntax) | |
| 3 | Runtime: create instances + `link_instances` so the relationship exists | |
| 4 | `invoke_action` on peer causes subscription to fire | |
| 5 | Observable effect uses peer binder (property from peer, or guide-legal path) | |
| 6 | `get_instance` / `evaluate_policy` / export shows honest outcome | |
| 7 | Report notes guide honesty for peer `as` (worked / lie / omit) | |

**PASS** = binder subscription fires on linked peer transition with observable effect.

**FAIL useful** = cannot author, cannot link, notify silent, binder unbound fail-open, or guide wrong — classify C/I/M/G/A/R.

---

## Forbidden workarounds

- Hand-calling library `NotifyTransition` outside MCP  
- Redesigning to avoid peer binder (plain `when` without `as` only)  
- Using multi-hop or dates to “make it work”  
- Fixing platform code in the discovery turn  

---

## Session steps (suggested)

1. `create_domain_session` + `get_dsl_guide` — find peer `as` / `when` examples  
2. `apply_dsl` minimal Order/Payment (or guide names)  
3. `get_domain_analysis` — note structured facts / diagnostics  
4. `create_instance` Order + Payment; `link_instances`  
5. `invoke_action` on Payment to enter target stage  
6. `get_instance` Order — assert side effect  
7. Report; stop  

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S4-YYYYMMDD.md`

Emphasize: **authorable?** **runtime fire?** **binder usable in body?** **guide honest?**

---

## Status tracking

**Claimed by:** Copilot pipeline supervisor  
**Started:** 2026-08-06  
**Report path:** `agent-summaries/dogfood/DOGFOOD-S4-20260806.md`  
**Result:** PASS  
