# Poly board (lane status; the plan pick lives in PIPELINE-STATUS)

Updated: 2026-10-06 06:27 CDT. Master 11287134. Top slice: PR 113 post-merge review (Razor, OpenCode) → 100x fixes as one PR → FB. Open code PRs at time of writing: 110 C1a (CI red, BLOCKED; rebases after the 113 fixes land), 115 F380 (FB review). 116 F304 (FB review).

Lives in the repo at `docs/plans/poly-eng/board.md` (moved from the box folder 2026-10-06; see [README](README.md)). Task files: [`tasks/`](tasks/).

Repo: https://github.com/scoizzle/Poly.git

## Working orders (Scot 2026-09-22)

- **Anti-stall:** tip unmoved ~45–60 min → re-fire once; second empty → hard-block + Chieftan. Board/git tip only.
- **Mill split:** Implement = Grok Build; Reviews = OpenCode `deepseek-v4.1-flash` or MiMo (`mimo-v2.5` / `mimo-v2.6-flash`) only — no Grok on review lanes. Dual pass = other lab.
- **Hand-editability gate (Scot 2026-09-27):** every code review (Razor + Final Boss) answers "could Scot change this by hand, opening the files cold, without an agent?" A no is a finding at suggestion or higher (never a nit) and blocks ship until fixed or Scot waives. Checklist: the hand-edit gate bullet in [convergence plan §0](../../domain-modeling/pipeline-convergence-plan.md#0-how-to-read-this). Implementer TASKs: smallest readable change.
- **Named F# fix-up** (after Final Boss ship / Scot HOLD): implementer → Final Boss only (skip Razor).

## Poly core principles (Scot 2026-09-27; govern every lane, review, doc)

1. **Why:** roadmap P1 sellable business-grade software from domain modeling (priority); P2 any software + agent review feedback into product; P3 neurosymbolic (weights heuristics become simple code, cheap to run).
2. **Principle zero:** the simple syntax tree is the unit of meaning; the interpreter simulates those snippets efficiently. Every other layer only produces those trees or consumes their results.
3. **Interpreter is a debugger:** step/pause/inspect in-process for any agent; every layer stays steppable and traceable to its source snippet.
4. **Code over docs:** implemented code (esp. Scot's hand-written core) is truth; docs/decisions are background ideation.
5. **Hand-editable:** Scot can open any file cold and change it by hand; a no is a real review finding.
6. **Composition:** libraries may depend on libraries, every chain ends at core; refactor tangles behind clearer abstractions, never wipe.
7. **Order:** document, then gauge pain, then fix. No fixes or new refactor slices until Scot signs off each phase.

## North star

**Verb→noun:** Domain → Load → Analyze → Lower → Interpret/print.
session.Lower plan **Accepted**. Not PIPELINE-STATUS CURRENT.
Campaign: Session Compile. One slice at a time.
A `20e35a4c`; B `1f94b94d`.

## Open lanes (eng WIP: 2, one PR per lane; implementer 100x for both; reviewers per row)

Plan: [`docs/domain-modeling/pipeline-convergence-plan.md`](../../domain-modeling/pipeline-convergence-plan.md) on master. Wave 1 (historical; T1, C0, K0, B1, G1, A3a, C4e, N4 are merged): order (Lane B): C0, T1, K0, B1, C1a (= P3-D), then G1 after T1. Lane A after N3: A3a, C4e, N4 (N1 waits decision 16). Next risks: T1 (11 test files, 12 of 110 failures unexplained), A3a (goldens byte-identical), B1 (cause unknown). Land C0 and K0 before C1a. H2/K6 need resizing (H1: 13 of 14 sample domains have VM errors) - propose split in wave 2 planning.

| Slice / PR | Tip | Lane / owner | Status / next |
|-----------|-----|-------------|---------------|
| **PR 113 review** (post-merge) misc: shrink agent surface, fail closed on dirty lower | merged `f398616e` (diff `2e70e477..f398616e`, 184 files) | top / Razor (OpenCode) then 100x | Scot merged unreviewed 2026-10-05. Razor post-merge review, findings in one table. Then 100x lands all fixes as one PR → Final Boss → merge. Everyone adopts 113 as baseline (short get_dsl_guide + section 12; PIPELINE-STATUS.md only CURRENT; Lower/Emit throw on analysis errors). Task [tasks/PR113-review.md](tasks/PR113-review.md). |
| **PR 110** C1a root program parameters https://github.com/scoizzle/Poly/pull/110 | `d7b6b9a1` | B / 100x | CI red, BLOCKED. Rebases onto master after the 113 fixes land, then Razor → FB. Task [tasks/C1a.md](tasks/C1a.md). |
| **PR 115** F380 (M1b) assign target reaching another entity anywhere in the target https://github.com/scoizzle/Poly/pull/115 | `0fc64633` | A / 100x (hand) | In review: Final Boss Review 1 on OpenCode. Task [tasks/F380.md](tasks/F380.md). |
| **F304** printer quotes contract source/version (quoting follows the parser's identifier rule) | PR 116 @ab2902d7 | B / 100x | Filed 2026-10-06. Review 1 (Final Boss, OpenCode). Task [tasks/F304.md](tasks/F304.md). |

Merged this wave: T0 bde1f7c5, A1 8e5ef81b, A2a 08eda8eb, A2b 16403b60, T2 afa246c2 (Scot), K1 7fb4b2a2 (PR 90), H1 47220d4f (PR 92), C4a dac9a6a8 (PR 91, Scot hand-merge). Plan status line fixes: H1 fixed by PR 93; C4a fixed in the C0 PR.
Open nits to fold into a later docs slice: F180-F183 (K1), F210-F212 (H1; F212 K6 text says TryAnalyzeForEmit returns null on failure but code returns null only for empty module), F170-F175 (C4a; F174 add equality-on-assign note to C4c card). C8d done-when must include MapModuleRequireFailure mapping.

## Closed / dropped

- **PR 75 / Slice B** — `1f94b94d`
- **PR 74 / Slice A** — `20e35a4c`
- **PR 64** DEI RestoreActionState — closed

## Merged recently

- 2026-10-05 (all squash-merged by Scot, CDT): #113 misc shrink agent surface `f398616e` 08:32 (unreviewed → post-merge review 2026-10-06); #106 N4 `9178b9de` 09:30; #105 A3b `331e4888` 09:37; #108 G1 `97652cfe` 09:40 ([task](tasks/G1.md)); #112 NU1903 `b57bbc27` 09:42 ([task](tasks/NU1903.md)); #111 A4 `3f5a7617` 09:45 ([task](tasks/A4.md)); #104 C4e `36471155` 09:51; #101 F292/F293 `11287134` 09:56 ([task](tasks/F292-rework.md)). Earlier same day: #109 B1c `e9e24a67` ([task](tasks/B1c-F291.md)); #103 M1 `2e70e477` (F380 follow-up card remains); #107 N7 `56a16ab4`; #102 B1b `dc7029d8`.
- 2026-10-04/05 merged rows moved off the open-lanes table: A3a PR 95 `89098229` (goldens byte-identical; F263 later answered by N5); T1 PR 98 `dd7d4204` (10 residuals carried: G1/G3/redesign/gap/N6); N6 PR 100 `bac2e8d5` (Text+Text analyzer); N5 PR 99 `bc7dfe42` (MCP add names match DSL); K0 PR 96 `2b3bdd64`; B1 PR 97 `076a9014`.

- 2026-10-04: N3 PR 93 6dbab628; PR 85-92 and PR 91 (see Open lanes list); master dac9a6a8.

- **PR 81** docs: lean AGENTS/CORE + Session Compile alignment — `9db8868f` (2026-09-28 20:48 CDT, master CI green)
- **PR 77** DslTokenWriter.IsWord reshape — `640025f7` (Scot, 2026-09-27)
- **PR 75** Slice B — `1f94b94d`
- **PR 74** Slice A — `20e35a4c`

## Parked (no work)

- Pre-existing (base too, Warden 19:45): subscription handler containing a transition prints `this.Notify(...)` → does not compile.
- F25 (PR 79 Razor #5, pre-existing on master): PolicyConstraintAnalyzer.cs:311-336 never checks a quantifier nested inside another quantifier body (e.g. `any parts where (any widgets where Qty exists)` gets 0 diagnostics; simulate and export both fail loudly). Logged 18:58.
- Pre-existing simulate-vs-print mismatches found during PR 79 (not in 79 scope; candidates for P3-C or a follow-up, may need a Scot policy-semantics call): (a) number-valued policy `policy { N }` simulates nonzero=true but printed `bool P() => this.N` fails CS0029; (b) plain `Name exists` / `N exists` policy outside `for` disagrees when unset. Logged 18:22.
- `when first` subscription quantifier (fire on first entry into watched stage). Scot 2026-09-27 17:05: future idea, NOT now, no work.

Parked (added 23:01): F7 DSL left-hand path wraps whole comparison (DslExpressionParser.cs:190-199; `advisor Age > Age` self-compare; `advisor Age < advisor mentor Age` print CS1061; flip UnlinkedComparisonAgreeTests.cs:122 if fixed). Value type named {Entity}Stage (e.g. OrderStage) passes analysis, print CS0101 (pre-existing).
Parked (added 23:35, pre-existing on master, Final Boss PR 82): F42 binder name matched at any depth (DomainExpressionLoweringPass.cs:154-164, :427), `Balance < advisor order Total` with `as order` True where False is right. F44 quantifier-body cardinality checked against outer entity (:258-259), export throws when Order `product: many` and Item single `product`.
Parked (added 23:52, pre-existing, Final Boss PR 82): F46 hop to bool property outside a comparison unguarded (DomainExpressionLoweringPass.cs:180-182), `advisor Active` as policy root, `advisor where Active and mentor Active` mentor unlinked. F44 also reachable outside quantifiers (`advisor mentor Age` with Customer `mentor: many`).
Parked (added 00:22, pre-existing, Final Boss PR 82 sweep): F47 WalkPathPrefixRequireGuards walks quantifier bodies on outer entity (DomainToCSharpExporter.Actions.cs:908-932), `require Q` with `Q: any items where Qty < product Stock` CS1061/VM reject or false refusal. F46 expanded: `if (advisor Active)`, `any items where product Active`, `assign Score to advisor Age`, `create Note { Val: advisor Age }` unguarded. Candidate follow-up lane with G1 after PR 82+83 merge.

Scot decisions 2026-10-03: V1-V11 on the v2 plan ALL as recommended (file pipeline-convergence-plan.v2.decisions.md). V4 = yes, entry/exit/when count as named actions, docs recommend authoring them as named actions. V6 rename = new id stands unless Scot overrides. Open older decisions: 13, 16, 18, 19, 20. First-wave code slices (T0, A1, A2a/b, K1, H1, N3, T2, and wave 1) HELD until Scot signs off PR 84.
WAVE 0 (v2 plan, released 2026-10-03 21:40): T0 Lane A 100x (review 1 Final Boss); T2 Lane B 100x (review 2 Razor then Final Boss). Queue after T0: A1, A2a, A2b, K1, H1, N3. Wave 1 (HP1 reached on 82/83 merge: C4a, C0, T1, K0, B1, C1a=P3-D, G1 after T1) follows wave 0 per plan section 12; confirm with Chieftan whether HP1 release is covered by 'full send' before starting.
T0 -> PR 85 https://github.com/scoizzle/Poly/pull/85 @ `c341b546` (6bf03ff6 Grok Build, c341b546 hand; CI green, 2946 tests, diff only Poly.Tests). Final Boss one pass on OpenCode assigned 2026-10-03 21:56 -> merge under V11 standing approval if SHIP + gate YES. PR 73 closed unmerged by Scot (head da2e3c2), removed from board.
T0 MERGED: PR 85 squash bde1f7c5 2026-10-03 22:08 (Final Boss SHIP c341b546, gate YES, V11 standing merge; follow-ups F106-F108 suggestions, F109-F113 nits). A1 (Lane A) -> 100x assigned 22:08, Final Boss one pass after. T2 (Lane B) in progress with 100x. Remaining Lane A queue: A2a, A2b, K1, H1, N3.
A1 -> PR 86 https://github.com/scoizzle/Poly/pull/86 @ ec0a5bbf (hand, no mill; CI green, 2978 tests, 2 files). Final Boss one pass assigned 2026-10-03 22:12 -> merge under V11 if SHIP + gate YES. T2 still with 100x.
T2 -> PR 87 https://github.com/scoizzle/Poly/pull/87 @ 07c2a28d (4083423b Grok Build, 07c2a28d hand; CI green, 2940 tests, test-only; all 10 samples compile so no B1 gaps; 2 real simulate/print divergences in PR body for later failing-first rows). Razor exhaustive first pass on OpenCode assigned 2026-10-03 22:14 -> Final Boss second pass -> merge (T2 not on V11 low-risk list: Scot hand-merge unless Chieftan says otherwise).
A1 PR 86: Final Boss SHIP @ ec0a5bbf 22:34 (gate YES, 0 bugs, F114-F120 non-blocking). Asked 100x for comment-only F117 fix (+ cheap F114-F116) 22:35, then Foreman verifies diff and merges under V11. Razor on PR 87 (T2) in progress.
A1 MERGED: PR 86 squash 8e5ef81b 2026-10-03 22:41 (Final Boss SHIP ec0a5bbf; final tip d0252f4d = comment + F114-F116 tests only, verified by Foreman; V11). A2a (Lane A) -> 100x assigned 22:42 -> Final Boss one pass -> merge under V11. T2 PR 87 with Razor. Queue: A2b, K1, H1, N3.
A2a -> PR 88 https://github.com/scoizzle/Poly/pull/88 @ 0a0b6555 (hand, no mill; CI green, 2987 tests, no .golden changed; also EmitGoldenTests one-line helper). Final Boss one pass assigned 2026-10-03 22:46 -> merge under V11 if SHIP + gate YES.
A2a PR 88: Final Boss NOT SHIP @ 0a0b6555 23:23 (1 bug F121 Artifacts exposes mutable list via IList cast; gate YES; F122-F128 suggestions/nits). 100x fix assigned 23:24 -> Final Boss re-verify.
A2a PR 88: 100x fix pushed dddaf5c8 23:29 (F121-F125 closed, CI green, 2991 tests, goldens untouched) -> Final Boss re-verify assigned 23:30 -> merge under V11.
A2a MERGED: PR 88 squash 08eda8eb 2026-10-03 23:49 (Final Boss SHIP dddaf5c8, gate YES, 2991 tests, goldens untouched; V11). Open nits F126, F127 (plan A2a Files line stale -> fold into K1), F129-F131. A2b (Lane A) -> 100x assigned 23:50. T2 PR 87 still with Razor. Queue: K1, H1, N3.
A2b -> PR 89 https://github.com/scoizzle/Poly/pull/89 @ b756ebbd (100x; CI green, 2998 tests, goldens untouched). Final Boss one pass assigned 2026-10-03 23:55 -> merge under V11.
T2 PR 87: Razor NOT SHIP @ 07c2a28d 00:09 (OpenCode; F1 hand-kept sample list 10 vs 12 probes, F2 second create records previous state, F3 broad catch masks harness faults, F4 message/exception compares unprotected; F5-F9 suggestions/nit; review/razor-pr87-07c2a28d @ 1a201b20). 100x fix F1-F4 (+F6) assigned 00:10 -> Final Boss (not Razor re-verify per Razor note).
A2b PR 89: Final Boss NOT SHIP @ b756ebbd 00:15 (1 bug F132 descriptor keeps caller list, forbidden edge slips past Register; gate YES; F133-F140). 100x fix assigned 00:16 -> Final Boss re-verify.
T2 PR 87: 100x fix pushed 627b174f 00:20 (master merged in 6b54829f; CI green, 3020 tests, goldens untouched; Razor F1-F6 closed, F7 partial (helper 206 lines/2 files), F8/F9 skipped). Final Boss second pass on OpenCode assigned 00:21. Note for C8d: add require-message mapping (MapModuleRequireFailure) to C8d Done-when (100x suggestion).
A2b PR 89: 100x fix pushed 7372c542 00:22 (F132-F138 closed, F139/F140 skipped; CI green, 3011 tests, goldens untouched) -> Final Boss re-verify assigned 00:23 -> merge under V11.
A2b PR 89: Final Boss NOT SHIP @ 7372c542 00:56 (0 bugs; gate NO on F150 only: DeclareType summary/test name false about dangling vs WrongType; F151-F157). 100x comment+test fix (F150, F151, F152) assigned 00:57 -> Foreman verifies diff comment/test-only, merges under V11. PR 87 T2 still with Final Boss.
A2b MERGED: PR 89 squash 16403b60 2026-10-04 01:04 (Final Boss 0 bugs @ 7372c542; final tip 4c5fc375 = 3 doc lines + tests, verified by Foreman; V11). Open nits F153-F157. K1 (Lane A, docs) -> 100x assigned 01:05 (+F127, merged SHAs). T2 PR 87 with Final Boss. Queue: H1, N3.
K1 -> PR 90 https://github.com/scoizzle/Poly/pull/90 @ 6602517a (100x; docs only, CI green; also fixed A2b Files line + section 5 sentence). Final Boss one pass assigned 2026-10-04 01:07 (queued after PR 87) -> merge under V11.
T2 PR 87: MERGE-READY -> Scot hand-merge via Chieftan. Final Boss SHIP @ 627b174f 01:23 (gate YES, 0 bugs, 3020/3020, 22 mutations 2 survive F141/F147, merged w/ master 16403b60 clean 3040/3040; two simulate-vs-print gaps pinned as rows: C2b range violation, C8d require message; F141 suggestion, F142-F148 nits). Chieftan pinged 01:24, also asked HP1 (wave 1) release. Lane B idle after T2 until HP1.
T2 MERGED: PR 87 squash afa246c2 (Scot approved via Chieftan 01:25). HP1 released. C4a (Lane B) -> 100x assigned 01:28 -> Final Boss one pass -> Scot hand-merge (not on V11 list).
C4a -> PR 91 https://github.com/scoizzle/Poly/pull/91 @ a8684962 (100x; CI green, 3046 tests, goldens untouched; known-gap rows: entry-assigned equals -> C2b, action-assign equality -> C4c). Final Boss one pass assigned 2026-10-04 01:39 (PR 90 K1 also queued with him) -> Scot hand-merge via Chieftan.
K1 PR 90: Final Boss NOT SHIP @ 6602517a 01:45 (1 bug F160 stage-map:150 section 7 difference 2 contradicts 2.7/decisions 5,6/C9; gate NO; F161-F167). 100x docs fix assigned 01:46 -> Final Boss short re-check -> merge under V11. PR 91 C4a queued with Final Boss.
K1 PR 90: 100x fix pushed f3f0dd36 01:48 (F160-F167 closed, docs only, CI green) -> Final Boss short re-check assigned 01:49 -> merge under V11; mark K1 done in plan after merge (next docs touch).


- 2026-10-04 02:20 K1 PR 90 merged 7fb4b2a2 under V11. H1 assigned to 100x (Lane A).

- 2026-10-04 02:15 PR 91 C4a SHIP @a8684962; Chieftan pinged for hand-merge. Follow-ups F170-F175 (F174: add equality-on-assign note to C4c card).

- 2026-10-04 02:25 H1 PR 92 @b657dbfd to Final Boss. N3 assigned to 100x.

- 2026-10-04 02:32 N3 PR 93 @a6981648 queued for Final Boss after PR 92.

- 2026-10-04 02:46 PR 92 H1 NOT SHIP #1 (gate NO, 0 bugs, comments only). Fix list sent to 100x.

- 2026-10-04 02:52 PR 92 fixup 9f46235e routed to Final Boss for re-check.

- 2026-10-04 02:55 PR 93 N3 NOT SHIP #1 (gate NO, 0 bugs; real breaks 45 not 49). Fix list sent to 100x.

- 2026-10-04 03:00 PR 93 fixup 0d4782c2 routed to Final Boss.

- 2026-10-04 03:05 H1 PR 92 merged 47220d4f under V11 (head 9f46235e, CI green).

- 2026-10-04 03:15 PR 93 N3 NOT SHIP #2 (gate NO, 0 bugs). Fix list sent to 100x incl. plan H1->done 47220d4f.

- 2026-10-04 03:21 PR 93 fixup f4e6b9a2 routed to Final Boss.
- 2026-10-04 11:40 Chieftan audit actioned: PR 93 re-fired, C0 assigned, board pruned.
- 2026-10-04 11:43 C0 PR 94 @82894b63 routed to Final Boss.
- 2026-10-04 12:05 N3 PR 93 merged 6dbab628 under V11 (head f75c3e84 after mechanical update-branch, CI green). A3a assigned to 100x. Plan status fixes folded into PR 94.
- 2026-10-04 11:56 PR 94 tip 1a82868f (merge + docs) routed to Final Boss.
- 2026-10-04 12:02 A3a PR 95 @a06e0ff9 filed; Razor assigned; golden ruling recorded.
- 2026-10-04 12:18 PR 95 A3a Razor NOT SHIP #1; fix list to 100x; F1 decision asked via Chieftan.

Parked (added 2026-10-04 12:20): dead code RunTransitionEffect and two-arg EvaluateDefaultValue (Final Boss F245, later dead-code card). Culture bug: 5 tests fail under Asia/Tokyo+de_DE and LA+tr_TR on base and head (decimal parsing under comma-decimal culture: Export_RangeNegativeAndFractionalBounds_Parse, OpenRange_VerifiedEnvelope_KeepsBoundOpen, DecimalNumbers_Addition_ReturnsDouble, MixedTypes_ComplexExpression_PromotesCorrectly, MixedTypes_IntAndDouble_PromotesToDouble); needs its own card (tell Chieftan at next digest).
- 2026-10-04 12:22 PR 94 C0 NOT SHIP #1; fix list sent to 100x.
- 2026-10-04 12:27 PR 94 fixup 972b3de1 routed to Final Boss.
- 2026-10-04 12:36 PR 95 F2-F7 @bd970b73; F1 go given to 100x; Razor re-check after F1 push.
- 2026-10-04 12:43 PR 95 tip 1e31f6fa routed to Razor for re-check.
- 2026-10-04 12:56 PR 95 Razor SHIP @1e31f6fa; routed to Final Boss. PR 94 C0 Final Boss SHIP @972b3de1, merge BLOCKED by auto-review, awaiting Scot approval (asked 12:50).
- 2026-10-04 13:27 Scot confirmed option (a) for PR 95 F1 (via Chieftan); already implemented in b9e6f8c1. PR 94 merge approval still pending from Scot.
- 2026-10-04 16:55 C0 PR 94 merged ab0772cd. PR 95 Final Boss re-fired. K0 assigned.
- 2026-10-04 17:00 K0 PR 96 @61b80ac9 queued for Final Boss.
- 2026-10-04 17:28 PR 95 A3a Final Boss NOT SHIP #1 (docs-only gate). Fix list to 100x. K0 PR 96 next in Final Boss queue.
- 2026-10-04 17:35 PR 95 fixup 0d84edd1 routed to Final Boss ahead of PR 96.
- 2026-10-04 17:48 PR 95 Final Boss SHIP @0d84edd1 with waiver item F265 (unreachable throw); chose delete over waive: 100x doing a tiny fixup (F265, F280, F282), then 2-minute re-check, then Chieftan/Scot hand-merge. Later docs card: F281 stale line numbers plan 196/727, F283 N4 text 'Emit splits files by type name', F263 decision (MCP add accepts names DSL refuses), plan line 3 A3a + merge SHA after hand-merge.
- 2026-10-04 17:50 PR 96 K0 SHIP @61b80ac9 -> Chieftan hand-merge. Next Lane B: B1 (after 100x pushes PR 95 fixup); T1 after PR 95 lands.
- 2026-10-04 17:56 PR 95 e747c15f routed to Final Boss; B1 assigned to 100x (Lane B, from ab0772cd).
- 2026-10-04 18:05 PR 95 A3a Final Boss SHIP @e747c15f -> Chieftan hand-merge (with PR 96). Open: F263 Scot question; plan nits F281/F283 + line 3 A3a/K0 after merges.
- 2026-10-04 18:10 B1 PR 97 @183f17af filed (real bug: printed this.Notify after transition in when-handler/first-stage entry; exporter fix; +6 tests, CI green). With Final Boss. Parked pre-existing: exit-transition stack overflow in exporter; stage-only entity duplicate ctor CS0111 (need cards). KnownGap rows: subscriber cascade -> C5b, first-stage entry transition -> C2b.

- 2026-10-04 22:05 K0 2b3bdd64 + A3a 89098229 merged by Scot. PR 97 re-fired. T1 and N5 assigned to 100x. Plan line 3 still missing K0, A3a, B1 (fold into N5 or T1 docs commit).
- 2026-10-04 22:25 PR 98 T1 leftovers (10), carried per Chieftan recs (Scot gave no ruling): 1 WrongSource_FailsLoud + 2 CreateEntityInRelationship_WrongSource: wait for G1 (analyzer says unknown relationship vs runtime 'not the source'); 3 OnEntryEffect_Throws_StageStillSet: test redesign (needs another throw source); 4 EvaluatePolicy_PathPrefix_MultipleLinkedTargets_Throws: wait for G1; 5 EvaluatePolicy_SelfRelationship_OutboundOnly: known gap (self-relationship quantifiers unsupported); 6 InvokeAction_StringConcat: N6; 7 ParityTests.Create_WhenDefaultViolatesEquality: wait for G1; 8-10 OracleToolTests OracleExpression_And_Works/_AgeGte_*: G3 (domain built in OracleTool.cs:399-412).
- 2026-10-04 22:45 Candidate cards (not yet assigned, WIP cap 2): N7 contract/contract_value_type/contract_endpoint/contract_binding `add` returns Success=false though stored; no-op guard GetFingerprint ignores contracts (Lane A, S, Review 1, after N6). B1b (F290) session.Emit prints OnEntry{Stage}/OnExit{Stage} with this.Notify for nested-if transition = CS1061 (RuntimeAnalysisCache.cs:322); one-line EmitInstanceNotify:false + test (Lane B, XS). F291 flip/remove the true default of EmitInstanceNotify (fold into B1b). F292 exporter exit-transition stack overflow (abort exit 134; nested-if in non-first-stage exit); F293 duplicate parameterless ctor CS0111 (stage-only entity, also Text property + first-stage entry{assign}); repro DSL in docs/agent/reviews/2026-10-04-pr97-183f17af-final-boss-followups.md on branch review/finalboss-pr97-183f17af. F294 first-stage-entry gap owner = C2b; fix test comment (simulate skips only top-level entry transitions). F295 PR 97 body counts (6 tests, three contexts). Plan Done-so-far needs K0, A3a, B1 (+SHA after merge), T1, N5.
- 2026-10-04 22:50 G1 card note: flip KnownGap_TextWithNumber rows (PR 100) to 'both refuse'; they fail under the probe on purpose until G1.
- 2026-10-04 23:08 B1 PR 97 squash-merged 076a9014 (Scot via Chieftan). KnownGap owners: subscriber cascade C5b; first-stage entry transition C2b (closest, no firm owner).
- 2026-10-04 23:17 Cards from PR 99 review: F304 DSL printer NeedsQuotes twin rule (contract source 'default' / version '1' exported unquoted, re-apply fails) - candidate slice, Lane A, S; F305 = N7 (contract add Success=false, in progress); F308 N5 card missing from plan (docs commit).
- 2026-10-04 23:55 B1c (post-98): delete EmitInstanceNotify, this.Notify branch in EffectLoweringPass.StageTransition, the 4 explicit false args; update the 2 StageTransitionHostAbiTests. Lane B, XS, Review 1.
- 2026-10-05 00:10 DMEFF012 claimed by both PR 101 and PR 103; second merge renumbers. Razor stayed on 100/102 (no Warden/Sentinel). C1a+A4 to 100x after push.
- 2026-10-05 00:09 F325: write the 10 T1 leftovers and the real T1 file list (12, incl. ParityTests) into the plan (docs commit).
- 2026-10-05 00:33 Later touch-up (docs/tests, not blocking): F330 ValidDomain doc clause on invalid relationships (DomainTestFactory throws when source entity missing); F331 'primitives a parsed domain starts with' assumes uses line; F325 write 10 waived T1 leftovers + owners into the T1 plan card.
- 2026-10-05 07:12 Scot addendum on F292: stages gate later transitions; guarded automatic transitions valid; Analyze rejects only unconditional cycles; runtime loop guard fail-loud in interpreter and printed C#.
- 2026-10-05 07:20 Scot: 24h standing merge through 2026-10-06 07:20 CDT. Chieftan squash-merges FB SHIP+gate YES. Foreman pings Chieftan only SHIP+SHA or hard blockers; board status after each merge.
- 2026-10-05 07:28 Cards from PR 99 FB: F340 MCP property Date:Line vs DSL relationship-name collision (with F264); F341 MCP vs parser primitive-name sources; F342-F344 nits; F304 NeedsQuotes twin; F308 N5 plan card.
- 2026-10-05 07:36 F351 dead no-op guard / old revision after commit; F352 dup stage/property throw vs dup contracts stored — candidate slices after N7.
- 2026-10-05 07:45 Candidate: M1b — EffectAnalyzer scan whole target for code-built OwnedAccess→RelationshipNavigation (Razor F1 on PR 103).

- 2026-10-06 06:12 Scot (via Chieftan): PR 113 post-merge review is top priority (Razor, table format; 100x lands fixes as one PR → FB; PR 110 rebases after). All Poly plans live in the repo: board and task files moved to `docs/plans/poly-eng/`; every bot updates its slice task file with each push, review verdict and merge (see [README](README.md)).
