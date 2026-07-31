# DAS W3.2 — Split validation diagnostics from fact emitters

**Wave:** W3 · **Queue:** [`das-README.md`](./das-README.md)  
**Future state:** [`../domain-analysis-future-state.md`](../domain-analysis-future-state.md) §3.1, W3  
**Difficulty:** Large (slice-friendly)  
**Status:** `[ ]`  
**Prereq:** W3.1  

## Objective

Start separating megapass “lint everything” from small fact emitters. Do not require finishing all diagnostic packs in one PR—land a clear split for **EffectAnalyzer** and/or **PolicyConstraintAnalyzer** as the template.

## Tasks

- [ ] W3.2.1 Identify metadata actually consumed from EffectAnalyzer / PolicyConstraintAnalyzer.
- [ ] W3.2.2 Extract fact publication into a small pass or catalog-adjacent step; leave diagnostics in a validate pack.
- [ ] W3.2.3 Document optional severity tiers (core vs advisory) if splitting suggestion analyzers.
- [ ] W3.2.4 Progress notes: remaining megapass LOC and follow-up tasks if multi-PR.

## Acceptance criteria

- [ ] At least one megapass has a clear fact-vs-lint boundary in code or task follow-ups.
- [ ] No consumer breaks; build + tests green.
- [ ] Follow-ups filed in suite if split incomplete.

## Progress notes

(empty)
