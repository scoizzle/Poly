# Final Boss re-verify: PR 84, tip f5ce4a95 (closes the NOT SHIP at 445066a6)

- PR: scoizzle/Poly#84, head `f5ce4a9549d6994c4f58e78e5c2631e00b5c5910` (re-checked against GitHub just before filing: unchanged). CI green, frozen.
- Commits since 445066a6: `3bc322be` (F72-F74), `8818446e` (F76-F94 except whitespace), `f5ce4a95` (hand edit: V10 settled, pending markers dropped, C6a depends on T2, whitespace).
- Master: `945a2164` (unchanged). Code in the review tree equals master. Stage map is byte-identical to 445066a6 (not edited).
- Prior review: `docs/agent/reviews/2026-10-03-pr84-445066a6-final-boss.md` (+ `-followups.md`), branch `review/finalboss-pr84-445066a6`.
- Mill: OpenCode, read-only prompt, `OPENCODE_DB` unset, V-source text pasted into the prompt. **`opencode-go/deepseek-v4.1-flash`** died mid-run (it tried to write a scratch file; the write was rejected and ended the session) and printed no review. **Fallback `opencode-go/mimo-v2.5`** completed and produced a review (verdict SHIP, 15 findings). Every mill finding and every claim below was re-checked by hand (see "Mill disposition").

## Verdict: SHIP (bugs 0; open: 3 suggestions, 8 nits)

F72, F73 and F74 are closed against current master code, the counts cascade correctly, V10 is settled everywhere, decisions 1-15 and V1-V9, V11 are intact, no wave is released, `git diff --check` is clean, and the hand-editability gate is YES. What is left is polish a reader can fix by hand (F95-F105). Same shape as the abc6181c SHIP.

## 1. Scope and byte checks

- `git diff origin/master..HEAD --stat`: 3 files, all under `docs/domain-modeling/` (plan 1074 lines, decisions 219, stage map 169), +1462. PASS.
- `git diff --check origin/master..HEAD`: **clean** (0 lines). Delta `445066a6..f5ce4a95` touches only the plan and decisions.md.
- Whitespace fix is sane (F75): every old trailing double space was on a bold slice heading (no other hard breaks existed in any of the three files). Each heading is now followed by a blank line, then the `_Lane ..._` line. A script confirms all 82 headings have the pattern heading / blank / `_Lane`. Rendering: the lane line becomes its own paragraph instead of a soft-broken next line; meaning is unchanged. No EOF blank line.

## 2. F72-F94

