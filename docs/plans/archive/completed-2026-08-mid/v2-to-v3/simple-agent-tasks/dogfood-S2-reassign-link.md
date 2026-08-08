# Dogfood S2 — Reassign via linking existing instances

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Status:** `[x]` PASS -- see [re-run](../agent-summaries/dogfood/DOGFOOD-S2-RERUN-20260725.md)  
**Difficulty:** Medium–Hard  
**Prereq:** Prefer S1 done once (baseline works); can run standalone if agent is strong  
**Est. session time:** 45–75 min  

---

## Goal

Model a domain where an existing **child/work item** must be **moved** from one parent to another using **link/unlink of existing instances** — not only `create in` spawn-and-wire.

## Concept under test

**Graph identity beyond create-in:** connecting two pre-existing instances; optionally detaching the old link.

This is the primary signal for whether **link/unlink** must become product DSL or stay MCP/library-only.

---

## Domain sketch (minimum)

- **Patron** (or Worker) — two instances will exist (A and B).  
- **Loan** (or Task) — one instance created under A.  
- Relationship Patron→Loan (OneToMany) or equivalent.  
- **Success path must reassign** Loan from Patron A to Patron B **without** deleting and recreating Loan if at all possible.  
- Prefer MCP `link_instances` / document if only library unlink exists.

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain authors cleanly on MCP (analysis errors = 0) | |
| 2 | Create Patron A, Patron B, and one Loan **already associated with A** (create-in or link) | |
| 3 | **Reassign** Loan to Patron B using product-accessible ops (MCP tools and/or DSL you discover) | |
| 4 | `get_instance` / list / evaluation shows Loan associated with B (or equivalent observable) | |
| 5 | Old association to A is gone or explicitly documented as multi-parent if model allows | |
| 6 | Report states clearly: worked with **link_instances** / needed **unlink** / impossible / workaround | |

**PASS** = reassignment of an **existing** Loan instance to another Patron without full recreate.

**FAIL useful** = cannot reassign without recreate → bucket **C** or **I** or **M** with evidence.

---

## Forbidden workarounds

- “Reassign” by `delete` + new `create in` Loan (destroys identity — note if only path)  
- Skipping runtime and only editing DSL text  
- Using internal test helpers / C# `DomainInstanceStore` unless documenting “MCP cannot” as the finding  
- Changing scenario to avoid two parents  

---

## Session steps (suggested)

1. Session + `get_dsl_guide` — search for link/unlink language  
2. Author domain; note absence of DSL `link` if still true  
3. Runtime: create A, B, Loan under A  
4. Attempt reassignment via `link_instances` and any discoverable unlink  
5. If stuck, one honest attempt at `apply_dsl` with invented syntax — capture rejection  
6. Report; stop  

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S2-YYYYMMDD.md`

Emphasize: **is missing DSL link the blocker, or MCP unlink, or runtime model?**

---

## Status tracking

**Claimed by:**  
**Started:**  
**Report path:**  
**Result:** PASS / FAIL / PARTIAL  
