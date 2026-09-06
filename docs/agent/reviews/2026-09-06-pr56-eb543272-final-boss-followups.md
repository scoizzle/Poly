# Follow-ups — PR 56 Type-create emit F5/F6 (Final Boss re-verify) — 2026-09-06

- Source review: `docs/agent/reviews/2026-09-06-pr56-eb543272-final-boss.md`
- Target: PR 56 SHA `eb543272c154d882b6d99daf1881a20f663114e9` vs `origin/master`
- Mode: re-verify of Razor `docs/agent/reviews/2026-09-06-pr56-eb543272-razor-followups.md`
- Model: grok-4.6
- Verdict: ship

## Open bugs (must close before ship)

None.

## Suggestions

- [ ] **F7** — `EffectLoweringPass.cs:668-674`. Do: emit reverse nav when HostAbi would `TryLinkCreateInBackReference` (`HostAbi.cs:658-659`, `:667-677`), or narrow `poly-dsl-guide.md:73-77` to collection-Add-only (runtime reverse stays HostAbi/CreateNav). Add an export oracle that generated Type-create sets or documents reverse. Optional sibling: `Register*Subscriber` that `AddCreateNavMethod` emits (`DomainToCSharpExporter.Notify.cs:231-244`) is also absent on Type-create Add.

- [ ] **F8** — `DomainToCSharpExporterTests.cs:1985-1987`. Do: pin `Patron.AssessByType` (method body / occurrence count) so `_fines.Add` is not satisfied by `CreateFines` alone (`Notify.cs:219-223`). Add export oracle for F6 emit sibling: two many-navs, AssessByType does not `Add` to either collection.

## Nits

None.

## Process

Razor closed F5 citing `Contains("_fines.Add")` without checking the CreateNav factory sibling that already emits that substring. Protocol §3.2a / §3.4: dual-path oracles must force the edited sibling, not a pre-existing factory. Tighten future emit tests to the action method, not the whole compilation unit.

## Disposition of prior items (Razor @ eb543272)

- **F5** — emit Type-create `_collection.Add` + export test + guide — **fixed** in production (`EffectLoweringPass.cs:660-675`, wrap `Actions.cs:268-276`, guide `:73-79`). Export test exists (`DomainToCSharpExporterTests.cs:1966-1988`) but is not a unique AssessByType oracle (F8).

- **F6** — ambiguous multi many-rel MCP simulate test — **fixed**. `SimulateCreateDogfoodTests.TypeOnly_AmbiguousManyRel_ListsButDoesNotLink` (`:182-227`) matches HostAbi `outs.Count != 1` (`HostAbi.cs:656-657`).

- **N1** — `StrayRepoRootProbesPoly_MustBeEmpty` rename — **fixed**. `ProbePlacementGateTests.cs:27-57`.

- **F7** — reverse-nav emit parity — **still open** (suggestion). Independently confirmed; not a ship blocker.

## Freeze

Filed for ship. Never implement from this review. Never merge. Never force-push product.
