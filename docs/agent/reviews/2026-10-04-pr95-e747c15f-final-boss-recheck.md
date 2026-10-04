# Final Boss re-check 2: PR 95 (A3a) at `e747c15f`

Appends to `2026-10-04-pr95-0d84edd1-final-boss-recheck.md` (SHIP at `0d84edd1`, F265 needed delete-or-waive; Foreman ruled delete). New findings would start at F290; there are none.

- **Head reviewed:** `e747c15f4bb7a7c063a2691a05a60b18fe1bc89b`. `gh pr view 95` and `git fetch origin pull/95/head` agree, so it matches the assignment. One commit on top of `0d84edd1`; GitHub shows CLEAN and MERGEABLE.
- **Base:** `origin/master` is still `ab0772cd` (not moved; PR 96 has not merged). `git merge-tree --write-tree origin/master e747c15f` is clean (rc 0).
- **Mill:** none; hand re-check. **Clock:** 2026-10-04, 17:58 CDT.

## Verdict: SHIP, gate YES. F265 is closed.

## (1) The diff `0d84edd1..e747c15f`

One commit, one file: `Poly/DomainModeling/Compile/DomainSession.cs`, with exactly two edits and nothing else:
1. Deleted `if (trees.Length == 0) throw new InvalidOperationException("DomainProgramProjection produced no type definitions for entity ...")` (3 lines) from `RegisterTrees`.
2. Rewrote the `ArtifactCatalog` property doc comment.

No test, golden, plan or other file changed. `git grep 'no type definitions'` over the whole head is empty (code, tests, plan, docs). The PR body no longer contains the "moved here from Emit" bullet part or the "Entity with no type definitions" row either (checked with grep on the body).

Does deleting the throw leave a path that silently emits nothing? No. The entity loop now is `var trees = module.Where(t => Belongs(t, entity)).ToArray(); catalog.Register(...)`; nothing after it indexes `trees[0]` or assumes non-empty. `Emit` reads a tree through `TypesOf(tree)` and passes the list to the generator. A tree can only be empty if the projection omits an entity type, and it cannot: `BuildTypeDefsForEntity` adds `new TypeDefinitionNode(entity.Name, ...)` unconditionally at method top level (`DomainToCSharpExporter.cs:805`) and has a single `return typeDefs`; `ToSyntax` calls it for every entity, and duplicate entity names already fail earlier. One thing to know: if the projection is ever changed to skip an entity, the failure would now be an empty `X.cs` instead of an exception. That was the reasoning behind the waiver option; Foreman chose deletion and the code is consistent with that choice.

## (2) F280: property doc

New text: "Empty before the first Lower or Emit. Each Lower or Emit replaces it with a new catalog holding the trees it made (when calls overlap, the last to finish wins): one `scaffolding` tree for the domain and one `entity` tree per entity (the entity type and its stage enum). It is not an emit/contributor file inventory. This is the live instance: anything registered on it by hand is dropped by the next Lower or Emit." True against the code: the property starts as `new()`, only `Lower` and `Emit` assign it (a plain reference write, last writer wins), and `RegisterTrees` fills a new catalog per call. Lines are wrapped normally. **F280 closed.**

## (3) F282 and the PR body

Probe (scratch test, removed after):

| Name | `DomainFactory.Create` | `McpSessionStore.Create` | Create then `Emit` |
|---|---|---|---|
| null | `ArgumentNullException` | `ArgumentNullException` | `ArgumentNullException` |
| empty, space, tab | `ArgumentException` | `ArgumentException` | `ArgumentException` |
| `My Domain` | ok | ok | `FormatException` |
| `A/B` | ok | ok | `FormatException` |

The new body sentence ("already throw `ArgumentException` for null, empty or whitespace-only names, but a name like `My Domain` or `A/B` passes them and fails at `Lower`/`Emit`; names given to `EvolutionBuilder.AddEntity` or `SetDomainName` are not checked until `Lower`/`Emit`") is true (`ArgumentNullException` is an `ArgumentException`; `AddEntity` and `SetDomainName` were probed in earlier reviews). **F282 closed.**

Whole body re-read against the head, sentence by sentence: product files (3) correct; `LowerTreesTests` 9, `UnifiedAddTests` +6 rows; 3054 on master, 3069 here; 58 insertions and 10 deletions in 10 `catalog.golden` files; the shape matrix is consistent after the row removal (no mention of a missing-type throw left; the remaining rows match the code); the 13-of-14 VM context paragraph is unchanged from earlier reviews. No false sentence found.

