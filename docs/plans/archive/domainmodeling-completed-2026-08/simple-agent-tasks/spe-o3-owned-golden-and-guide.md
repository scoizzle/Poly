# SPE-O3 — Owned golden + guide honesty

**Stream:** O  
**Difficulty:** S  
**Status:** `[x]`  
**Soft prereq:** O2  

## Objective

One end-to-end golden and guide text that match reality after O2.

## Required reading

- O2 fix  
- `poly-dsl-guide.md` owned / related policy bullets  

## Exact steps

1. Golden: domain with `owned` (or to-one) nav → create/link instances → `EvaluatePolicy` true and false cases.  
2. Guide: adjust any overclaim; document store requirement for related/owned reads.  
3. List residual gaps (many owned, nested owned, …) in a short “not yet” list if not fixed — do not leave as silent false ✅.

## Verification

- [x] Golden green  
- [x] Guide honest  
- [x] Full suite green  

## File ownership

- **Edit:** tests; guide owned/policy sections only  
- **Do not rewrite** §7 export peer notes (E owns)  

---

## Progress notes

### 2026-08-02 — implement

**Golden:** `EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse` (MCP) — `profile: owned Profile` + path-prefix policy → create Alice/Bob + profiles → link → `evaluate_policy` true/false.

**Guide:** Related/owned store+link requirement; fail-closed without store/link; residual not-yet (multi-hop nested owned, IR-only OwnedAccess, many-owned beyond Q3′); Expression Gaps splits single-hop shipped vs nested multi-hop not shipped; JSON `simulate_policy` bag-only honesty.

**Test fix:** `SimulatePolicy_RelationshipJson_WithoutStore_FailsClosed` (was soft-success pre-O2; now expects fail-closed).

### 2026-08-02 — verify (fail — bug)

Static adversarial re-check of O3 AC against current sources (no shell: git/suite not re-executed).

| AC | Result |
|----|--------|
| Golden green | **Pass** — `McpSmokeTests.EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse` (~3181–3237): owned DSL nav, create Alice/Bob+profiles, `link_instances` profile, `evaluate_policy` IsUrban true/false. Unit siblings `EvaluatePolicy_ToOneRelationshipNav_{ResolvesLinkedProperty,NonMatching_ReturnsFalse,WithoutStore_Throws,Unlinked_Throws}` still present. Fail-closed MCP sibling `OracleToolTests.SimulatePolicy_RelationshipJson_WithoutStore_FailsClosed` (~236–245) expects Success false. Production O2: `DomainEntityInstance.PreprocessQuantifiers` RelationshipNavigation (~1144–1155) always `GetOutboundRelatedInstances` + empty-target throw. |
| Guide honest | **Fail** — guide ~577–583 groups `Rel exists` with store+link fail-closed path-prefix/owned/quantifier claim. Parse `Exists(PropertyAccess)` + bag lower is a **sibling incomplete path** not listed under residual “not yet”. Path-prefix residuals (multi-hop / OwnedAccess / many-owned) + `simulate_policy` bag-only (~595–617) match O3 steps 2–3 for path-prefix only. §7 peer/export left intact. |
| Full suite green | **Not re-verified** — implement-era “1799 passed” claim not independently re-run. |

**Verdict:** implement success; verify **fail** (severity: bug) — guide overclaims / omits residual for `Exists` bag-lower vs store+link grouping. Leave open until guide residual honesty fixed and suite re-confirmed.

### 2026-08-02 — residual close

**Guide honesty fix** (`poly-dsl-guide.md` owned/related policy sections only; §7 export peer untouched):
- Split dual paths: store+link for path-prefix / owned / quantifiers (fail-closed) vs **`Rel exists` → `Exists(PropertyAccess)` bag null-lower** (not store outbound presence).
- Residual **not yet**: store-aware `Rel exists` / link presence; multi-hop nested owned; IR-only OwnedAccess product DSL; many-owned demos beyond Q3′.
- Expression Gaps + presence/absence table rows document bag-null vs store-link; JSON note prefers path-prefix/quantifiers until store-aware exists.

**Evidence (implementer):** `dotnet build Poly.Mcp/Poly.Mcp.csproj` + `dotnet run --project Poly.Tests/Poly.Tests.csproj` → **1799 passed**, 0 failed. Golden still present (`EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse`).

### 2026-08-02 — verify (pass, severity nit)

Static re-check of O3 AC after residual close (read-only verifier session; no shell).

| AC | Result |
|----|--------|
| Golden green | **Pass** — `McpSmokeTests.EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse` (L3181–3237): owned profile nav + path-prefix policy, create Alice/Bob+profiles, `link_instances`, `evaluate_policy` true/false. Unit fail-closed siblings `DomainEntityInstanceTests.EvaluatePolicy_ToOneRelationshipNav_{WithoutStore,Unlinked}_Throws`; MCP `SimulatePolicy_RelationshipJson_WithoutStore_FailsClosed` expects Success false. |
| Guide honest | **Pass** — L523–524/574/577–584/594–599/608–617 split store+link path-prefix/owned/quantifiers (fail-closed) from `Rel exists` bag path; residual lists store-aware exists, multi-hop nested owned, IR-only OwnedAccess, many-owned demos. §7 export peer block (~398–470) unchanged in substance (E ownership). |
| Production (O2 peer) | `PreprocessQuantifiers` RelationshipNavigation always `GetOutboundRelatedInstances` + empty-target throw; `Exists` only recurses Target (no store outbound). Parser `Rel exists` → `Exists(PropertyAccess)`; `DomainExpressionLoweringPass` Exists → `NotEqual(Member, null)` bag null-lower. |
| Full suite green | **Nit** — 1799 green is implementer-reported only; not re-executed in this verifier session. |

**Verdict:** implement success; verify **pass** (severity: nit). Mark O3 complete; suite re-run remains for `spe-gate` G1.

## Status

**Status:** Complete — implement success; verify pass (severity nit) 2026-08-02  
