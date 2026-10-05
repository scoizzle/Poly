# Final Boss review: PR 98 (slice T1), tip 1b79fdb7

- Reviewer: Final Boss (one pass, after Razor SHIP at the same SHA). Filed 2026-10-05 ~00:15 CDT.
- PR: https://github.com/scoizzle/Poly/pull/98, implementer 100x (hand, no mill).
- SHA reviewed: `1b79fdb78a6fc3ba0de186e0e16f71936d1812cc` (verified with `gh pr view 98` and `pull/98/head`; equals Foreman's SHA; one commit on `89098229`). PR is BEHIND.
- master at review time: `076a9014` (B1 #97 merged after the PR branched). `git merge-tree --write-tree origin/master 1b79fdb7` = **clean** (tree `8f607091`); a real merge in a scratch worktree auto-merged `ParityTests.cs` with no conflict.
- Card: T1 in `docs/domain-modeling/pipeline-convergence-plan.md` (~line 78). Foreman scope ruling: the 10 residual probe failures are waived and carried.
- Razor: SHIP, 0 bugs, `docs/agent/reviews/2026-10-04-pr98-1b79fdb7-razor.md` (branch `review/razor-pr98-1b79fdb7`). Read in full; everything below was re-verified independently.

## Verdict: NOT SHIP. Hand-editability gate: NO. Bugs: 0.

The work itself is sound. The change is tests only, no test is weakened or deleted, the fixture comes from the canonical catalogs, and the probe goes from 115 to exactly the 10 waived tests. One false sentence blocks it: the `ValidDomain` class doc says the helper returns a domain "that analyzes cleanly". Callers in this PR build domains through it that are invalid on purpose and assert the errors (F320). Under Scot's standing gate, a false sentence in a comment is gate NO. The fix is a doc rewrite in `Poly.Tests/TestHelpers/ValidDomain.cs` plus one new small test file, so **none of the 11 frozen test files needs to change**. See the fix list.

## Checks

### Diff scope vs card; PR body vs diff
- 12 files, all under `Poly.Tests/`: new `TestHelpers/ValidDomain.cs` (27 lines) and 11 test files (+146/-127). No product file, no plan edit, no golden.
- I ran the PR body's re-runnable sed/perl command on `89098229` myself and diffed the result against `1b79fdb7`. They are byte-identical except the six hand edits the body lists (CreateWithDomain `extensions:`, `InvokeAction_AssignTodayClockNode_StoresDate`, `InvokeAction_StageTransition_RunsExitThenEntryEffects`, `ParityTests.EqualityScenario`, the two Date tests, `someRel`). Every changed line is a construction line. No assertion, Skip, or try/catch is changed.
- `ParityTests.EqualityScenario` (`:199`): the input is `.Single()` entity and all three callers use single-entity DSL, so swapping only the entity and keeping the parsed primitives is correct.
- Card vs PR: the card lists OracleToolTests among "11 files", but the oracle domain is product code (`OracleTool.cs` ~399-412) and G3's job. The PR touched ParityTests instead, which is the right call; the plan text is stale (Razor F7, carried).

### The 10 waived failures (head + `/tmp/t1-probe.patch`, never committed)
Head + probe: total 3073, **10 failed**, and they are exactly the named set. Nothing else is waived silently. Each one passes in the normal head run (3073/0), so each failure is the probe throwing on `HasErrors`. Captured analysis errors:

| # | Test | Error under probe (captured) | Owner (Foreman ruling) | Matches PR body |
|---|---|---|---|---|
| 1 | `CreateEntityInstance_WithRelationshipName_WrongSource_FailsLoud` | "CreateEntityInstance references unknown relationship 'rel' in domain 'Test'" | G1 | yes |
| 2 | `CreateEntityInRelationship_WrongSource_FailsLoud` | "CreateIn effect references unknown relationship 'rel' in domain 'Test'" | G1 | yes |
| 3 | `OnEntryEffect_Throws_StageStillSet_NotifyStillFires` | "Assign effect targets property 'Nonexistent' which does not exist on entity 'A'" | test redesign | yes |
| 4 | `EvaluatePolicy_PathPrefix_MultipleLinkedTargets_Throws` | "Path-prefix expression 'items' ... Bare path-prefix on a 'many' relationship is invalid" | G1 | yes |
| 5 | `EvaluatePolicy_SelfRelationship_OutboundOnly_TargetSeesNoReports` | "Quantifier on self-relationship 'reports' is not supported yet" | known gap | yes |
| 6 | `InvokeAction_StringConcat_Assign_Concatenates` | "arithmetic operand is not numeric (got 'Text' and 'Text')" | N6 (Scot 2026-10-04: Text + Text is valid concatenation, so the analyzer is wrong, not the test) | yes |
| 7 | `ParityTests.Create_WhenDefaultViolatesEquality_FailsWithTheSameMessage` | "Property 'Code' default value 'Active' violates == Other" | G1 | yes |
| 8 | `OracleToolTests.OracleExpression_And_Works` | "Property 'Age' references unknown type 'Number'; Property 'Active' references unknown type 'Boolean'" (oracle's own domain) | G3 | yes |
| 9-10 | `OracleExpression_AgeGte_PassesForAdult` / `_FailsForMinor` | fail the same way ("Expected to be true"); same `OracleTool` code path; message not captured (box overload) | G3 | yes |

None of them hides a bug introduced by this PR. The PR does not touch OracleToolTests, the product code, or tests 4-7's domains beyond the construction line. Owners appear in the PR body and in Foreman's ruling, **not** in the test code or the plan, so they need to be carried into the plan (followups).

### Tests not vacuous, none weakened; mutations
- `[Test]`/`[Arguments]` counts are unchanged per file (Razor's table; my rerun diff agrees). No test added or removed (3073 before and after on the base).
- Invalid-by-design tests stay invalid: `RequireCatalog_Returns_WhenErrorsWithoutCatalog` (type "Nope"), `CreateEntityInstance_UnknownRelationship_FailsLoud`, `SubscriptionAnalysisTests.Analyze_UnknownRelationshipName_ReportsDMSS003` / `Analyze_UnknownTargetStageName_ReportsDMSS003`. Generic `HasErrors` assertions that used to be trivially true now assert real errors, so they got sharper.
- Razor's mutation logs (`/workspace/pr98-mut-*.log`, read, not rerun): dropping `Text` from the fixture leaves DomainEntityInstanceTests green in the normal run (128/128) but 60/128 red under the probe; renaming `someRel` turns its test red under the probe; removing the temporal import passes in the normal run and goes red under the probe. So the fixture only matters under the uncommitted probe, which is Razor F1, and I agree (F321).
- Razor F5 / Foreman's "F3", the `someRel` NoStore test: name `CreateEntityInstance_RelationshipNameWithoutStore_NoOp` and comment "Create with RelationshipName but no store -> no crash, no link" are **still true**. `LinkRelated` returns on `Store is null` before it resolves the relationship (`DomainEntityInstance.HostAbi.cs:50-53`), so the declared and undeclared names take the identical code path. Not a gate issue (F324, nit).

### Counts (box load 190-290 from other agents; several full runs were OOM-killed)
| Run | Result |
|---|---|
| head 1b79fdb7, UTC, full | 3073 total, 0 failed |
| head + probe, full | 3073 total, 10 failed (the waived set) |
| merged (076a9014 + PR), UTC, full | 3079 total, 0 failed (master 076a9014 = 3079: the PR adds no tests) |
| master 076a9014 full | OOM-killed (exit 137) under box load, not rerun. Its count is 3079 from the PR 97 merged-tree run, and the merged tree's 3079/0 covers it |
| head, 11 affected classes (326 tests): UTC / invariant | 0 / 0 failed |
| same, Tokyo+de_DE / LA+tr_TR | 1 / 1 failed: `Export_RangeNegativeAndFractionalBounds_Parse` (one of the 5 known comma-decimal failures; lives in DomainToCSharpExporterTests). No new culture failure. T1 resolves none of the 5 (it does not touch their files or code). |
| `POLY_UPDATE_GOLDEN=1` (EmitGoldenTests, 21) | passed, `git status` clean; `git diff --stat 89098229 1b79fdb7 -- '*.golden'` empty |
| CI on 1b79fdb7 | Build & test SUCCESS |

### Hand-editability gate: NO (F320)
`Poly.Tests/TestHelpers/ValidDomain.cs:7`: "Hand-built `Domain` that analyzes cleanly." This is false for callers in this same PR: `DomainEntityInstanceTests.cs:451` (`RequireCatalog_Returns_WhenErrorsWithoutCatalog`, type "Nope", asserts `HasErrors`), `:2678` (`CreateEntityInstance_UnknownRelationship_FailsLoud`), `SubscriptionAnalysisTests.cs:80/:96` (Analyze_Unknown*, assert DMSS003). The helper declares primitives; it does not make a domain valid. A maintainer who reads the doc cold would believe the opposite. `:9` "a domain built in code has none" is also imprecise: `DomainFactory.Create` builds in code and seeds nine primitives plus temporal (F323, nit). The rest is true: "from the same sources the DSL loader uses" holds, because canonical definitions plus Core `PrimitiveSeeds` with the same `Contains` skip is exactly what `PolyDslParser.IndexPrimitiveNames` does (`:150-158`). No dead guards or twin paths.

### Principles / decisions
Tests only; nothing contradicts decisions 1-20, V1-V11, or HELD. Fail-closed is unaffected in product code. The PR is the measured precondition for G1. "Text + Text is valid concatenation" (Scot, 2026-10-04): residual 6 is correctly an analyzer defect owned by N6, not a test to change.

## My call on Razor F1 and F2
- **Razor F1 (no committed test that ValidDomain analyzes clean): does not block on its own.** Until G1 lands, the fixture's whole effect is invisible to the committed suite (Razor's MA/MC logs). That is a test-honesty gap, but G1 (next in Lane A) closes it by construction. Since a fix pass is needed anyway for F320, include it: it is one new file and touches no frozen file (fix item 2).
- **Razor F2 (silent skip of an unknown extension id): does not block, and it is not a fail-open introduced by this PR.** The mill and I both checked the product: `AddDomainExtensionChange.ApplyTo` (`DomainChange.cs:~685`) records any extension id and adds primitives only `if (ExtensionCatalog.Core.Contains(id))`. `PolyDslParser.IndexPrimitiveNames` (`:154-158`) skips non-Core ids the same way, because pack ids (for example `sqlite`) resolve from a different catalog. Fail-closed lives in `DomainSession.ForExtensions/ForSource` (`Resolve` throws "Unknown domain extension"). So the fixture copies product semantics exactly, which is what its doc claims. Scratch repro: `ValidDomain.Create("T", [w], extensions: ["temporl"])` records `temporl`, adds only the five canonical primitives, and Analyze reports no error when no temporal type is used. With a `Date` property it reports "Property 'At' references unknown type 'Date'", which is loud, and G1 will turn it into a throw. `DomainSession.ForExtensions(["temporl"])` throws. The silent case is a typo of an extension the test does not use, which is harmless. Dropping the `Where` (Razor's suggestion) would be safe for today's callers, since all pass `TemporalId`. It would make the fixture stricter than the product, though, and reject pack ids. My call: document the behaviour in one sentence (fix item 1c), and don't change the code.

## Findings (from F320)

| id | severity | where | summary | blocks? |
|---|---|---|---|---|
| F320 | suggestion (gate NO) | `ValidDomain.cs:7` | Doc claims the helper yields a domain "that analyzes cleanly"; false for this PR's invalid-by-design callers (listed above). | **yes** (gate) |
| F321 | suggestion | `ValidDomain.cs` / new test | = Razor F1. No committed assertion that a ValidDomain domain analyzes without errors; the fixture is invisible until G1. | no; in the fix pass |
| F322 | nit | `ValidDomain.cs:19-20` | = Razor F2, reframed: non-Core id silently skipped, which mirrors `AddDomainExtensionChange.ApplyTo` / `PolyDslParser`. Not a PR-introduced fail-open. Document it. | no |
| F323 | nit | `ValidDomain.cs:9` | "a domain built in code has none" is imprecise (`DomainFactory.Create` seeds primitives, and its extensions default to temporal; Razor F6). | no; fold into the doc rewrite |
| F324 | nit | `DomainEntityInstanceTests.cs:2238` | = Razor F5 (Foreman's "F3"). The `someRel` declaration changes nothing on the no-store path (`LinkRelated` returns first). Name and comment stay true; the undeclared-name + no-store case is behaviourally identical. No action. | no |
| F325 | nit | plan T1 card | = Razor F7. The Files line names OracleToolTests among "the 11 files". The waived 10 and their owners exist only in the PR body and Foreman's ruling; record them in the plan at merge (Foreman docs). | no |
| — | nit | `ValidDomain.cs:23` | Razor F3 (`DistinctBy` does not dedupe caller primitives; loud "Duplicate member name"). Agree; optional doc line. | no |

## Mill (OpenCode `opencode-go/deepseek-v4.1-flash`, run `pr98-fb`, read-only, throwaway checkout `pr98fb-mill`; it stayed clean)
- Mill F0 "card gate unmet (10 probe failures)": **dropped**, waived by Foreman's binding ruling.
- Mill F1 "ValidDomain doc 'analyzes cleanly' false": **kept**, F320 (I reached it independently).
- Mill F2 "unknown extension skip": **kept as F322**, with its correction (mirrors `AddDomainExtensionChange` / `PolyDslParser`; fail-closed is in `DomainSession`). I verified both product sites.
- Mill F3 "'built in code has none' overbroad": **kept**, F323.
- Mill F4 "string-comparison test needlessly imports temporal": **dropped (false)**. `CreateDateComparisonEntity` has `Start`/`End` Date properties, so temporal is required.
- Mill F5 "EqualityScenario Select could silently keep a future primitive redefinition": **dropped**, speculative; input is `.Single()` entity, three single-entity callers.
- Mill also confirmed: no assertion changed; the `someRel` path is identical (`LinkRelated` returns on no store).

## Fix list for one 100x pass (only `Poly.Tests/TestHelpers/ValidDomain.cs` and one new file; no frozen test file)
1. **(required, F320 / F322 / F323)** Rewrite the `ValidDomain` class doc so every sentence is true, for example:
   a. "Declares the primitives a parsed domain starts with: the canonical built-ins (`CanonicalBuiltInTypeCatalog`) plus the primitive seeds of each core extension listed. A domain built with `new Domain(..)` or `DomainTestFactory` has none, so Analyze reports "unknown type 'Text'"."
   b. "It does not validate anything else: a test may pass invalid types or relationships on purpose, and the result then has analysis errors."
   c. "An extension id outside `ExtensionCatalog.Core` is recorded but adds no primitives, as `AddDomainExtensionChange` does. Unlike `DomainFactory.Create`, extensions default to none."
   (Optional: "Do not pass primitives this already declares; that is a duplicate-name error.")
   Keep the class name: a rename would touch the 11 frozen files, and the doc fix suffices.
2. **(recommended, same pass; F321 = Razor F1)** New `Poly.Tests/TestHelpers/ValidDomainTests.cs` (or under `Poly.Tests/DomainModeling/`):
   - an entity with one property of each canonical type (`Boolean`, `Number`, `Text`, `Uuid`, `Binary`) analyzes with `HasErrors == false`;
   - the same plus a `Date` property with `extensions: [ExtensionCatalog.TemporalId]` analyzes with no errors;
   - a `Date` property without the extension still reports "unknown type 'Date'".
   It must fail if the fixture drops a primitive (mutation: `.Where(p => p.Name != "Text")` turns it red).
3. Do not change the `Where` behaviour (F322). Update the PR body: note the doc fix; the fail count is unchanged.

Re-review scope after the pass: the ValidDomain.cs diff, the new test file, normal-run count 3073 + new tests, one probe run (still exactly the 10).

## Merge notes for Scot (after the fix pass; not V11)
- Rebase: PR is BEHIND (`89098229` to master `076a9014`, B1 #97 merged). `merge-tree` is clean; `ParityTests.cs` auto-merges (B1 added tests, T1 changed one construction line). Foreman's update-branch is mechanical. The merged tree passed 3079/0.
- Risk: low; tests only. The real risk is G1: it will turn these 10 waived tests red unless G1 lands their dispositions (1, 2, 4, 7 assert the gate message; 3 redesign; 5 known gap; 6 N6; 8-10 G3).
- After merge: full suite once; Foreman records T1 (merge SHA) in "Done so far", fixes the T1 card's Files line, and writes the 10 residuals with owners into the plan (G1 card for 1, 2, 4, 7; G3 for 8-10; N6 for 6; known-gap row for 5; a redesign item for 3). The 11 frozen test files unfreeze at merge.
