# SPE-O2 — Owned policy eval fix (from inventory)

**Stream:** O  
**Difficulty:** M  
**Status:** `[x]`  
**Soft prereq:** O1 (named target)  

## Objective

Implement the **single** gap named by O1 with tests; keep production generic.

## Required reading

- O1 matrix + O2 target  
- Relevant eval/preprocess/parser files only  

## Exact steps

1. Write failing TUnit test that constructs the illegal/missing behavior.  
2. Smallest production fix (prefer preprocess/eval; avoid new dual path).  
3. Fail closed where appropriate (no vacuous true on missing link/store).  
4. Do not expand into date ops or multi-hop inventiveness beyond O1 target.

## Verification

- [x] New test green  
- [x] Existing owned/nav policy tests still green  
- [x] Build green  

## File ownership

- **Edit:** DomainEntityInstance policy path / analyzers as needed; tests under DomainModeling  
- **Do not edit:** export peer handlers, entity-level notify (unless shared helper unavoidable — prefer not)  

---

## Progress notes

### 2026-08-02 — implement

**O1 gap:** fail-closed to-one `RelationshipNavigation` when store or link missing.

**Production:** `PreprocessQuantifiers` `case RelationshipNavigation` always resolves via `GetOutboundRelatedInstances` (throws without store/domain/metadata); empty target list throws `InvalidOperationException` (no soft pass-through / swallowed catch).

**Tests:** `EvaluatePolicy_ToOneRelationshipNav_WithoutStore_Throws`, `…_Unlinked_Throws`; existing linked true/false kept green.

### 2026-08-02 — verify (pass, severity none)

**Target (from O1):** fail-closed `EvaluatePolicy` for to-one `RelationshipNavigation` when store or outbound link missing.

**Production:** `Poly/DomainModeling/DomainEntityInstance.cs` `PreprocessQuantifiers` case `RelationshipNavigation` (L1144–1155) always calls `GetOutboundRelatedInstances` (L1053–1084: throws on null Domain, missing rel metadata, wrong source, bad cardinality, null Store) then throws `InvalidOperationException` on `targets.Count == 0` with clear message; no try/catch soft pass-through; success path `EvaluateBodyOnTarget` + `Literal`. Doc comment L1118–1121 states fail-closed contract.

**Tests:** `DomainEntityInstanceTests` — `EvaluatePolicy_ToOneRelationshipNav_WithoutStore_Throws` (domain, no store → Throws IOE), `…_Unlinked_Throws` (store.Add source only, no Link → Throws IOE); `…_ResolvesLinkedProperty` / `…_NonMatching_ReturnsFalse` kept for linked true/false.

**Sibling paths:** quantifiers share `GetOutboundRelatedInstances` (aligned fail-closed); `DomainExpressionLoweringPass` still unfolds `RelationshipNavigation` to `Member` for export/peer, but `EvaluatePolicy` preprocesses nav to `Literal` first so bag soft-Member is not a policy success path. Nested multi-hop and `OwnedAccess` explicitly deferred (O1). File ownership OK (`DomainEntityInstance` + DomainModeling tests + spe-o2/spe-README only). AC checkboxes and README O2 `[x]` match intent. Static proof in verifier session; no contract bugs within O2 scope.

## Status

**Status:** Complete — implement success; verify pass (severity none) 2026-08-02
