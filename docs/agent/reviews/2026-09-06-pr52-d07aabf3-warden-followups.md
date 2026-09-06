# Follow-ups — PR 52 Fine Type-create / Type+Rel (Warden re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr52-d07aabf3-warden.md`
- Target: PR 52 SHA `d07aabf3a99720a52f29f9448f09291f799500fe` vs `origin/master`
- Mode: re-verify of `docs/agent/reviews/2026-09-06-pr52-5299b98b-warden-followups.md`

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F5** — Close or explicitly bound the C# emit sibling of Type-create auto-link. File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:610-624`. Do: either lower unambiguous `create Type` to the same CreateNav/Add path as `create in Rel` (`DomainToCSharpExporter.Notify.cs:219-224`), or amend `poly-dsl-guide.md:73-79` to say export Type-create remains `Type.Create` with no collection wire. Add an export test on `docs/probes/dogfood/simulate-create-type.poly` that the generated `AssessByType` does or does not `Add` to Fines. Runtime HostAbi already links (`DomainEntityInstance.HostAbi.cs:606-612`).

- [ ] **F6** — Force the documented several-match Type-create path. File: `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:656-657`. Do: domain with two many-navs to Fine (`fines` and `waived`); `create Fine`; assert list Fine=1, `HasFines` false, both collection link counts 0. Nested leftover: ambiguous multi many-rel Type-create still unlinked. `CreateEntityInstance_WithoutRelationship_NotLinked` is zero-nav, not several.

## Nits

- [ ] **N1** — Rename or tighten `CommittedPolyProbes_AreUnderDocsProbesOrArchive`. File: `Poly.Tests/DomainModeling/ProbePlacementGateTests.cs:53-54`. Do: the body only flags `probes/`; `RepoRoot_ProbesDirectory_MustNotExist` already owns that recurrence.

## Process

None new. F4's cheap placement gate landed. Dual-path create grew (runtime vs emit); if that class recurs, CORE 3.4 “do not grow dual-path” needs a review checklist item, not only a HostAbi patch.

## Disposition of prior items (5299b98b Warden)

- **F1** — Place fixtures on `docs/probes/` and enroll consumers — **fixed**. `docs/probes/dogfood/simulate-create-*.poly` exist; compile-oracle Arguments at `DslCompilerCompileOracleTests.cs:168-170`; README `docs/probes/README.md:12`; `git ls-tree HEAD` has no `probes/` paths.

- **F2** — MCP simulate test for list/policy/links including combined Type then Rel — **fixed** (expected values follow the F3 product change, not the 5299b98b Type-unlinked comment). `SimulateCreateDogfoodTests.cs:97-180`. Local filtered run 3/3 passed.

- **F3** — Stop claiming a product close without HostAbi/guide, or land the change — **fixed**. `HostAbi.cs:606-612` + `:649-660`; guide `poly-dsl-guide.md:73-79`. GitHub PR body still cites `probes/dogfood/` (stale metadata, not an open product gap).

- **F4** — Gate live-probe placement — **fixed** for the named recurrence (repo-root `probes/`). `ProbePlacementGateTests.cs:19-24`. Scan test name overclaims (N1).