## (4) Suites and environment

| Check | Result |
|---|---|
| Full suite on head, `dotnet test Poly.Tests -p:NuGetAudit=false` | **3069 passed, 0 failed** (master `ab0772cd`: 3054, +15 as before) |
| `POLY_UPDATE_GOLDEN=1`, full suite | 3069 passed; `git status --short` empty |
| `git diff origin/master e747c15f -- '*.cs.golden'` | empty |
| `catalog.golden` against master | `-SyntaxModule|module|Lower` x10, `+scaffolding|<Domain>|Lower` x10, `+entity|<Domain>/<Entity>|Lower` x48; nothing else |
| `git grep SyntaxModule` over `Poly Poly.Mcp src Poly.Benchmarks` | empty |
| PR size against master | 18 files, +367 -57 |
| `LowerTreesTests`, `UnifiedAddTests`, `SliceCProducerLoopCatalogTests`, `EmitGoldenTests`, each x2 under UTC, `Asia/Tokyo` + `de_DE`, `America/Los_Angeles` + `tr_TR`, invariant globalization | all 32 runs exit 0 |
| Full suite `Asia/Tokyo` + `de_DE`, and `America/Los_Angeles` + `tr_TR` | 3069 total, 5 failed in each: the same five known comma-decimal tests (`Export_RangeNegativeAndFractionalBounds_Parse`, `OpenRange_VerifiedEnvelope_KeepsBoundOpen`, `DecimalNumbers_Addition_ReturnsDouble`, `MixedTypes_ComplexExpression_PromotesCorrectly`, `MixedTypes_IntAndDouble_PromotesToDouble`). No new culture failure |
| `git merge-tree` against `origin/master` (`ab0772cd`) | clean |

## (5) Hand-editability gate on the changed lines

The `RegisterTrees` loop is now two statements with no guard that cannot fire; the property doc is true and wrapped; the `Lower` doc ("throws FormatException ... InvalidOperationException when two lowered types would collide") is fully true again now that the third throw is gone. Nothing misleading, no dead guard, no twin path added. **Gate: YES.**

## Status of earlier findings

| ID | Status |
|---|---|
| F260, F261, F262, F269 | Closed (earlier re-check) |
| F265 | **Closed** (throw deleted, body row and bullet removed, no remaining reference) |
| F266, F280, F282 | Closed |
| F263 | Open, Scot's decision (which rule is the entry rule; `1Order`, `Order-Item`, `A.B` pass `add` but not `apply_dsl`). Pre-existing, non-blocking |
| F264 | Open, non-blocking (`add` entity `DomainResult` or `X`/`XStage` passes entry, fails at export) |
| F267 | Open, nit (message wording typed twice, check written twice, no direct `IsValidPart` test) |
| F268 | Open, nit (concurrency test compares file names only) |
| F281 | Open, nit (plan line citations into `DomainSession.cs` at plan lines 196 and 727 now point at the wrong lines) |
| F283 | Open, nit (plan slice N4 still says "Emit splits files by type name") |

New findings: none.

## What Scot needs to merge

1. **Rebase:** none. Master is `ab0772cd`, the PR is CLEAN, `git merge-tree` is clean. If PR 96 merges first, re-run `git merge-tree`; this PR touches `DomainSession.cs`, `ArtifactId.cs`, `DomainTools.cs`, ten `catalog.golden` files, two test files, one new test file, and two docs.
2. **Merge message:** the PR body is now accurate; use it.
3. **Risks (unchanged):** `Emit` stays fail-open; `create_domain_session("")` now returns a failure message instead of throwing; `Lower` and `Emit` throw `InvalidOperationException` for an entity named `DomainResult` and for entity pairs `X`/`XStage`; ten `catalog.golden` files changed (checked line by line); a future projection change that skips an entity would now yield an empty `X.cs` rather than an exception (cannot happen today).
4. **After merging, check:** `git grep SyntaxModule -- Poly Poly.Mcp src Poly.Benchmarks` is empty; full suite 3069 (plus whatever master brings); `*.cs.golden` has no diff; `create_domain_session("My Domain")` returns the "must be non-empty ..." message.
5. **For Foreman after the merge:** add A3a with its merge SHA to plan line 3 and the wave lists; fix F281 and F283 in the plan.
