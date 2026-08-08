# Fix S1-R — Re-run library checkout to full PASS

**Suite:** [`dogfood-fix-README.md`](dogfood-fix-README.md)  
**Finding:** S1 PARTIAL — invoke_action disabled  
**Prereq:** **HOST** runtime tools enabled; prefer **G1** + **G3** fixed if touching policy/evolve  
**Status:** `[ ]`

## Objective

Re-run [`dogfood-S1-library-checkout.md`](dogfood-S1-library-checkout.md) end-to-end including **invoke_action** create-in checkout and Loan lifecycle. Produce a new report with Result **PASS** or a new product blocker (not host disable).

## Exact Steps

1. Confirm `invoke_action` is callable (HOST).  
2. Follow S1 scenario file success checklist.  
3. Write `agent-summaries/dogfood/DOGFOOD-S1-RERUN-YYYYMMDD.md`.  
4. Update `dogfood-S1-library-checkout.md` Status to `[x]` if PASS.  
5. Update `dogfood-README.md` scenario table + pick → S2.

## Definition of Done

- [ ] Report filed  
- [ ] All six S1 success criteria yes **or** new classified product blocker (not “tool disabled by user”)  
- [ ] Queue pick advances  

## Out of Scope

- Implementing new domain features mid-run  

## Status tracking

**Claimed by:**  
**Started:**  
**Report path:**  