| F | Claim | Result |
|---|---|---|
| F72 | Q1a = parity rows | **Closed.** `plan:455-461` Q1a "Parity rows for collection rules", S, Review 1, Depends T2, Files Poly.Tests only. Facts check on master: `LowerFilteredQuantifier` defined `DomainExpressionLoweringPass.cs:503` (calls `:475, :478, :481, :488`), class remarks `:29-30`; `git grep AnyRelated|AllRelated|CountRelated` empty; existing tests `Export_HasOverdueLoans_PrintsForeachOverLoans` (`:3034`), `Patron_HasOverdueLoans_SimulateAndGeneratedCSharp_Agree` (`:3045`) exist. Table row `plan:344`, sizing `:1019`, V10 text all say "already lowered". Residuals: F95, F100 |
| F73 | Q1b = regenerate demo + test | **Closed.** `plan:464-471`; `demo/Poly.RestApi/Patron.cs:112` still throws "requires store-aware evaluation ..." (confirmed); exporter says "no per-policy stub" (`DomainToCSharpExporter.cs:378`); nothing under `Poly/` produces the message; no test compares the demo to fresh output; `demo.http` has "Action: Reinstate" (`:64-65`); `LibraryCheckoutDsl` is `DomainToCSharpExporterTests.cs:20` with the same five entities as the demo (Book, Patron, Loan, Fine, PremiumPatron) |
| F74 | C6a covers the printed twin | **Closed.** `plan:509-516` names `TryAutoLinkUnambiguousOutbound` (`HostAbi.cs:820`; callers `HostAbi.cs:782`, `DomainInstanceStore.cs:226`), the printed twin (`StoreBind.cs:105-116`, `237-243`), `FindAutoWireBackReference` (`Actions.cs:846`; other callers `Notify.cs:126`, `StoreBind.cs:277`, all confirmed), `TryLinkCreateInBackReference` (defined `HostAbi.cs:838`; plan says "~833"). Files now include `StoreBind.cs`; done-when requires a T2 parity row "bare `create Type` with one matching many-relationship is unlinked in both" plus a second row for explicit create-in; NOT SHIP if simulate and print disagree. Depends now T2 (T2 is earlier in the sequence). Size M, Review 2, consistent everywhere. Open question to Scot (does decision 10 cover the back-ref slot choice) is correctly put to him at slice start, not decided |
| F75 | diff --check | **Closed** (section 1) |
| F76 | files not in repo | **Closed in plan and decisions.md.** `g1-gate-probe.diff` replaced by an inline probe (`plan:83`: add the HasErrors throw, do not commit); no `bin/mill.sh`, FB/R ids or `/workspace` in plan or decisions.md. Stage-map `:169` still cites `/workspace/poly-ro` and a raw-findings file: now explicitly K1's job (`plan:664`); see F97 |
| F77 | principle 7 | **Closed.** "Release rule" defined once at `plan:13`; no "principle 7" anywhere |
| F78 | wrong citations | **Closed.** Spot-checked (section 6): `~949-953`, `484/519/529/557`, `97 ... 1235`, `658/729`, `294`, `169-174` and `203-208`, `ExtensionCatalog.cs:57-58` (+ `Core` `:29-32`), G1 overload text (both overloads exist: `DomainProgramProjection.cs:21` and `:32`), 61, 149/37, `:53`/`:48`. Residuals: F99, F100 |
| F79 | post-merge wording | **Closed in the plan** (D5 `:352` past tense and now correct; wave 1 "PRs 82 and 83 are merged"; the "Depends on: PRs 82 and 83 merged" lines are satisfied dependencies, harmless). Stage-map `:72, :123, :167` are now named in K1 (`plan:664`); see F97 |
| F80 | voice / roster | **Closed.** No first person, no Razor/Ontologist/100x/Warden/Sentinel/Grok in plan or decisions.md; Foreman and Final Boss named once in section 0 (`:14`) and in Scot's decision 15. "Review 1 / Review 2" defined in section 0 (`:15`). Residual jargon: F102 |
| F81 | V9 vs H4 | **Closed.** H4 (`plan:~769`): "each entry names the slice that closes it, or is a permanent entry with owner Scot (for example instance delete, V9)" matches V9 |
| F82 | N1/K6 | **Declined in part; accept** (section 4) |
| F83 | decision 8 | **Closed.** Row 8 is clean; "narrows the work" appears only in a Notes line about the work (K4), not the ruling; K4 is verification only |
| F84 | status lines | **Closed.** No `abc6181`; "sign off PR 84" everywhere (`plan:3, 890, 937`); HP0 "V1 to V11 are answered" |
| F85 | hard-coded count | **Closed.** T1 SHIP: "same pass count as master before and after, 0 new failures"; 110 / 2895 dated to `9db8868f` with "has since grown" |
| F86 | stage-map:169 | **Closed as ownership** (K1 names line 169); stage map itself deliberately unedited |
| F87 | T5 | **Closed.** "the storage structure test (inside C8a1)"; no T4/T5 |
| F88 | A2 | **Closed** (A2a/A2b in row 15 and wave 0). "A2/A5 are split" at `plan:1029` is history |
| F89 | V1 squash | **Closed.** decisions.md V1: PR 82 `16a895dc`, PR 83 `945a2164` |
| F90 | count slips | **Closed.** One number "about 91" at `:40` and `:81`; 61; 21 sites |
| F91 | graph edges | **Declined; accept** (section 4) |
| F92 | row 11 / tails | **Declined in part; accept** (section 4); rows 8, 9, 15 tails moved to a Notes line (verified) |
| F93 | "Verify line" | **Closed.** Gone; A6/A7 "named consumer ... Scot approves the wave" |
| F94 | K3a to K3c | **Closed.** Lane lists read `K3c, K3a` / `K3a, K3b, K3c` |

## 3. Counts cascade (recounted by script)

- 82 slice headings, 82 distinct, 82 in the full sequence (`:910`), no id missing or extra, no duplicates, no dependency placed after its dependent.
- Sizes from headings: **38 S / 43 M / 1 L** (Q1a L->S, Q1b M->S, C6a S->M). Matches section 16 text (`:1029`) and every row of the sizing table (all 82 entries compared one by one).
- Reviews: **42 Review 1 / 40 Review 2**, 42 + 80 = **122** passes: matches `plan:923`, section 16, `decisions.md:197` (V11).
- Lanes: 36 A / 46 B; wave lists and lane lists contain Q1a, Q1b, C6a, Q2 in the right lanes.
- Q1a no longer blocks C5a (C5a depends only on C3b) or C10 (no Q1a); graph edge `T2 --> Q1a` only; Q1b stands alone as a bare node (consistent with "Depends on: none"; see F91 decline).
- "13 new slices" list still includes Q1a, Q1b, Q2 (+ P1 conditional): correct.
- Section refs: `plan:275` "see section 7 for Q1a and Q1b; Q2 is here" is right (Q1a/Q1b are in section 7, Q2 in section 6); section 19 reference at `:17` right; "section 2" / "section 1" refs right.

