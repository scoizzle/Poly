# PR 52 — Fine Type-create orphan / Type+Rel (Final Boss) — 2026-09-06

- **Target**: PR 52 (`https://github.com/scoizzle/Poly/pull/52`, branch `cleanup/fine-orphan-type-rel`) / SHA `d07aabf3a99720a52f29f9448f09291f799500fe` vs `origin/master`
- **Mode**: re-verify (not rubber-stamp). Prior Warden at `docs/agent/reviews/2026-09-06-pr52-d07aabf3-warden.md` claimed ship, 0 bugs, F1–F4 fixed, F5/F6 suggestions only. Each claim re-checked against `git show d07aabf3:PATH` and current tree; no chain-trust of Warden quotes.
- **Model**: grok-4.6
- **SHA**: `d07aabf3a99720a52f29f9448f09291f799500fe` (`git rev-parse HEAD` matched; product HEAD not rewritten)
- **PINNED**: `/workspace/Poly-pr52-d07aabf3`
- **Issue counts**: 0 bugs, 2 suggestions, 1 nit
- **Verdict**: ship
- **Process notes**: Recurring fixture-off-tree class is gated (`RepoRoot_ProbesDirectory_MustNotExist`). Dual-path create grew: runtime Type-create auto-links when `Entity.Navigations` has exactly one many-rel to the created type; C# emit of the same IR is still `Type.Create` with no collection `Add`. CORE 3.4 (“do not grow dual-path”) was not updated in this diff. GitHub PR body still cites `probes/dogfood/simulate-create-*.poly` and “does not mix PR 51 product” — stale metadata, not a missing product change. Chieftan: do not block ship on F5/F6 unless a new bug; none found.

## Summary

PR 52 vs `origin/master` is two commits (`5299b98b` chore probes, `d07aabf3` enroll + HostAbi auto-link), **11 files, +485/−0**. Recomputed from this SHA: live fixtures are `docs/probes/dogfood/simulate-create-{type,in,create-in}.poly`; compile-oracle `Arguments` are **9** (origin/master **6**); `git ls-tree HEAD` has **zero** `probes/` paths; `SimulateCreateDogfoodTests` drives MCP `apply_dsl` → `create_instance` → `invoke_action` → list/policy/store links for Type-only, Rel-only, and Type-then-Rel; `TryAutoLinkUnambiguousOutbound` runs on Type-create when `RelationshipName` is null and the source owns exactly one many-nav; `ProbePlacementGateTests.RepoRoot_ProbesDirectory_MustNotExist` fails if repo-root `probes/` exists. Optional TUnit on this worktree: full `Poly.Tests` **2632 passed, 0 failed, 0 skipped** (intended `--treenode-filter` did not narrow; `--list-tests` includes the three F2 methods, three F4 methods, `CreateEntityInstance_WithoutRelationship_NotLinked`, and the three new compile-oracle Arguments). Dominant residual: C# emit of `create Fine` still `Fine.Create` with no collection `Add` (F5), and the documented several-match Type-create branch has no test (F6). No new contract bug on the MCP path F1–F4 named.

## F1–F4 disposition (primary evidence at d07aabf3)

