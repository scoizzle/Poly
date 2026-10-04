# Final Boss re-check: PR 90, tip f3f0dd36 (slice K1, Name the third input; fix the stage map)

- PR: scoizzle/Poly#90, head `f3f0dd365d419bbcd14abd78ceeb7bcd3a82df29` (checked first with `gh pr view` and `git fetch pull/90/head`: it is the tip the assignment named). Two commits: `6602517a` (the head I reviewed NOT SHIP, `docs/agent/reviews/2026-10-04-pr90-6602517a-final-boss.md`) and `f3f0dd36` ("stage map section 7 no longer lists allowed readers as violations; status lines (K1 follow-up)"), both by Scot Murphy (100x).
- Base: the PR's merge base is master `16403b60`. Master is now `afa246c2` (T2, test files only). `git merge-tree --write-tree origin/master pr90` is clean (rc 0); the PR touches only two docs files and T2 touched none of them.
- Checklist used: `docs/agent/reviews/2026-10-04-pr90-6602517a-followups.md`. I read the full incremental diff `6602517a..f3f0dd36` (two files) and re-ran the greps on the whole head.
- Box clock (`date`): 2026-10-04 CDT.
- Mill: OpenCode `opencode-go/deepseek-v4.1-flash`, read-only, stdout-only, `OPENCODE_DB` unset, the incremental diff and the old findings pasted into the prompt. It answered on the first run (no fallback needed): 5 claims, all re-checked by hand, 0 kept as stated, 1 partly kept (F181), 4 dropped. Docs only, so no tests were run.

## Verdict: SHIP (bugs 0; open: nits F180-F183; hand-editability gate = YES)

F160 and all of F161-F167 are fixed. Difference 2 now says plainly that the MCP read tools reading the authored `Domain` (decision 6) and the generators reading `Domain` plus analysis (decision 5) are allowed, and difference 4, difference 5, the plan's line citations, the status lines, the wave 0 heading and difference 1 each say what the code and the other sections say. Nothing newly added contradicts a settled decision or the code. The four open items are one-line nits (one sentence that is true but not the whole story, one wording, one pair of helper names, one status line that will go stale on merge). K1 is on the V11 list: SHIP with gate YES means Foreman merges.

## F160-F167 closure