## 4. F82 and F92 (and F91) decline judgments

- **F82 (keep N1 and K6 on the standing-merge list): ACCEPT.** The list is the one Scot approved in V11 (a), so an agent removing items would change his answer. The doc now says the true thing in the place he answers (`decisions.md:197`, "N1 can change MCP-visible severity text and K6 changes Emit behavior; they are on the list as approved; Scot can take them off") and the category wording matches the source ("unwired-new-code" restored in both plan `:928` and decisions.md). Condition for full acceptance: nit F103 (the caveat is only in decisions.md, not in plan `:928`).
- **F92 (do not add wording to row 11): ACCEPT.** Row 11 is Scot's verbatim text including the create/link/unlink exception; the points I raised (invariants still apply to the exception, cross-entity `invoke` as outside influence, root `create_instance`) are enforced by slices C6b/C6c (link/unlink trees), M1 (positive tests), C8c2 and T3 (matrix, "create" row). Adding editorial wording to a ruling would be worse. The editorial tails on rows 8, 9, 15 were moved out, which was the real F92 defect.
- **F91 (do not add graph edges): ACCEPT.** Caption now reads "principal edges only; each slice's Depends line is authoritative", and my scripted check shows the Depends lines have no order violations. Only a cosmetic bare `Q1b` node.

## 5. V10 and leftovers

- V10 is **SETTLED** in `decisions.md` (V10 heading block: "Answer (Scot, 2026-10-03): settled, revised scope" with Q2 stays, Q1a parity test, Q1b regenerate + equality test, and the reason), in `plan:3`/`:17` ("V10 as a revised scope"), wave 2 lists (`:899-900` "V10 = revised scope"), HP0 (`:937`, "V1 to V11 are answered"), `plan:1074` and decisions.md header ("V1-V9 and V11 as recommended; V10 as the revised scope").
- Greps for `pending V`, `V10 pending`, `awaiting V`, `V10 is open`, `reopen` (case-insensitive), `return to Scot`, `back to Scot`, `principle 7`, `abc6181`, `bin/mill`, `g1-gate-probe`, `/workspace`, `T5`, `FB-`, `R22`, `.v2.`, `Razor|Ontologist|100x|Warden|Sentinel`: **no hits in the plan or decisions.md**. The only hits are in the stage map (`:146` the English word "sentinel"; `:169` the K1-owned `/workspace/poly-ro` pointer). "Can start now / release wave" greps are empty.
- V10 is the only answer that is not "as recommended"; the doc says so in three places and gives the reason. No contradiction with Scot's settled text.

## 6. Decisions 1-15 and V1-V11

Section 19 (`plan:1048-1074`) read in full. All rulings present with Scot's wording and numeric order:

| # | Verdict |
|---|---|
| 1, 2, 3, 4, 5, 6, 7, 9, 10, 12, 14 | Preserved |
| 8 | Preserved, clean ("Compile into the printed output; no simulate-only behavior") |
| 11 | Preserved verbatim, including "Create, link and unlink are effects of relationship operations ... only time an outside entity can influence another entity's state. The relationship owns those lifecycle effects (think RAII). So they are the one named exception to 'only named actions mutate their own entity'." Row 1 of section 1 repeats the exception |
| 15 | Preserved: "No preference. After you sign off, in the order Foreman picks. Until Scot signs off PR 84, A1, A2a, A2b, H1, N1 and N3 stay HELD." |
| 17 (RuntimeEnumTypeProvider interim, PR 82 merged) | Preserved: "Accepted as interim; removed in the enum slice (E1)"; D5 `:352` says PR 82's provider goes with E1 |
| 13, 16, 18, 19, 20 | STAY OPEN (`plan:1072`, with the lane that triggers each question) |

V1-V11 (decisions.md) against the V-source: V1 done (PR 82 squash `16a895dc`, PR 83 squash `945a2164`), V2-V9 and V11 (a) as before, V4 "(a) yes ... but docs recommend authoring them as named actions" (plan `:24` and section 1 repeat it), V6 "(a) a rename is a new id; stands unless Scot overrides", V9 "(a) analysis only for now, permanent entry on the H4 known-gaps list" (now consistent with H4), V11 slice list unchanged apart from the count text 41/41 -> 42/40. Primary record of Scot's answers is still Foreman's relay and the settled V10 text (not independently verifiable).

