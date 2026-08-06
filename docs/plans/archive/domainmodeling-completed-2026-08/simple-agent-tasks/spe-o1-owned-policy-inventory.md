# SPE-O1 — Owned policy evaluation inventory

**Stream:** O (owned policies)  
**Difficulty:** S  
**Status:** `[x]`  
**Soft prereq:** Parent plan §4 O  
**Completed:** 2026-08-02  

## Objective

Document what already works vs fails for owned / to-one nested policy reads (no large feature without a named gap).

## Required reading

- Guide path-prefix / owned claims  
- `DomainEntityInstance.EvaluatePolicy` / `PreprocessQuantifiers` RelationshipNavigation  
- Tests: `EvaluatePolicy_ToOneRelationshipNav_*`, owned parse round-trips, dogfood S3 notes if present  

## Exact steps

1. Build a short matrix in this file (or `docs/plans/…` sibling) with rows:  
   - Parse path-prefix owned  
   - EvaluatePolicy store-linked to-one owned/nav  
   - Without store  
   - Many owned + quantifier  
   - OwnedAccess IR vs RelationshipNavigation  
   - Nested owned (owned of owned)  
2. Mark each ✅ / 🟡 / ❌ with one test name or “no test”.  
3. Choose **one** gap as O2 target (prefer: fail-closed without store, or missing path-prefix eval case, or guide overclaim).  
4. No production code unless a one-line doc fix is required.

## Verification

- [x] Matrix complete with evidence  
- [x] O2 target named explicitly at bottom of this file  

## File ownership

- **Edit:** this task file (matrix); optional comment-only  
- **Do not** change exporter or store notify  

---

## Evidence matrix (2026-08-02)

Legend: **✅** works with test · **🟡** partial / dual path / no dedicated test · **❌** broken or no product path

| Row | Status | Evidence | Notes |
|-----|--------|----------|-------|
| **Parse path-prefix owned** | ✅ | `Parser_PathPrefix_RelPropCompare_Authoring_ExportsCorrectly` (`McpSmokeTests`) — `customer Tier is "VIP"` → `RelationshipNavigation`; `Parse_OwnedNav` / `Parse_OneOwnedNav_CreatesOneToOneOwned` (`N1NavigationTests`) — `passport: owned Passport` / `profile: one owned Profile` set `SourceOwnsTarget` | Product path-prefix is **relationship-name agnostic**: owned entity nav and plain to-one both parse as `RelationshipNavigation`. Guide claim (`profile City is "Metropolis"`) matches parser. No single test that pairs `owned` declaration + path-prefix policy in one apply (composition is mechanical). Round-trip: `Printer_PathPrefix_RoundTrips`. |
| **EvaluatePolicy store-linked to-one owned/nav** | ✅ | `EvaluatePolicy_ToOneRelationshipNav_ResolvesLinkedProperty`; `EvaluatePolicy_ToOneRelationshipNav_NonMatching_ReturnsFalse` (`DomainEntityInstanceTests`) | `PreprocessQuantifiers` resolves `RelationshipNavigation` via store outbound links when `Store != null` and `targets.Count > 0`, then `EvaluateBodyOnTarget` on first target. Tests use `OneToOne` without `SourceOwnsTarget`; ownership flag is not consulted at eval (correct for reads). JSON authoring: `AddPolicy_RelationshipJson_ValidSyntax` with `profile: owned Profile`. |
| **Without store** | ❌ | Quantifiers: `EvaluatePolicy_Quantifier_WithoutStore_Throws` ✅ fail-closed. **To-one path-prefix / nav: no test.** | Code: if `Store is null`, or store present but **unlinked** (`targets.Count == 0`), or resolution throws (caught and swallowed), nav is **passed through** to VM lowering as nested `Member` on the subject bag — not fail-closed. Contrasts with Q3′ quantifiers which always call `GetOutboundRelatedInstances` (throws without store). Guide § related policies implies store path for cross-entity; design lock §4 O: “Fail closed without store/link… (no vacuous true).” **This is the honesty/product gap.** |
| **Many owned + quantifier** | 🟡 | `EvaluatePolicy_AnyQuantifier_ReturnsTrueWhenMatched` / `…FalseWhenUnmatched`; `EvaluatePolicy_AllQuantifier_*`; `EvaluatePolicy_Q3Prime_Any_WithLinkedInstances` (`McpSmokeTests`); without store: `EvaluatePolicy_Quantifier_WithoutStore_Throws` | Quantifiers work for `OneToMany` + store links. **No test** with `SourceOwnsTarget = true` / `many owned` declaration — eval path does not branch on ownership, so behavior should match plain many, but unproven. Parse of many-owned: `N1NavigationTests` (`lineItems: many owned LineItem`). |
| **OwnedAccess IR vs RelationshipNavigation** | 🟡 | Nav product path: path-prefix tests above + to-one eval. OwnedAccess: `OwnedAccess_LowersToNestedMember`; `OwnedAccess_LowersWithoutThrowing` (documents VM bag gap); `EntityPolicy_OwnedAccess_NestedProperty_NotValidatedAgainstEntity`; PersonLifecycle-style `DomainExpression.Owned("BirthCertificate", …)` | **Two IRs:** (1) **Product:** path-prefix → `RelationshipNavigation` (entity-to-entity, including `owned` rels); store-preprocess for eval. (2) **IR-only / legacy value-doc:** `OwnedAccess` for nested value-type shape; **not** authored by path-prefix parser; **not** store-resolved in `PreprocessQuantifiers` (only recurses inner); bag-based `EvaluatePolicy` does not materialize nested CLR objects. Design lock: prefer one authoring surface (path-prefix); keep OwnedAccess for lowering if needed. |
| **Nested owned (owned of owned)** | ❌ | **no test** | Multi-hop path-prefix (e.g. `customer profile City`) / nested `RelationshipNavigation` inside nav body is not covered. `PreprocessQuantifiers` resolves outer nav on **source**, then runs `EvaluateBodyOnTarget` with already-preprocessed inner against target bag — inner `RelationshipNavigation` is not re-resolved against the **target** instance’s store links. Nested owned-of-owned is unshipped for runtime honesty. |

