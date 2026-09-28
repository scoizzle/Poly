# Final Boss RE-VERIFY — PR 79

**Target:** https://github.com/scoizzle/Poly/pull/79  
**SHA:** `5fbac3a7f5e61635a67392f12f08c1a7e8f6638d` (HEAD, detached)  
**Branch:** `refactor/p3a-quantifier-nodes` (base: `master`, merge-base `033f14a2f9a4eb80581b57bc919819fd89873524`)  
**Prior SHAs:** `449a43da` (previous re-verify head), `63bad953` (base of this doc series, `State what the property-exists check skips; cover all/none/count`)  
**Reviewer:** Final Boss (OpenCode mill, model `opencode-go/deepseek-v4.1-flash`)  
**Mode:** re-verify (adversarial, code-first, review-only)

## Docs-only confirmation

`git diff --stat 449a43da HEAD` — **docs-only, confirmed.** One file, one line:

| File | Δ |
|------|---|
| `docs/domainmodeling-capability-inventory.md` | +1 / −1 |

`git diff --stat 63bad953 HEAD` — **docs-only, confirmed.** Five `.md` files, no non-doc change:

| File | Δ |
|------|---|
| `Poly.Mcp/Docs/poly-dsl-guide.md` | +7 / −7 |
| `docs/domainmodeling-capability-inventory.md` | +1 / −1 |
| `docs/interpretation/README.md` | +1 / −1 |
| `docs/interpretation/domain-execution-model.md` | +36 / −25 |
| `docs/plans/simple-agent-tasks/e2e-2-README.md` | +3 / −1 |

Worktree is clean (`git status --short` shows only the two untracked review files); no other diffs.

## Issue counts

| Severity | Count |
|----------|-------|
| Bug | 0 |
| Suggestion | 1 (F32) blocking; plus F33, which is pre-existing and non-blocking |
| Nit | 1 (F31) |

## Verdict

**not ship.** There are zero bugs. The one blocker is F32: the runtime sentence this head adds at `domainmodeling-capability-inventory.md:99` says every quantifier is a loop that breaks, but the `count` forms in the table right above it are not a loop (bare) or do not break (filtered). It is a one-sentence fix. F28, F29 and F30 are closed. The hand-editability gate is **NO**, because of F32 only.

## Oracles

- CI "Build & test" on `5fbac3a7` **passed** (run 36362696413; its head SHA is `5fbac3a7…`).
- The executor ran `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` at `449a43da`: **2881 total, 2881 passed, 0 failed**. The code is identical at `5fbac3a7`, because `449a43da..5fbac3a7` changes one doc line.
- The executor checked F32 and F33 against the code:
  - F32: `CountExpr` with no body returns `Member(Member(subject, nav), "Count")` at `DomainExpressionLoweringPass.cs:437-441`, and the count branch at `:482-483` has no `BreakStatement`.
  - F33: the stale lines predate the PR, as described above.

## Summary

`5fbac3a7` is a one-line docs follow-up that rewrites `docs/domainmodeling-capability-inventory.md:99` from the stale "store-linked `DomainEntityInstance` preprocess + VM" to "each quantifier lowers to a `ForEachLoop` over the collection nav with a `BreakStatement` once the answer is known; simulate and the printed C# run the same loop." That fixes the specific stale line the prior re-verify missed, and the simulate/print parity half is correct. But the new sentence over-generalizes to **every** quantifier listed in the table immediately above it: bare `count Rel` lowers to `this.Rel.Count` (no loop, no break) and filtered `count … where` loops with **no** `BreakStatement`. The same nuance is stated correctly one file over (`domain-execution-model.md:222`, `:226`). Meanwhile the task-3 grep for the deleted preprocess path surfaces a live, CORE-linked doc (`complexity-semantic-map.md:140,142,274`) that still describes a runtime quantifier/path preprocess — PR 79 did not create those lines. On `origin/master` the code already had no quantifier preprocess (`git grep Preprocess origin/master -- 'Poly/*.cs'` finds nothing), and `complexity-semantic-map.md` has not changed since `8aa72834` (2026-08-09). So they are filed as F33 (pre-existing, non-blocking) and do not reopen F28. F28, F29 and F30 are closed, and F31 stays a nit.

## F28 / F29 / F30 disposition

