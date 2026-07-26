# Dogfood S3 — Owned nested profile

**Queue:** [`dogfood-README.md`](dogfood-README.md)  
**Protocol:** [`../mcp-dogfood-protocol.md`](../mcp-dogfood-protocol.md)  
**Status:** `[x]` — owned-1/2/3 shipped; see [synthesis](../agent-summaries/dogfood/DOGFOOD-SYNTHESIS-20260725.md) and [owned suite](dogfood-owned-README.md)  
**Difficulty:** Hard  
**Prereq:** S1 preferred (baseline authoring)  
**Est. session time:** 45–75 min  

---

## Goal

Model a **Customer** (or Patron) with an **owned nested structure** (profile / address) and use that nested data in a **policy or effect expression** — not only store a flat string blob.

## Concept under test

**Owned / nested domain data** (composition): authoring (`owned`), instance shape, and expression access in product DSL.

Signals whether **value types / deeper owned access** are the next core concept build.

---

## Domain sketch (minimum)

- **Customer** entity with either:  
  - `profile: owned Profile`-style nav, **or**  
  - documented product-supported owned form from the guide  
- Nested fields e.g. City, PostalCode, or DisplayName on owned target  
- A **policy** or **require** that reads nested data (e.g. City is required / matches)  
- An **action** that assigns a nested field or creates/updates owned data  

If `owned` is undocumented or broken, that is the finding — do not abandon without trying guide-first.

---

## Success checklist

| # | Criterion | Met? |
|---|-----------|------|
| 1 | Domain with owned/nested structure applies with analysis errors = 0 **or** blocker is clearly owned-related | |
| 2 | Agent can create a Customer instance and set nested data via product path | |
| 3 | Policy or effect **reads** nested field via product expression (path-prefix / owned access per guide) | |
| 4 | `evaluate_policy` or invoke shows nested rule enforced | |
| 5 | `export_dsl` preserves owned declaration | |
| 6 | Report states: owned works / IR-only / guide lie / need value types | |

**PASS** = nested data authored, stored, and enforced in a rule without flattening to one Text field.

**FAIL useful** = forced to use `AddressLine: Text` only → bucket **C** or **I**.

---

## Forbidden workarounds

- Encoding profile as a single JSON/Text property to “pass”  
- Skipping policy/expression read of nested field  
- Only documenting in comments without runtime check  
- Jumping to codegen column mapping as the goal  

---

## Session steps (suggested)

1. `get_dsl_guide` — find `owned`, nested access, examples  
2. Author Customer + owned target  
3. If parse/analysis fails, capture diagnostics; one redesign still using owned if possible  
4. Runtime create + set nested fields  
5. evaluate_policy / invoke with nested guard  
6. export_dsl check  
7. Report  

---

## Report output

`docs/plans/v2-to-v3/agent-summaries/dogfood/DOGFOOD-S3-YYYYMMDD.md`

---

## Status tracking

**Claimed by:**  
**Started:**  
**Report path:**  
**Result:** PASS / FAIL / PARTIAL  
