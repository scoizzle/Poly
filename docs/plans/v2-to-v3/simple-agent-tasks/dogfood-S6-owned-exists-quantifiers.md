# Dogfood S6 — Owned access, store-aware `exists`, quantifiers

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Wave:** 2 (query surface + owned)  
**Status:** `[ ]` Not Started  
**Difficulty:** Medium–Hard  
**Prereq:** Prefer S4/S5 not blocking; link + evaluate_policy available  
**Est. session time:** 45–75 min  

---

## Goal

Via MCP only: author policies (or requires) that use **owned path-prefix**, **store-aware `Rel exists` / not exists**, and at least one **Q3′ quantifier** (`any` / `all` / `none` / `count`) against **real store links**, then evaluate them with `evaluate_policy(instanceId=…)`.

## Concept under test

**Store-linked policy evaluation:** not JSON-local-only; empty links fail closed / false correctly; owned access honesty; quantifier empty-set semantics (esp. `all` → false when empty).

---

## Domain sketch (minimum)

- Parent with **owned** child relationship (e.g. Customer → Profile owned, or Order → LineItems).  
- Optional second OneToMany for quantifiers (e.g. loans / items).  
- Policies such as (guide-legal spelling):  
  - owned field compare  
  - `assignee exists` / `not assignee exists` (or rel name from domain)  
  - `any items where …` / `count items` / `none items where …`  
- Runtime: create parent, create/link children, evaluate each policy with **instanceId**.

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain + policies author cleanly | |
| 2 | Owned access policy evaluates correctly with instanceId | |
| 3 | `exists` / not-exists reflects **link presence** in store (not only local bag) | |
| 4 | At least one quantifier form evaluates against linked collection | |
| 5 | Empty collection: document `any`/`all`/`none`/`count` results (expect non-vacuous `all`) | |
| 6 | Missing store/domain fails closed if product requires it — report honesty | |
| 7 | `get_domain_analysis` noted: useful facts vs re-infer from DSL | |

**PASS** = owned + exists + one quantifier all green on store-linked evaluate_policy.

**FAIL useful** = JSON-only eval; exists ignores store; quantifiers throw or vacuous true; owned IR-only.

---

## Forbidden workarounds

- Evaluating without instanceId when the concept needs store  
- Building graphs only in C# tests  
- Skipping owned and only testing free properties  
- Platform fixes mid-discovery  

---

## Session steps (suggested)

1. Guide: owned, exists, quantifiers, evaluate_policy  
2. Author domain + policies  
3. create/link instances (zero children, then one+, as needed)  
4. `evaluate_policy` matrix; capture results  
5. Optional: `get_domain_analysis` structured payload usefulness  
6. Report  

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S6-YYYYMMDD.md`

Emphasize: **store-linked or local-only?** **empty-set honesty?** **MCP facts enough for agents?**

---

## Status tracking

**Claimed by:**  
**Started:**  
**Report path:**  
**Result:** PASS / FAIL / PARTIAL  