| F# | Prior finding | Disposition | Evidence from this worktree |
|----|---------------|-------------|-----------------------------|
| F28 | Product docs describing the deleted store-job / preprocess quantifier path (`domain-execution-model.md`, `README.md:10`), plus the missed `domainmodeling-capability-inventory.md` runtime line | **CLOSED** | The specific line is fixed: `inventory.md:99` no longer says "store-linked … preprocess". `grep -E 'AnyRelated\|AllRelated\|NoneRelated\|CountRelated\|StoreQuantifier\|RequirePredicate'` over all docs → 0 hits; `grep -rn 'Preprocess' --include=*.cs Poly/` → 0 hits. **But** live, CORE-linked `docs/complexity-semantic-map.md:140` still says "Guards run via lower→VM after quantifier/path preprocess against the store" (demon "preprocess + VM dual steps"), `:142` "DomainExpression rewrite (runtime): Before lower, rewrite DE for store-aware forms (quantifiers, peer binders, …)", and `:274` D16 "Runtime DE rewrite + lower + VM: Three stages for one policy". These rows are stale, but they were already stale before PR 79, so they are filed as F33 and do not reopen F28. They describe a quantifier preprocess that master had already removed; `EvaluatePolicy` now lowers in-tree (`DomainEntityInstance.cs:370-393`; peer rewrite at `DomainEntityInstance.HostAbi.cs:496-509` is subscription-only). |
| F29 | `Poly.Mcp/Docs/poly-dsl-guide.md` ~556-561 led with "at any depth … is rejected" before the unresolved-nested-quantifier carve-out | **CLOSED** | guide:554-564 now leads with "wherever every quantifier on the way to it resolves: on the record itself, or on a related record inside an `any`/`all`/`none`/`count` body", then the unresolved-nested carve-out. Matches `EffectAnalyzer.TestsPropertyExists` (`EffectAnalyzer.cs:858-884`). |
| F30 | `docs/plans/simple-agent-tasks/e2e-2-README.md:6` replaced the `[ ]` Status marker with prose | **CLOSED** | Line 6 is `**Status:** \`[ ]\``; prose moved to `**Note:**` at line 8. |

## New-claim verification (including the 5fbac3a7 line)