| # | Was | Status | Evidence on `f3f0dd36` |
|---|---|---|---|
| F160 | bug: difference 2 listed the generators and MCP read tools as violations | **Closed** | `pipeline-stage-map.md:150` now keeps only DEI and `RuntimeAnalysisCache` re-running stages and says: "Allowed, not violations: the MCP read tools (...) reading the authored `Domain` (decision 6), and the generators reading `Domain` plus analysis because they are part of Compile (decision 5). What is left for the generators is registering their output as artifacts (slices A5a and A5b)." That agrees with 2.7 (line 94), decisions 5 and 6 (plan section 19) and plan C9 ("Generators reading Domain plus analysis is fine (decision 5)"). A5a ("Contributors return artifacts") and A5b ("generator trees registered") exist and say that. Whole-file re-grep of the stage map (`generator`, `MCP`) and of the plan (`reach back`, `generators`, `MCP read tools`): no other sentence calls a generator or an MCP read tool's reading of the domain a violation. The only related text left is difference 8 ("MCP session state" holds the live `Domain`), which is listed as an example, not as a violation; see F183 |
| F161 | suggestion: difference 4 called walking the `Domain` a violation | **Closed** | Line 152: "Reading the session tables ... is fine ... Reading the `Domain` is fine too: it is the first input. The rest is not: it has a re-scan fallback when analysis is null (`LoweringContext.cs`, `EffectLoweringPass.cs`), re-lowers action/stage-scoped policies (`CompletePolicyBodies`), and writes side tables on the cache. `DomainSession.Emit` re-analyzes ...". `CompletePolicyBodies` exists (`RuntimeAnalysisCache.cs:116, 366`); consistent with 2.5 and decision 4 |
| F162 | suggestion: difference 5 said `BindThis`, `RewriteVoidFailClosedThrow`, `BindExportBody` still rewrite | **Closed** | Line 153: "PR 82 removed `BindThis`, `RewriteVoidFailClosedThrow` and `BindExportBody`. Two pre-run rewrites are left on master, `DomainEntityInstance.BindModuleMethodBody` and `BindForSimulate`." `grep -rn "BindThis\|RewriteVoidFailClosedThrow\|BindExportBody" Poly --include=*.cs`: empty. `BindModuleMethodBody` (`DomainEntityInstance.cs:884`) and `BindForSimulate` (`:925`, called at `:736-749`, `:897`, `HostAbi.cs:269`) exist. It agrees with "What PRs 82 and 83 changed" on the three deleted names; the two sections name different leftover pairs (F182, nit) |
| F163 | suggestion: plan cited "stage map line 70" | **Closed** | Plan A3a (`:196`): "Stage map 2.5, Definition of done (...) is NOT met until A6"; A6 (`:259`): "closes the gap against the stage map 2.5 Definition of done". `git grep -n -i "stage map line" docs/domain-modeling` now finds only the K1 card's own scope text (`:664`: "Edit stage map lines 66 ...", the historical instruction the slice carried out) |
| F164 | nit: 2.5 note did not name A6 | **Closed** | Stage map line 71 ends "(slice A6 in the convergence plan)"; A6 is "Finer trees (deferred)" |
| F165 | nit: status lines a merge behind | **Closed** | Plan line 3: "Done so far: T0 (`bde1f7c5`), A1 (`8e5ef81b`), A2a (`08eda8eb`), A2b (`16403b60`), T2 (`afa246c2`). In review, not merged: K1 (PR 90), C4a (PR 91)." T2's SHA is right (`git log`: `afa246c2` "test: T2 ... (#87)"); PR 91 is open, "C4a: simulator enforces equality constraints on create", branch `fix/c4a-equality-on-create`. Wave 0 Lane B: "T2 (done, `afa246c2`)". Wave 1: "C4a first (in review as PR 91; ...)". Stage map line 3: "merged through master `16403b60`". The in-review wording goes stale when this PR merges (F181) |
| F166 | nit: wave 0 said "test-only, docs-only" | **Closed** | Plan line 890: "(everything here is test-only, docs-only or new code that nothing calls yet; ...)". True for the intent; see F181 for one wording point |
| F167 | nit: difference 1 contradicted itself | **Closed** | Line 149: "exist but nothing real is registered through them yet" |

## Checklist (from the short re-check list)