### Code anchors

- Eval entry: `Poly/DomainModeling/DomainEntityInstance.cs` — `EvaluatePolicy` → `PreprocessQuantifiers`
- To-one preprocess: same file, `case RelationshipNavigation` (~1141–1158): store-linked → literal; else pass-through (soft miss)
- Quantifiers: `EvaluateAnyExpr` / `GetOutboundRelatedInstances` throw without store
- Guide claims: `Poly.Mcp/Docs/poly-dsl-guide.md` — Related policies dual path; Expression Gaps “Owned/nested access ✅ shipped (path-prefix)”

### Guide honesty snapshot

| Claim | Matches runtime? |
|-------|------------------|
| Path-prefix for owned/to-one authoring | ✅ |
| Store-linked `evaluate_policy(instanceId=…)` for to-one path-prefix | ✅ (proven for plain OneToOne; owned flag unused) |
| Fail closed without store for cross-entity | 🟡 quantifiers yes; **to-one nav no** |
| Nested owned multi-hop | ❌ overclaim if read as multi-level composition |

---

## Progress notes

### 2026-08-02 — implement + verify (pass, severity none)

**Implement success:** true · **Verify pass:** true · **Severity:** none  
No production changes. Docs-only inventory; suite build not required for this task.

- **Matrix:** Full 6-row evidence matrix with ✅/🟡/❌, concrete test names (or explicit “no test”), and code anchors.
- **Code:** `DomainEntityInstance.PreprocessQuantifiers` `RelationshipNavigation` ~1141–1158 — store-linked literal vs pass-through; quantifiers throw via `GetOutboundRelatedInstances`.
- **Tests verified present under Poly.Tests:** `DomainEntityInstanceTests` (`EvaluatePolicy_ToOneRelationshipNav_*`, quantifier suite, `EvaluatePolicy_Quantifier_WithoutStore_Throws`); `N1NavigationTests` (`Parse_OwnedNav_CreatesOwnedOneToOne`, `Parse_OneOwnedNav_CreatesOneToOneOwned`); `McpSmokeTests` (path-prefix authoring/export, Q3′); lowering (`OwnedAccess_LowersToNestedMember`, `OwnedAccess_LowersWithoutThrowing`).
- **Guide honesty:** path-prefix + store-linked to-one ✅; fail-closed without store only for quantifiers 🟡; nested multi-hop ❌.
- **Dual IR:** Product path-prefix → `RelationshipNavigation`; IR-only `OwnedAccess` for nested value-doc — honestly partial, not unified.
- **O2 target (explicit):** Fail-closed `EvaluatePolicy` for to-one `RelationshipNavigation` when store or link missing (see Status below). Nested multi-hop / OwnedAccess unification deferred.

## Status

**Status:** Complete  

**O2 target:** **Fail-closed EvaluatePolicy for to-one `RelationshipNavigation` when store is missing or link is missing** (no pass-through to bag `Member` chains; no vacuous true/false from soft catch). Align with `EvaluatePolicy_Quantifier_WithoutStore_Throws` and parent plan §4 O. Prefer throw `InvalidOperationException` with a clear message; add tests: without store, store-unlinked, and keep existing linked true/false tests green. Do **not** expand O2 into nested multi-hop or OwnedAccess unification (list those for O3/follow-up if needed).  