## 7. HELD / no wave released

- `plan:3`: "Code slices are still HELD until Scot signs off PR 84". Wave 0 (`:890`) "after you sign off PR 84"; wave 1 "PRs 82 and 83 are merged (hold point HP1 reached); wave 1 still starts only on Scot's word"; HP0 releases T0, A1, A2a, A2b, K1, H1, N3, T2 and "N1 also needs decision 16"; decision 15 keeps A1, A2a, A2b, H1, N1, N3 HELD. `decisions.md` V1/V11: standing approval and "the release rule" release no wave. No "can start now" lines exist; the precedence note remains unnecessary. PASS.
- New in the hand edit: nothing releases a wave. See F96 for the wave-0 header description.

## 8. Code claims (master 945a2164): 45 checked

Confirmed: `HostAbi.cs:85-87` (silent bare return in `TransitionStage`), `:294` `MaterializePeerInSyntax`, `:658`/`:729` `FillCreateDefaults` callers, `:782` and `:820` auto-link, `:838` `TryLinkCreateInBackReference`; `DomainInstanceStore.cs:149-150, 174-175, 226`; `DomainEntityInstance.cs:328` `SetProperty` (0 product / 13 test callers), `:559` stage-policy loop, `:949-953` unbound adapter failure (file is 1039 lines); `Actions.cs:128, 238, 484, 519, 529, 557, 846`; `EffectLoweringPass.cs:97, 127, 266, 541, 573, 770, 1038, 1215, 1235`, `LowerCreateInProbe` return null at `:1037-1039`; convenience ctors `:53` / `:48`; `Notify.cs:126, 438, 448-450`; `StoreBind.cs:105-116, 237-243, 277`; `DomainToCSharpExporter.cs:371-374, 378-379, 481-489`; `DomainSession.cs:169-174, 203-208`; `RuntimeAnalysisCache.cs:109`; `DomainProgramProjection.cs:21` / `:32` / `:41`; `Interpreter.cs:59` (`CompileChecked`), `:76-81`; `DirectVmAbiEmitter.Statements.cs:424-430`; `DirectVmAbiEmitter.Invoke.cs:481`; `DomainTools.cs:1273-1276`, `1310-1331`; `McpSessionStore.cs` (in `Poly.Mcp/Sessions/`) `:89-92`; `ExtensionCatalog.cs:29-32, 57-58`; `NodeId.cs:23-24`; `PolyDslParser.cs:1524-1525`; `DomainDslPrinter.cs:799`; `OracleTool.cs:399-404`; `poly-dsl-guide.md:73-75`; `SliceCProducerLoopCatalogTests.cs:75-90`; `MinimalApiGenerator.cs:148-149`; `DbContextArtifactContributor.cs:28`; `BindModuleMethodBody` still present (`DomainEntityInstance.cs:884`), `BindThis`/`RewriteVoidFailClosedThrow`/`BindExportBody` gone; `ExportDomainToCSharp_WithPeerAnalysisError_FailsClosed` exists (`SurfaceExtensionDogfoodTests.cs:860`); 61 `new LoweringContext(`; 450 `DomainEntityInstance.Create` calls in 37 test files, 149 in `DomainEntityInstanceTests.cs`; `demo/Poly.RestApi` has 8 `.cs` files plus `demo.http`. A bulk script found every `file:line` in the plan and decisions.md within the file's length (49 refs, 80 numbers). Wrong or imprecise: F99, F100 only.

## 9. Hand-editability gate

**Answer: YES.** Scot can open the plan and decisions.md cold and change them by hand:
- Every slice is the same card (scope, files, done-when, SHIP / NOT SHIP, hand-edit), now preceded by a blank line, so no editor setting can break the layout.
- The T1 probe is described inline (a few lines to add, do not commit); the process line about a script the repo does not contain is gone; the release rule is defined once; agents and chat are no longer referenced; counts are relative.
- decisions.md is the cold interface for his answers: each V has Answer / What it is / Options / Blocks / If you do not answer.
- The plan and decisions.md agree on V10 scope, V11 list and counts, C8a1, HP0 and the standing-merge list. The hand edit f5ce4a95 added no contradiction (Depends T2 on C6a is correct; Q1b sentence now says where the Library domain lives).
- Remaining soft spots (not blockers): the stage map still has two out-of-repo pointers and stale wording until K1 runs (F97, F98); a few jargon words ("mill") are not defined (F102).

## 10. Findings

Severity: bug = a reader would act on something false; suggestion = should be fixed, can be done by hand at or before the slice; nit = polish. No bugs.