| Claim (doc line) | Code evidence | Accurate? |
|---|---|---|
| **Inventory runtime: "each quantifier lowers to a `ForEachLoop` over the collection nav with a `BreakStatement` once the answer is known" (`inventory.md:99`)** | Bare `count Rel` → `LoweredExpression.Of(Member(Member(subject, nav), "Count"))` = `this.Rel.Count`, no loop (`DomainExpressionLoweringPass.cs:437-441`); filtered `count … where` loops with **no** `BreakStatement` (`:482-483`) | ❌ **inaccurate for `count`** (both forms) → **F32** |
| Inventory "simulate and the printed C# run the same loop" (`inventory.md:99`) | Prior row below + `DirectVmAbiEmitter.cs:234-235`; `CSharpGenerator.cs:200-212` | ✅ (for any/all/none and filtered count; bare count is a direct `.Count` read on both paths) |
| Quantifiers lower to plain loop nodes, same tree on simulate and print; method `LowerFilteredQuantifier` (211) | `DomainExpressionLoweringPass.cs:31,428-435,442,457` | ✅ |
| Init `any:false, all/none:true, count:0` (214) | `DomainExpressionLoweringPass.cs:469-474` (`false`/`true`/`true`/`Constant(0L)`) | ✅ |
| `all` also sets `sawItem = true` (215) | `DomainExpressionLoweringPass.cs:486-491` | ✅ |
| `count: result = result + 1`, no `break` (217) | `DomainExpressionLoweringPass.cs:482-483` (no `BreakStatement`) | ✅ |
| `all` value = `sawItem && result` (empty ⇒ false) (219) | `DomainExpressionLoweringPass.cs:497` `new SN.And(sawItem, result)` | ✅ |
| Statements sit before the reading statement via `LoweredExpression.Before` (222) | `LoweredExpression.cs:14-31`; callers `EffectLoweringPass.cs:283`, `DomainToCSharpExporter.Actions.cs:478` | ✅ |
| Nested loops inside outer body; one `LocalNames` per method (222) | `DomainExpressionLoweringPass.cs:500-504,523-529` (`_context with` keeps `Names`); `DomainToCSharpExporter.Actions.cs:108-110` one `LocalNames` per method | ✅ |
| VM runs `ForEachLoop`/`BreakStatement` directly (222) | `DirectVmAbiEmitter.cs:234-235`; `DirectVmAbiEmitter.Statements.cs:534,589` | ✅ |
| C# export prints the same loop (222) | `CSharpGenerator.cs:200-212,534-542` | ✅ |
| Bare `count Rel` is `this.Rel.Count` (222) | `CountExpr` no-body `DomainExpressionLoweringPass.cs:437-441`; export subject `ThisReference` `:108-110`; collection property `DomainToCSharpExporter.cs:243-265` | ✅ (this is the nuance `inventory.md:99` drops) |
| Simulate collection = dict read of nav = `ReadLinkedTargets`, empty when unlinked, throws without Store (224) | `DomainEntityInstance.Dictionary.cs:18-29,40-51`; `DomainEntityInstance.Runtime.cs:389-398` | ✅ |
| Export reads the entity's own collection property (224) | `DomainToCSharpExporter.cs:243-265` (`IReadOnlyList<T>` getter over `_rel`) | ✅ |
| `Rel exists` runtime `ExistsRelated(relName)`; unknown rel throws (230,234) | `DomainExpressionLoweringPass.cs:250-254`; `DomainEntityInstance.HostAbi.cs:42-50` | ✅ |
| Export to-one `this.Rel != null`; collection `this.Rel.Count != 0` (234) | `DomainExpressionLoweringPass.cs:255-265` with `ThisReference` subject | ✅ |
| To-one path-prefix runtime `ExistsRelated(rel) ? ((Target)GetRelatedOne(rel)).Leaf : false` (235) | `DomainExpressionLoweringPass.cs:178-200` (`Conditional(exists, typedLeaf, false)`, `TypeCast` to target) | ✅ |
| Export `this.Rel!.Leaf` (235) | `DomainExpressionLoweringPass.cs:202-203` (`NullForgiving(Member(This, nav))`) | ✅ |
| Many links fail closed; require gates return `DomainResult.Failure` first (235) | `DomainEntityInstance.HostAbi.cs:62-65`; `EffectLoweringPass.cs:640-661` (`requires a linked`) | ✅ |
| Action-parameter root `Member(param, leaf)` (236) | `DomainExpressionLoweringPass.cs:163-169` | ✅ |
| §6b `LoweredExpression` (statements + variables + value), `DomainExpressionDispatch<LoweredExpression>` (289) | `LoweredExpression.cs:6-9`; `DomainExpressionLoweringPass.cs:31` | ✅ |
| §6c member list; `Names` last; shared `LocalNames` (296,303) | `LoweringContext.cs:89-110` (`Names` is final parameter); `LocalNames.cs:6-9` | ✅ |
| Known-gaps quantifier row (loop + break; simulate = print) (354) | Code/tests above (boolean quantifiers) | ✅ |
| Known-gaps relationship-navigation row (runtime guard vs export NRE) (355) | `DomainExpressionLoweringPass.cs:178-203`; F17 (pre-existing) | ✅ |
| `docs/interpretation/README.md:10` purpose line "quantifier loops" | Code/tests above | ✅ |
| Guide 554-564 vs `TestsPropertyExists` and `PolicyConstraintAnalyzer` | `EffectAnalyzer.cs:781-791,858-884` (scope resolves per-hop; unresolved nested ⇒ `return false`); `PolicyConstraintAnalyzer.cs:99-139,311-336` (top-level quantifiers validated; nested not recursed as quantifiers) | ✅ |
| e2e-2-README Status marker restored (6) | line 6 `[ ]`, line 8 `**Note:**` | ✅ |

## Stale-reference grep (docs only; excluding `docs/agent/reviews`, `docs/plans/archive`)

`grep -E 'AnyRelated|AllRelated|NoneRelated|CountRelated|StoreQuantifier|RequirePredicate'` over all `.md` and over all `.cs` → **0 hits**. `"Store job"` for quantifiers → **0 hits**. `grep -rn 'Preprocess' --include=*.cs Poly/` → **0 hits**. `"preprocess"` near quantifiers in live product docs (top-level `docs/*.md`, `docs/interpretation`, `Poly.Mcp/Docs`, `README*`, `docs/CORE.md`):