| ID | Warden claim | Now | Evidence (this pass) |
|---|---|---|---|
| **F1** | fixtures under `docs/probes/dogfood/`; compile-oracle Arguments; no repo-root `probes/` | **fixed** | `git ls-tree -r --name-only HEAD` has **zero** paths matching `^probes/`. Files exist at `docs/probes/dogfood/simulate-create-type.poly:1`, `simulate-create-in.poly:1`, `simulate-create-create-in.poly:1`. Compile-oracle Arguments at `DslCompilerCompileOracleTests.cs:168-170` (HEAD 9 vs origin/master 6). README row `docs/probes/README.md:12`. Filesystem `probes/` absent in this worktree. |
| **F2** | `SimulateCreateDogfoodTests` Type / Rel / Type-then-Rel | **fixed** | `SimulateCreateDogfoodTests.cs:97-117` `TypeOnly_UnambiguousManyRel_ListsAndLinks`; `:119-137` `RelOnly_CreateIn_ListsAndLinksBothDirections`; `:139-180` `Combined_TypeThenRel_OnOnePatron_BothLinked`. Asserts `list_instances` Fine count (`:62-69`, `:109`/`:131`/`:152`/`:166`), policies via `PolicyTool.EvaluatePolicy` (`:82-95`) against MCP message `"Policy passed (true)."` / `"Policy failed (false)."` (`DomainTools.cs:1217`), `GetRelatedInstances("fines"\|"patron")`, `returnInstanceId` (`RuntimeTool.cs:740` `invokeActionResult`). Type-only expects `hasFines: true` / links=1 (product after F3). `--list-tests` names all three methods; they are in the 2632-pass run. |
| **F3** | HostAbi `TryAutoLinkUnambiguousOutbound` on Type-create when exactly one many-rel | **fixed** | `git show d07aabf3:Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs`: else-branch `:606-612` when `createEffect.RelationshipName is null` and `Store is not null`; `:649-660` `TryAutoLinkUnambiguousOutbound` (`outs.Count != 1` → return; else `Store.Link` + `TryLinkCreateInBackReference`). Guide `poly-dsl-guide.md:73-79` under §0.3 runtime store. `many` parses as `OneToMany` (`PolyDslParser.cs:1241-1243`), which the filter accepts. |
| **F4** | `ProbePlacementGateTests.RepoRoot_ProbesDirectory_MustNotExist` | **fixed** (named recurrence) | `ProbePlacementGateTests.cs:19-24` `RepoRoot_ProbesDirectory_MustNotExist` asserts `Directory.Exists(root/probes)` is false. `:61-70` also asserts the three simulate-create files live under `docs/probes/dogfood/`. The all-committed `*.poly` scan (`:27-57`) only records `rel.StartsWith("probes/")` (`:53-54`) — weaker than the test name (N1); the recurrence F4 named is the directory test. |

**Nested leftover (not F1–F4):** ambiguous multi many-rel Type-create still unlinked — **still true**. `HostAbi.cs:656-657` `if (outs.Count != 1) return;`; guide `:76-78`. No test constructs two many-navs to the same type. IR `CreateEntityInstance_WithoutRelationship_NotLinked` (`DomainEntityInstanceTests.cs:2036-2056`) is **zero** navs (`Parent`/`Child` with empty `Navigations`, `DomainTestFactory.Create(..., [])`), not several.

## Issues

### Issue 1 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:610-624`
- Description: Runtime Type-create now auto-links when the source owns exactly one many-rel (`HostAbi.cs:606-612` → `:649-660`). C# emit of the same `create Fine` still lowers to `Fine.Create(...)` (`CreateEntityInstance` when `_lowerStageTransitions` is true) with no `CreateFines` / `_fines.Add`. Create-in emit does add (`DomainToCSharpExporter.Notify.cs:219-224`). Guide §0.3 is runtime-store scoped (`poly-dsl-guide.md:67-79`), so this is not a lying MCP invariant — but CORE 3.4 says residual create dual-path is debt and must not grow. Before this SHA both paths left Type-create unlinked (runtime registered, emit discarded local); after, MCP list/policy agree and export still discards the Fine local (same class as archived `docs/plans/archive/probes-2026-08/findings/fleet-eval/12-mcp.md` Fine-discard). Compile-oracle 0/0 on the new probes cannot see the skip. No new throw. Reachability: valid `session.Emit` of `simulate-create-type.poly` and `docs/probes/fleet-eval/12-mcp/mcp-library.poly` subscription `create Fine` (`mcp-library.poly:10-12`).
- Suggestion: Either emit the unambiguous CreateNav/Add for Type-create, or state in §0.3 that export Type-create remains a discarded `Type.Create` local. Add an export test that generated `AssessByType` does or does not `Add` to Fines.
- Status: open