| # | Check | Result |
|---|---|---|
| 1 | F160: difference 2 vs 2.7, decisions 5/6, C9; no other violation statement | **PASS** (table above) |
| 2 | F161, F162, F163, F165 fixed; F164, F166, F167 reported | **PASS**: all eight closed |
| 3 | "exactly two" empty; 2.5 names three inputs | **PASS.** `git grep -n "exactly two" docs/domain-modeling/pipeline-stage-map.md` is empty (rc 1). Line 67: "Input: three things: the domain model, the analysis result, and the libraries the session loaded for the domain (...). Compile reads nothing else." |
| 4 | Nothing new contradicts decisions 1-15, V1-V11 (incl. V10), HELD; new sentences accurate | **PASS.** New sentences checked: T2 SHA and PR 91 (above); "merged through master `16403b60`" (true); difference 2 mapping to A5a/A5b (true) and decisions 5/6 (true); difference 4 and 5 (true, code-checked); the A6 pointer (true); wave 0 and 1 edits (true). Plan section 19 table and `pipeline-convergence-plan.decisions.md` are untouched by the PR; the release rule is unchanged (wave 0 released 2026-10-03; later waves only on Scot's word; N1 waits for decision 16; HELD items are not listed as released). One sentence is true but incomplete: F180 |
| 5 | Docs only; `git diff --check` | **PASS.** `git diff --name-only origin/master...HEAD`: `docs/domain-modeling/pipeline-convergence-plan.md` and `docs/domain-modeling/pipeline-stage-map.md`. No `.cs`, `.csproj`, `.golden` or test file. `git diff --check origin/master...HEAD`: clean (rc 0). Merges clean onto master `afa246c2` |
| 6 | Hand-editability gate | **YES**, below |

## Hand-editability gate

**Answer: YES.** Scot can open both files cold and change them by hand. Section 7 no longer contradicts 2.5 or 2.7: difference 2 defers to decisions 5 and 6, difference 4 treats the domain as the first input, difference 5 names the helpers that are really left, and the plan's cross-references use the sentence name, not a line number, so the next insertion will not break them. Names match the code (`BindModuleMethodBody`, `BindForSimulate`, `CompletePolicyBodies`, A5a/A5b/A6/C9), and every path cited exists. What is left are statements that are true and slightly short (F180) or will need a touch at merge time (F181), not ones that contradict another section or the code.

## New findings (all nits; none blocks)

### F180 [nit] "What is left for the generators" omits C9's `GetOrLower` call
`pipeline-stage-map.md:150`: "What is left for the generators is registering their output as artifacts (slices A5a and A5b)." The plan's C9 ("Generators stop asking for the module") covers one more thing: `src/Poly.DslCompiler/MinimalApiGenerator.cs:1145` still calls `RuntimeAnalysisCache.GetOrLower(...)` and checks actions against it (`RequireHttpActionsInModule`, `:1147`), and C9's NOT SHIP line is "a new cache read appears". Decision 5 lets generators read `Domain` plus analysis; it does not let them re-run Lower through the cache. Add "and no longer calling `GetOrLower` (slice C9)". True as written, but a reader would conclude nothing else is left.

### F181 [nit] Two wordings that go stale or read slightly wrong
(a) Plan line 3, "In review, not merged: K1 (PR 90), C4a (PR 91)", and the wave 1 "in review as PR 91" are wrong the moment PR 90 merges; whoever merges K1 should drop K1 from that sentence (and add it to "Done so far" with its SHA) in the same step, or the line can say "K1, C4a are in flight" without PR numbers. (b) Wave 0 heading: "new code that nothing calls yet" is not literally true for A2a/A2b: `DomainSession.Lower` calls `DeclareType` and `Register` for the `SyntaxModule` placeholder (`DomainSession.cs:154-156`). V11's wording was "unwired new code"; use "new code not yet wired into Emit or the consumers". (Mill claim 2, kept as a nit.)

### F182 [nit] Difference 5 and "What PRs 82 and 83 changed" list different leftover helpers
Difference 5 (line 153): left on master are `BindModuleMethodBody` and `BindForSimulate`. The history section (line 161) says PR 82 left `BindForSimulate` and `AsVoidResultBody`. The code has all three (`DomainEntityInstance.cs:884` which wraps `BindForSimulate`, `:925`, and `:802`, used at `:784` and `HostAbi.cs:282`). Neither list is false; name the same set in both ("`BindModuleMethodBody`, `BindForSimulate` and `AsVoidResultBody`").

### F183 [nit] Difference 8 lists "MCP session state" next to the DEI violation
`pipeline-stage-map.md:156`: "Domain is not purely structured data for consumers (DEI holding `Domain`: violation to retire). It is also the live object consumers hold (`DomainEntityInstance.Domain`, MCP session state)." The MCP session store must hold the authored `Domain` (decision 6), so it is not a violation; the title ties the violation to DEI, so it is not a contradiction, but a one-clause "(the MCP session holding the authored Domain is allowed, decision 6)" would close it.

## Mill disposition

Kept: the "nothing calls yet" wording (partly, F181b); everything else it said is a closure confirmation ("F160-F167: all eight are addressed").

Dropped: (1) "`BindModuleMethodBody` appears nowhere in the reviewed docs and was previously the only one listed": false, it is in the code (`DomainEntityInstance.cs:884`) and was in the 6602517a text of difference 5; (3) "dropping 'cached analysis directly' grants blanket absolution": no code under `Poly.Mcp` references `RuntimeAnalysisCache` (grep empty), so that phrase was not backed by the code, and decision 6 is stated correctly; (4) "decision 5 names only Minimal API and DbContext, the earlier text also listed the HTTP file generator": `demo.http` is produced inside `MinimalApiGenerator` (`:1103, :1154`), so it is covered by the Minimal API generator; (5) "A5a/A5b mapping unverifiable": it is in the plan (A5a "Contributors return artifacts", A5b "Emit files and generator trees registered with references").

## What Foreman needs

- **SHIP, gate YES, bugs 0.** K1 is on the V11 list, so Foreman may merge PR 90 at `f3f0dd36`. It merges clean onto master `afa246c2`.
- At merge time, optionally touch plan line 3 (F181a): K1 moves from "in review" to "Done so far" with the merge SHA. The other nits (F180, F181b, F182, F183) are one-sentence edits that can go in the next docs slice.