| path:line | Content | Live/stale |
|---|---|---|
| `docs/complexity-semantic-map.md:140` | "Guards run via lower→VM **after quantifier/path preprocess** against the store" (demon "preprocess + VM dual steps") | **stale** — live doc linked from `docs/CORE.md:266`; preprocess is gone (`EvaluatePolicy` lowers in-tree `DomainEntityInstance.cs:370-393`). pre-existing, filed as **F33** |
| `docs/complexity-semantic-map.md:142` | "DomainExpression **rewrite (runtime)**: Before lower, rewrite DE for store-aware forms (**quantifiers**, peer binders, …)" | **stale** (quantifier half) — only the subscription peer-binder rewrite survives (`DomainEntityInstance.HostAbi.cs:496-509`). pre-existing, filed as **F33** |
| `docs/complexity-semantic-map.md:274` | D16 "**Runtime DE rewrite + lower + VM** — Three stages for one policy" | **stale** — quantifier rewrite gone; policy lower is two stages. pre-existing, filed as **F33** |
| `docs/complexity-semantic-map.md:270` | D12 fix "Split effect runner / **policy preprocessor**" | **stale wording** (recommendation references a gone stage); pre-existing, filed as **F33** |
| `Poly.Mcp/Docs/poly-dsl-guide.md:857` | "…legal in policies, require, assign RHS, and `if` conditions (**same preprocess as policy eval**)" | **borderline nit** — uses repudiated terminology (`domain-execution-model.md:226`); describes consistency, not a stage. Note for the next guide touch |
| `docs/interpretation/domain-execution-model.md:90` | "`PreprocessRuntimeKeyword` are gone" | **live/accurate** (negation statement) |
| `docs/interpretation/domain-execution-model.md:226` | "Execute-time `PreprocessQuantifiers` → literals is gone" | **live/accurate** (negation statement) |
| `docs/PROJECT-SUMMARY-FOR-AGENTS.md:77`; `docs/domainmodeling-capability-inventory.md:181` | "Q3′ quantifiers … with store-linked resolution"; "`EvaluatePolicy` local vs store-linked (quantifiers)" | **live/accurate** — simulate does read store-linked targets (`ReadLinkedTargets`) |
| `Poly.Mcp/Docs/poly-dsl-guide.md:861` | Q3′ resolvable at `evaluate_policy` | **live/accurate** (no throw claim) |
| `docs/interpretation/domain-execution-model.md:211` | "Q3′ quantifiers … lower to plain loop nodes" | **live/accurate** |
| `Poly.Mcp/Docs/poly-dsl-agent-guide.md:326,364,368,432`; `poly-dsl-guide.md:768,855,874,970` | Q3′ shipped, eval/print parity | **live/accurate** |
| `docs/plans/*` hits (`ef-and-api-codegen.md:38`, `live-demo-reliability-2026-08-13.md:37`, `domainmodeling-e2e-representation-2026-08-13.md`, `simple-agent-tasks/e2e-0-1-guide.md`, `e2e-2-1-implement.md`, `e2e-r-8-…`, `e2e-x-10-…`, `p1-temporal-design-lock.md:46`, `create-create-in-*.md`, `domain-dsl-absorption-proposals.md`) | "Q3′ export throws", `PreprocessQuantifiers` removal notes | **stale but parked/archived-scope** — historical plan/task artifacts (some already executed); tracked as F31 (nit) |

The `complexity-semantic-map.md` rows above were stale before this PR: the file was last changed in `8aa72834` (2026-08-09), and master's code already had no preprocess. They are filed as F33 (pre-existing, non-blocking) and do not reopen F28. The product-facing docs this PR changed are clean apart from F32.

## Carried items (unchanged at this SHA)

| F# | Item | Still true? |
|----|------|-------------|
| F14 | `DomainEntityInstance.Runtime.cs:355` `Store.GetRelatedInstances` (deferred) | ✅ true (`Runtime.cs:355`) |
| F17 | `DomainExpressionLoweringPass.cs:202-203` unlinked hop: simulate false / export NRE (pre-existing) | ✅ true; now documented as a known gap (`domain-execution-model.md:235`) |
| F20 | `RuntimeAnalysisCache.cs:433` "VM StoreQuantifier path" comment (moved to PR 80) | ✅ true (`RuntimeAnalysisCache.cs:433`) |
| F24 | Number-valued policy: simulate nonzero-truthy / print CS0029; scalar `exists` differs (parked) | ✅ true (`QuantifierLoopTests.cs:380-396`) |
| F25 | `PolicyConstraintAnalyzer.cs:311-336` does not validate nested quantifiers (parked) | ✅ true (`PolicyConstraintAnalyzer.cs:311-336`) |

## Hand-editability gate — **NO**

