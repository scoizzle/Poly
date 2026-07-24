# Micro-Task: G6.4 — Plan/docs honesty after production IR

**Suite:** [`ip-README.md`](ip-README.md) **#G6.4**  
**Parent:** [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)  
**Difficulty:** Small  
**Estimated Context:** ~6k tokens  
**Status:** `[ ]` Not Started  
**Prereq:** G6.1–G6.3 green  

## Objective

Update plans so **production IR** is claimed only for what shipped: DbContext + Program via IR; HttpFile still string; Bar B still pull.

## Required Reading

- [`../infrastructure-pass-NEXT.md`](../infrastructure-pass-NEXT.md)
- [`../infrastructure-pass-task-list.md`](../infrastructure-pass-task-list.md)
- [`../infrastructure-concern-analyzer-suite.md`](../infrastructure-concern-analyzer-suite.md)
- [`../README.md`](../README.md) infrastructure row

## Exact Steps

1. Mark Group 6 Done on NEXT + task-list when code is in.
2. Agent pick: CURRENT post-G6 / next pull.
3. Concern suite “Step 2 / production string Generate” language → IR for Db+API.
4. ip-README statuses `[x]` for completed units.
5. Do not claim Bar B or full codegen IR for HttpFile.

## Verification

- [ ] No plan says “production still all string Generate” if Db+API IR shipped
- [ ] Bar B still explicitly pull
- [ ] README pointer accurate  

## Output

- Plan edits only (unless CORE needs one-line if you change a CORE-listed seam — unlikely)

## Out of Scope

- More code  

## Status tracking

**Claimed by:**  
**Started:**  
**Notes / Blockers:**
