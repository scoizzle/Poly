# Follow-ups — PR 52 Fine Type-create / Type+Rel (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr52-d07aabf3-final-boss.md`
- Target: PR 52 SHA `d07aabf3a99720a52f29f9448f09291f799500fe` vs `origin/master`
- Mode: re-verify of Warden `docs/agent/reviews/2026-09-06-pr52-d07aabf3-warden-followups.md`
- Model: grok-4.6

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F5** — Close or explicitly bound the C# emit sibling of Type-create auto-link. File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:610-624`. Do: either lower unambiguous `create Type` to the same CreateNav/Add path as `create in Rel` (`DomainToCSharpExporter.Notify.cs:219-224`), or amend `poly-dsl-guide.md:73-79` to say export Type-create remains `Type.Create` with no collection wire. Add an export test on `docs/probes/dogfood/simulate-create-type.poly` that the generated `AssessByType` does or does not `Add` to Fines. Runtime HostAbi already links (`DomainEntityInstance.HostAbi.cs:606-612`).

- [ ] **F6** — Force the documented several-match Type-create path. File: `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:656-657`. Do: domain with two many-navs to Fine (`fines` and `waived`); `create Fine`; assert list Fine=1, `HasFines` false, both collection link counts 0. Nested leftover: ambiguous multi many-rel Type-create still unlinked. `CreateEntityInstance_WithoutRelationship_NotLinked` (`DomainEntityInstanceTests.cs:2036-2056`) is zero-nav, not several.

## Nits

- [ ] **N1** — Rename or tighten `CommittedPolyProbes_AreUnderDocsProbesOrArchive`. File: `Poly.Tests/DomainModeling/ProbePlacementGateTests.cs:53-54`. Do: the body only flags `probes/`; `RepoRoot_ProbesDirectory_MustNotExist` already owns that recurrence. `demo/live/*.poly` and `docs/plans/archive/experiments/examples/*.poly` exist at this SHA and cannot fail the scan.

## Process

None new. F4's cheap placement gate landed. Dual-path create grew (runtime auto-link vs emit `Fine.Create`); if that class recurs, CORE 3.4 “do not grow dual-path” needs a review checklist item, not only a HostAbi patch. GitHub PR 52 body still cites `probes/dogfood/` (stale).

## Disposition of prior items (d07aabf3 Warden)

- **F1** — Place fixtures on `docs/probes/` and enroll consumers — **fixed**. `docs/probes/dogfood/simulate-create-*.poly` exist; compile-oracle Arguments at `DslCompilerCompileOracleTests.cs:168-170` (9 vs origin/master 6); README `docs/probes/README.md:12`; `git ls-tree HEAD` has no `probes/` paths.

- **F2** — MCP simulate test for list/policy/links including combined Type then Rel — **fixed**. `SimulateCreateDogfoodTests.cs:97-180`. `--list-tests` names Type / Rel / Combined; included in 2632-pass suite run on this worktree.

- **F3** — Stop claiming a product close without HostAbi/guide, or land the change — **fixed**. `HostAbi.cs:606-612` + `:649-660`; guide `poly-dsl-guide.md:73-79`. GitHub PR body still cites `probes/dogfood/` (stale metadata, not an open product gap).

- **F4** — Gate live-probe placement — **fixed** for the named recurrence (repo-root `probes/`). `ProbePlacementGateTests.cs:19-24`. Scan test name overclaims (N1).

- **F5** — C# emit Type-create Fine.Create with no collection Add — **still open** (suggestion). `EffectLoweringPass.cs:610-624` vs `DomainToCSharpExporter.Notify.cs:219-224`. Not a ship-blocker; no new bug found.

- **F6** — Untested several-match Type-create — **still open** (suggestion). `HostAbi.cs:656-657`. Not a ship-blocker; no new bug found.

- **N1** — Scan test name overclaim — **still open** (nit).