- The new `inventory.md:99` runtime sentence over-generalizes: a cold hand-editor following it would reproduce `ForEachLoop` + `BreakStatement` for `count`, contradicting the table row directly above and the code (`DomainExpressionLoweringPass.cs:437-441,482-483`). A suggestion-severity doc defect ⇒ gate NO.
- F33 (pre-existing, outside this PR's diff) is noted but does not decide the gate. The gate is NO because of F32 only.
- Everything else that made the prior "YES" holds: the pseudo-code block (`.md:213-220`) transcribes `LowerFilteredQuantifier` (`:469-497`) 1:1; `LoweringContext` (§6c) is flat with an elision marker; the F28 named-job/export-throw defects from `63bad953` remain gone.

## New findings

**F32 (suggestion, blocking) — `docs/domainmodeling-capability-inventory.md:99` claims every quantifier loops with a break; `count` does neither.**
The sentence "each quantifier lowers to a `ForEachLoop` over the collection nav with a `BreakStatement` once the answer is known" sits immediately under a table listing "`count Rel` / filtered". Bare `count Rel` lowers to `this.Rel.Count` (a direct member read, no `ForEachLoop`, `DomainExpressionLoweringPass.cs:437-441`), and filtered `count … where` accumulates with **no** `BreakStatement` (`:482-483`). A cold reader/agent taking the inventory at face value would model `count` as an early-exit loop over the collection — a wrong runtime shape and a wrong (O(1) vs O(n)) complexity model for a capability the doc is explicitly inventorying. Per the rule this is an inaccurate doc claim that could mislead, so it is a **suggestion** (blocks ship; gate NO), not a wording nit. Smallest fix: qualify the sentence ("for `any`/`all`/`none` and filtered `count`; bare `count Rel` is `this.Rel.Count` and filtered `count` does not break") or lead with the boolean quantifiers and add the `count` exception, matching `domain-execution-model.md:222`.

**F33 (suggestion, pre-existing, non-blocking; added on executor spot-check).** Four live docs still describe a runtime quantifier/path "preprocess" that `origin/master` had already removed before this PR: `docs/complexity-semantic-map.md:140`, `:142`, `:270` (D12 wording) and `:274` (D16), and `Poly.Mcp/Docs/poly-dsl-guide.md:857` ("same preprocess as policy eval"). `docs/CORE.md:266` links the complexity map. PR 79 did not touch these lines and did not delete the preprocess. It goes with the parked items (F24/F25): fix it the next time the map is touched, or put it on the board.

**F31 (nit, non-blocking) — historical plan/task docs still assert the Q3′ export throw.**
`docs/plans/ef-and-api-codegen.md:38`, `docs/plans/live-demo-reliability-2026-08-13.md:37`, `docs/plans/domainmodeling-e2e-representation-2026-08-13.md:97,203,209,216`, `docs/plans/simple-agent-tasks/e2e-0-1-guide.md:15,29`, `docs/plans/simple-agent-tasks/e2e-2-1-implement.md:16,17`, `docs/plans/p1-temporal-design-lock.md:46`. These are parked/draft/task artifacts (several carry "Not CURRENT" banners), not product-truth guides; the PR correctly updated the product-facing surfaces. Demote/annotate whenever these plans are next touched. Does not block ship.

## Checklist

- [x] Task 1 — `git diff --stat 449a43da HEAD` (1 file) and `git diff --stat 63bad953 HEAD` (5 `.md`, 0 code) are docs-only
- [x] Task 2 — BARE `count Rel` verified: `this.Rel.Count`, no loop (`DomainExpressionLoweringPass.cs:437-441`); filtered `count` verified: no `BreakStatement` (`:482-483`) → **F32 suggestion**
- [x] Task 2 — F28's new line (`inventory.md:99`) checked; boolean-quantifier half accurate, `count` half inaccurate
- [x] Task 2 — F29 closed (rejection scope matches `TestsPropertyExists`); F30 closed (`e2e-2-README.md:6` marker restored)
- [x] Task 3 — preprocess/quantif/store-linked grep across live product docs run; the live stale hits in `complexity-semantic-map.md` and `poly-dsl-guide.md:857` predate this PR, so they are filed as **F33** and F28 stays closed
- [x] Task 4 — files updated for `5fbac3a7` (title/SHA, 5-file docs table, F28 evidence, new-claim row, counts, verdict, gate, followups)
- [x] Hand-editability gate **NO** (F32 only)
- [x] Carried F14/F17/F20/F24/F25 confirmed still true; not raised as blockers
- [x] F31 kept as-is (nit)
- [x] No tracked file edited; no build/test run (operator oracle)
- [x] Exactly two review files written under `docs/agent/reviews/`