### Suggestions

- **F95 [suggestion] Q1a's done-when cannot be met by Q1a.** `plan:460`: "... the H4 Constant check is present", but H4 (`plan:~769`, Depends A3a only) is 20 places later in the sequence (`:910`: Q1a #34, H4 #54), and Q1a's Files line says "Poly.Tests only". Fix: either state the Constant check as Q1a's own test (and let H4 reuse it) or remove the clause and keep it in H4.
- **F96 [suggestion] Wave 0 header is false.** `plan:890`: "everything here is test-only, docs-only; `ExportedCSharp.cs` is untouched ..." but wave 0 holds A1, A2a, A2b (new unwired code: "SHIP if the type is referenced only by its tests"), plus H1 and N3 measurements. The old text said "or touches nothing PRs 82 and 83 touch". This is the line Scot reads when he decides what sign-off releases. Fix: "test-only, docs-only or new code nothing calls yet".
- **F97 [suggestion] K1 now owns eight stale stage-map items but its done-when checks one.** `plan:664`: K1 scope covers lines 66-72, 92, 107, 123, 157-167, 169 and the "Not a PR" header, but "Done when" is only `git grep "exactly two"` and section 2.5. F79/F86 are closed by ownership, so K1's checks must catch them. Fix: add greps with expected empty output for "after PRs 82", "Suggested order after PRs 82", "findings-raw", "/workspace", "Not a PR", "no PR exists".

### Nits

- **F98** Stage-map `:3` gives PR 82 and 83 as `d7c1da48` / `c2265740` (branch tips) while decisions.md V1 gives squash SHAs `16a895dc` / `945a2164`. K1 should use one set.
- **F99** `plan:377` cites `Invoke.cs:481`; the file is `Poly/Interpretation/Vm/DirectVmAbiEmitter.Invoke.cs` (there is also `Poly/Ast/Nodes/Invoke.cs`).
- **F100** `plan:458` says "`LowerFilteredQuantifier` at ~475-481" and "class remarks at lines 27-31": `475, 478, 481, 488` are call sites; the definition is `:503`; the remark is `:29-30`.
- **F101** C6a still lists "Depends on: ... PRs 82 and 83 merged" (and 12 other slices). Satisfied, harmless; drop at next edit.
- **F102** Undefined jargon and voice mix: "mill" (`plan:11, 16, 923`; decisions.md `:204`); decisions.md V11 (`:197`) says "Scot can take them off" in a file that otherwise says "you"; "the five reviews" (`plan:3, 17`, decisions.md `:8`) refers to reviews outside the repo (informational only).
- **F103** The N1/K6 caveat is in decisions.md V11 only; `plan:928` (section 13 item 5) still lists the categories without it. Also V11's hand-merge list still names Q1a as "risky" though Q1a is now S, test-only, Review 1 (a hand merge is the safe side).
- **F104** `plan:593` (C8a1) says "already trees by this point (C2b, C4b, C4d, C5, C6)": C5 and C6 are now C5a/C5b and C6a-C6c.
- **F105** `plan:512` cites `poly-dsl-guide.md:73-75, 141`: `73-75` describe auto-link; `141` is a `create Fine { ... }` example inside a `when` handler. Whether line 141 depends on auto-link is not clear to me; C6a should say "check the guide for every bare `create`".

### Parked and prior items

F54, F55 (stage-map headers) and F64 are owned by K1/F97 and no worse than at 445066a6 for the plan; F64 is improved. F56, F58, F62, F65-F71 stay closed (F65/F66/F68 as noted in section 4).

## 11. Mill disposition

The mimo-v2.5 review (verdict SHIP, F72-F94 table, 15 numbered findings, ~45 spot checks) was used as a cross-check. Accepted after hand checks: its table of F72-F94, the dependency, count and V10 greps, the `Invoke.cs:481` path (F99), `LowerFilteredQuantifier` location (F100), stage-map `d7c1da48/c2265740` vs squash SHAs (F98), "mill" jargon and the bare `Q1b` node, the C8a1 shorthand (F104), the standing-merge tension (kept as a declined-and-accepted item, F82). Rejected or dropped: its claim that Create counts are "148 / 38" (I measure 149 in `DomainEntityInstanceTests.cs` and 37 files, which is what the plan says); its "DbContextArtifactContributor line 28 vs 30" and "`HostAbi.cs:85-87` vs 85-86" (trivial); "C6a guide line 141 wrong" downgraded to F105 because I could not establish that the example does not rely on auto-link. The mill missed F95, F96, F97 and F103. Its "SHIP" verdict agrees with mine; its gate answer (PASS) agrees.