### Issue 2 -- Severity: suggestion
- File: `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:656-657`
- Description: Invariant-stating comment (`:607-610`, `:644-647`) and guide (`:76-78`): zero or several matching many-rels leave the child registered but unlinked (no silent pick). Tests force only `outs.Count == 1` (`SimulateCreateDogfoodTests` Patron.fines). Several-match (e.g. `fines: many Fine` and `waived: many Fine` plus `create Fine`) is unforced. A `Count >= 1` silent-pick would still green the new suite. Zero-nav IR test (`CreateEntityInstance_WithoutRelationship_NotLinked:2036-2056`) is a different sibling (no navs at all). Comment at `:2037` (“without RelationshipName → child is NOT linked”) is now over-broad as a general invariant; the body still matches zero-nav.
- Suggestion: MCP or IR test: source owns two many-navs to Fine; Type-create; list Fine=1; `HasFines` false; both collection link counts 0.
- Status: open

### Issue 3 -- Severity: nit
- File: `Poly.Tests/DomainModeling/ProbePlacementGateTests.cs:53-54`
- Description: `CommittedPolyProbes_AreUnderDocsProbesOrArchive` enumerates every `*.poly` then only records `rel.StartsWith("probes/")`. Recomputed `git ls-tree HEAD`: `demo/live/checkout.poly`, `demo/live/domain.poly`, and `docs/plans/archive/experiments/examples/*.poly` exist and cannot fail this test. The real F4 gate is `RepoRoot_ProbesDirectory_MustNotExist:19-24`.
- Suggestion: Narrow the test name to repo-root `probes/`, or fail on other stray live-probe trees if that is actually the rule.
- Status: open

## Sibling-path check

| Semantic | Paths | Invariant on all? | Test forces path? |
|---|---|---|---|
| Type-create auto-link when exactly one many-rel | MCP/runtime leaf: `ExecuteEffect` `CreateEntityInstance` (`DomainEntityInstance.cs:643-645`) → `CreateChildInstance` (`RelationshipName == null`) → `TryAutoLinkUnambiguousOutbound`. VM factory sibling: `CreateByType` (`HostAbi.cs:362-364`) → same `CreateChildInstance`. Subscription: `ExecuteSubscriptionEffects` (`HostAbi.cs:210-236`) → `ExecuteEffectList` → same leaf. | Runtime yes (shared bottleneck) | Yes for **action leaf**: `TypeOnly_UnambiguousManyRel_ListsAndLinks`, combined after Type. VM `CreateByType` (mixed if+create) **not** forced. Subscription `create Fine` (mcp-library) **not** forced. |
| Type-create when RelationshipName set | `Store.Link(rel)` + `TryLinkCreateInBackReference` (if branch, not the new else) | Unchanged | Rel-only / combined Rel via `ExecuteCreateInRelationship` wrap (`HostAbi.cs:717-723`) |
| Type-create zero many-rels | `outs.Count != 1` return | Yes (no link) | IR `CreateEntityInstance_WithoutRelationship_NotLinked` (no navs). Not DSL/MCP |
| Type-create several many-rels | same `Count != 1` return | Code says yes; **untested** | **No** |
| create-in outbound + reverse | `ExecuteCreateInRelationship` → `CreateChildInstance` with RelationshipName; `TryLinkCreateInBackReference` (`HostAbi.cs:667-677`) | Yes at this SHA | Rel-only + combined Rel |
| C# emit Type-create | `EffectLoweringPass.CreateEntityInstance` → `Fine.Create` (`:610-624`) | **Does not auto-link** | Compile-oracle only (compiles; does not assert `Add`) |
| C# emit create-in | `Create{Nav}` + field `Add` (`DomainToCSharpExporter.Notify.cs:219-224`) | Unchanged; links | Existing CreateNav tests; not these fixtures |

`GetRelatedInstances` (`DomainInstanceStore.cs:166-178`) is bidirectional by relationship **name**. F2 reverse asserts use `"patron"` (the reverse link name), so they force `TryLinkCreateInBackReference`, not a scan of the outbound `"fines"` edge.

## Reachability

No new throw/fail-loud. `TryAutoLinkUnambiguousOutbound` is reachable on **valid** domains (Patron with one `fines: many Fine` + `create Fine`) — intended product change, covered by F2 tests (included in the 2632-pass run). Silent no-link on `outs.Count != 1` is reachable on valid domains with zero or several many-rels; severity stays suggestion (documented, untested for several). Emit Fine-discard is reachable on valid export of the same DSL; §0.3 scopes auto-link to the runtime store.

## Invariant-stating comments

- HostAbi `:607-610` / `:644-647`: exactly one many-rel → outbound + reverse; zero/several stay explicit. Runtime implementation matches. Tests cover the =1 case only.
- Type probe `simulate-create-type.poly:5-8`: Expect auto-linked — matches HostAbi + F2 tests.
- Combined `simulate-create-create-in.poly:5-8`: both actions link; list=2, fines links=2 — asserted at `SimulateCreateDogfoodTests.cs:166-179`.
- Guide §0.3 `:73-79`: runtime Type-create auto-link. True for HostAbi. False for C# emit (Issue 1).
- Guide §0.4 `:136-142` taught `when loans Overdue { create Fine }` on a Patron that owns `fines: many Fine` — now runtime-auto-links (consistent with new §0.3). Emit still Fine-discard (Issue 1). `mcp-library.poly` Fine has **no** `patron` reverse (`:42-49`), so subscription Type-create would outbound-link only.
- IR test comment `DomainEntityInstanceTests.cs:2037` (“without RelationshipName → child is NOT linked”) is over-broad after this SHA; body is zero-nav and still true.
- `docs/interpretation/domain-execution-model.md:120` still says CreateEntityInstance “optionally auto-links via RelationshipName” only — stale vs HostAbi else-branch. Not a shipped MCP lie.

## Oracle / verification (read-only)

- `git diff --stat origin/master...d07aabf3`: 11 files, +485/−0.
- `git ls-tree HEAD` `^probes/`: none. `origin/master` `^probes/`: none.
- Compile-oracle Arguments: **9** on HEAD (warehouse, orders, clinic, mcp-library, university, crm, + three simulate-create). origin/master: **6**.
- `--list-tests` includes `TypeOnly_UnambiguousManyRel_ListsAndLinks`, `RelOnly_CreateIn_ListsAndLinksBothDirections`, `Combined_TypeThenRel_OnOnePatron_BothLinked`, `RepoRoot_ProbesDirectory_MustNotExist`, `CommittedPolyProbes_AreUnderDocsProbesOrArchive`, `SimulateCreateProbes_LiveUnderDocsProbesDogfood`, `CreateEntityInstance_WithoutRelationship_NotLinked`, and the three new `Compile_All_DemoDomains_EmitCompilableSolution(docs/probes/dogfood/simulate-create-*)` rows.
- `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false`: **2632 passed, 0 failed, 0 skipped** (~10.7s). Intended `--treenode-filter` did not constrain the run; the named methods are in the compiled suite. Full suite was not used as a substitute for sibling emit/ambiguous coverage (those tests do not exist).

## Checklist

- [x] Diff collected; scope drift noted (5299b98b Warden reviews committed into the product PR; GitHub body still cites `probes/dogfood/`)
- [x] Stance: adversarial / assume wrong; re-verify not rubber-stamp; reviewer is not the implementer
- [x] Producer/consumer keys traced (`fines` outbound, `patron` reverse, `list_instances` registry vs Rel-exists)
- [x] Null / partial / not-found: Type-create with 0/N many-rels is unlinked, not a missing-contract throw
- [x] Sibling-path check done; emit and several-match not forced; VM CreateByType and subscription not forced (shared `CreateChildInstance`)
- [x] Fail-loud / throw: none new; auto-link reachable on valid MCP inputs
- [x] Invariant-stating comments checked against runtime + emit + tests
- [x] Counts and baselines recomputed (Arguments HEAD=9 / master=6; probes/ on HEAD=0; TUnit 2632/2632)
- [x] Same-shape-different-meaning: runtime auto-link vs emit `Fine.Create`
- [x] Fail-closed tests: F2 constructs the session and asserts links; does not strip metadata (N/A)
- [x] Oracles not weakened (oracles added; Type-create expectation rewritten to match product)
- [x] Plan/gate: GitHub body still “Close” + stale probe path; PIPELINE-STATUS untouched; CORE 3.4 dual-path not updated
- [x] Review file written under `docs/`
- [x] Follow-up tasks written under `docs/`
- [x] Prior follow-ups dispositioned from **current** source
- [x] Pass B N/A (mode re-verify, not multi)
- [x] User given paths + top issues
