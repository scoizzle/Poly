# Final Boss re-check: PR 95 (A3a) at `0d84edd1`

Appends to `2026-10-04-pr95-1e31f6fa-final-boss.md` (NOT SHIP at `1e31f6fa`, F260 and F261 gate NO). Finding numbers: new ones start at F280 (F270 to F279 are reserved for PR 96).

- **Head reviewed:** `0d84edd1e3b7334c01a429e5a0b1b5c0e8a17bd2`. `gh pr view 95` and `git fetch origin pull/95/head` agree, so it matches the assignment. GitHub now shows the PR as CLEAN and MERGEABLE.
- **Base:** `origin/master` is still `ab0772cd` (unchanged since my first review). `git merge-tree --write-tree origin/master 0d84edd1` is clean (rc 0).
- **Commits since `1e31f6fa`:** `0e07dc52` (merge of master `ab0772cd`), `aa30dc26` (plan docs, F260), `0d84edd1` (message asserts, comment edits, test rename).
- **Mill:** none. A short re-check, done by hand.
- **Clock:** 2026-10-04, 17:45 CDT.

## Verdict: SHIP, gate YES, with one item that needs an explicit waiver (F265, below)

F260 and F261 are fixed and the code, tests and goldens are as before. The only judgment call is the "no type definitions for entity" guard, which the author now says cannot be reached. I confirmed it cannot be, and I rule it a suggestion that Scot or Foreman should waive; the reasoning and the one-line alternative are under F265.

## What changed since `1e31f6fa` (item 6)

- `git diff 1e31f6fa 0e07dc52` equals `git diff 6dbab628 ab0772cd` line for line (only the git index hash differs), so the merge commit brings in master and nothing else (C0 files and the plan/doc lines C0 touched).
- `git diff 0e07dc52 0d84edd1` is exactly four files, 16 insertions and 10 deletions: `DomainSession.cs` (doc comments only, no executable line), `LowerTreesTests.cs` (assertions and one test name), `UnifiedAddTests.cs` (two assertion lines), `pipeline-convergence-plan.md` (one line, 153). No product behavior changed.
- Against current master the PR is the same 18 files as before (+368 -56). `*.cs.golden`: empty diff against master. `catalog.golden` over master: `-SyntaxModule|module|Lower` x10, `+scaffolding|<Domain>|Lower` x10, `+entity|<Domain>/<Entity>|Lower` x48, nothing else. `git grep SyntaxModule` over `Poly Poly.Mcp src Poly.Benchmarks` is empty.

## Tests and environment (item 6)

| Run | Result |
|---|---|
| Full suite on head, `dotnet test Poly.Tests -p:NuGetAudit=false` | **3069 passed, 0 failed** |
| Master `ab0772cd` (from my first review, unchanged) | 3054 passed; +15 = 9 `LowerTreesTests` + 6 new `UnifiedAddTests` rows (the merge brought no test change) |
| Full suite with `POLY_UPDATE_GOLDEN=1` | 3069 passed; `git status --short` empty afterwards |
| `LowerTreesTests`, `UnifiedAddTests`, `SliceCProducerLoopCatalogTests`, `EmitGoldenTests`, each x2: UTC, `Asia/Tokyo` + `de_DE`, `America/Los_Angeles` + `tr_TR`, invariant globalization | all 32 runs exit 0 |
| Full suite `Asia/Tokyo` + `de_DE`, and `America/Los_Angeles` + `tr_TR` | 3069 total, 5 failed in each: `Export_RangeNegativeAndFractionalBounds_Parse`, `OpenRange_VerifiedEnvelope_KeepsBoundOpen`, `DecimalNumbers_Addition_ReturnsDouble`, `MixedTypes_ComplexExpression_PromotesCorrectly`, `MixedTypes_IntAndDouble_PromotesToDouble`. The same five as on master; no new culture failure |
| `git merge-tree` against `origin/master` | clean |

## (1) F260: plan text

Plan line 153 now reads: "...the compiled trees were one list split by entity name in `Emit`; contributors return `(fileName, text)` pairs. ... since A3a `Lower` registers one scaffolding tree and one tree per entity (`RegisterTrees`) and `Emit` only reads them (the placeholder is gone). Only the last claim still holds: contributors return plain pairs." Checked against the head: `RegisterTrees` is in `Lower`'s path (`LowerToCatalog`), `Emit` reads trees from its own catalog, contributors still return `(FileName, Source)` pairs. True. **F260 closed.** The merge also fixed plan line 3 and the wave lists for C4a and N3; A3a is not listed as done yet, which is correct until it merges.

Other stale claims, searched in the plan and docs (excluding review docs and archives): `pipeline-stage-map.md` difference 1 is accurate; `first-principles.md:222` ("`Emit` runs `Lower`, ... then `CSharpGenerator` per entity") is still true. Two small leftovers, both nits, see F281 and F283.

## (2) F261: PR body read against the head

Every sentence checked. All true, including: three product files named (`DomainSession.cs`, `ArtifactId.cs`, `DomainTools.cs`); `LowerTreesTests` has 9 tests; `UnifiedAddTests` has 6 new rows; "3054 on master (`ab0772cd`), 3069 here (+9, +6)"; 58 insertions and 10 deletions in 10 `catalog.golden` files; the shape matrix. Items I verified by running code: a domain with no entities gives only `Poly.Types.cs` and the catalog `scaffolding|Empty|Lower`; `SetDomainName("A B")` evolves and then `Emit` throws `FormatException` with the rule message; entity `DomainResult` and the `X`/`XStage` pair throw `InvalidOperationException`; the body also says honestly that `Lower` keeps the `FormatException` as a backstop, that entry validation for the C# API is not in this PR, and that the "no type definitions" row has no test. The "one edge where behavior changes" sentence is gone, and the `FormatException` and `InvalidOperationException` rows now match the code. **F261 closed.** One imprecision remains, F282.

## (3) F262: message asserts

Both new tests now assert `response.Message` contains "must be non-empty" (`UnifiedAddTests.cs:31` and `:44`). Mutation in a scratch worktree, reset after each:

| Mutation | Result |
|---|---|
| MCP message replaced with `"nope"` | **red: 6 of 6** (3 `create_domain_session` rows, 3 `add` rows). Before the fix this was 27 of 27 green |
| `RegisterTrees` owner message reworded | red: 2 (`..._IsRefused` and `..._IsRefusedEvenWithoutStages`, both now assert "belongs to the trees") |
| `RegisterTrees` duplicate-type message reworded | red: 1 (`Lower_EntityNamedDomainResult_IsRefusedAsDuplicateType`, asserts "more than once") |
| `ArtifactId.RequireValid` message reworded | red: 1 (`Lower_DomainNameWithWhitespace_Throws`, asserts "My Domain" and "no whitespace"); the 27 `UnifiedAddTests` stay green because the MCP message is typed separately (F267, still open) |

**F262 closed.**

## (4) F266 and F269: comments and test name

- `LowerToCatalog` comment: "another Lower or Emit on this session may replace at any time" is true.
- `ArtifactCatalog` property doc: "Each Lower or Emit replaces it ... (when calls overlap, the last to finish wins)" is true (plain reference write in both). Two leftover sentences in the same doc still name only `Lower`: see F280.
- `Lower` doc: "Throws `FormatException` when a domain or entity name cannot be part of an artifact id, and `InvalidOperationException` when two lowered types would collide (see `RegisterTrees`)" is true; it does not mention the unreachable no-type-definitions throw, which is fine.
- Test `Lower_EntityNamedDomainResult_IsRefusedAsDuplicateType` is accurate: the exception is the "defines type 'DomainResult' more than once" one and the test asserts it (mutation above).

**F266 closed except for the leftover in F280. F269 closed.**

## (5) F265: the "no type definitions for entity" guard

Is it reachable? No, by any path I can find. In `RegisterTrees`, the entities come from `domain.Types.OfType<Entity>()`; the module comes from `RuntimeAnalysisCache.GetOrLower` for the same `Domain` instance, which calls `DomainProgramProjection.ToSyntax`; that calls `BuildTypeDefsForEntity` for every entity, which adds a `TypeDefinitionNode` named `entity.Name` unconditionally (`DomainToCSharpExporter.cs:805`) and has no earlier return. Duplicate entity names already fail earlier in `ToSyntax` (`ToDictionary`). Direct use of `Lower` or `Emit` goes through the same path, so there is no public way to get a module without a type for each entity. The PR body says "No test reaches it; I could not build such a domain": honest, but it understates (it cannot be built).

Is it a gate NO? My ruling: **suggestion, not gate NO, but it needs an explicit waiver**, because Scot's standing order blocks dead guards unless waived and I cannot waive it myself. Reasons for recommending the waiver:
1. The guard and its message are on master, in `Emit`, unchanged; this PR moved it. It is not new dead code.
2. It asserts a contract between two components (the projection emits a type per entity) and names the component that broke it. Without it, a future projection bug would register an empty `entity` tree and `Emit` would write an empty `X.cs` without a word. It fails closed.
3. Deleting it is a behavior-neutral change outside the A3a card; it can go in a later slice.
4. The PR discloses that no test reaches it.

If Scot or Foreman does not want to waive it, the fix is small: either delete the `if (trees.Length == 0) throw` (3 lines) and the shape-matrix row, or add a one-line comment saying it is an invariant check that cannot fire today. A second pass would then be docs/comment-only.

**F265 stays open as a suggestion; waiver requested.**

## Status of F260 to F269

| ID | Status |
|---|---|
| F260 | Closed (plan line 153 true) |
| F261 | Closed (PR body true; F282 imprecision remains) |
| F262 | Closed (6 of 6 red under mutation) |
| F263 | Open, Scot's decision (which rule is the entry rule; `1Order`, `Order-Item`, `A.B` are accepted by `add` and unreadable by `apply_dsl`). Pre-existing, non-blocking |
| F264 | Open, skipped by author. `add` entity `DomainResult` or `X`/`XStage` passes entry and fails at export. Non-blocking; a later analyzer slice |
| F265 | Open, suggestion, confirmed unreachable, waiver requested (above) |
| F266 | Closed, with leftover F280 |
| F267 | Open, skipped by author (message wording typed twice, check written twice, no direct `IsValidPart` test). Non-blocking nit; confirmed by mutation above |
| F268 | Open, skipped by author (concurrency test compares file names only). Non-blocking nit |
| F269 | Closed |

## New findings (all nits, none blocks)

| ID | Where | Finding |
|---|---|---|
| F280 | `DomainSession.cs:42-48` | The property doc now says "Each Lower or Emit replaces it", but two other sentences still say only `Lower`: "Empty before Lower" (an `Emit` first call also fills it) and "dropped by the next Lower". The second doc line also grew to about 150 characters. Imprecise but true for `Lower`. |
| F281 | `pipeline-convergence-plan.md:196` and `:727` | Line citations into `DomainSession.cs` moved when this PR added code: `TryAnalyzeForEmit` is cited at `:203-208` and the fallback at `:169-174`, now at `:246` and `:227-230`. Both citations name the function, so nobody is misled about which code is meant, and the master citations were already approximate (205 and 173-176); a navigation pointer, not a claim about behavior. For Foreman's post-merge plan edit. If you read line citations as claims, this becomes a docs-only fix. |
| F282 | PR body, shape-matrix row "Domain or entity name not valid in an id" | Says a C#-built `Domain` or `McpSessionStore.Create` called directly "fails at `Lower`/`Emit`" for "empty, whitespace, `/`, `#`". For an empty or whitespace-only domain name `DomainFactory.Create` and `McpSessionStore.Create` throw `ArgumentException` at creation (probe in the first review), earlier than `Lower`. `/`, `#`, inner whitespace and entity names behave as stated. |
| F283 | `pipeline-convergence-plan.md:291` (slice N4) | "`Emit` splits files by type name" and "NOT SHIP if only Emit throws" predate A3a: the split is now in `Lower`, which also throws. Describes a problem N4 will fix, so the meaning is not lost. |

## (7) Hand-editability gate, my own, on the changed files

- `DomainSession.cs` (doc comments): true against the code; F280 is the only imprecision.
- `LowerTreesTests.cs`, `UnifiedAddTests.cs`: assertions are specific and mutation-tested (above); test names match what they assert; readable cold.
- Plan line 153: true. PR body: true except F282.
- No new dead guard, twin path, legacy or fallback branch in this fixup. The existing unreachable guard is F265.

**Gate: YES** (with the F265 waiver noted).

## What Scot needs to merge

1. **Rebase:** none. The PR is CLEAN against `ab0772cd`; `git merge-tree` is clean. The merge commit `0e07dc52` is already in the branch, so a squash or merge will not need an update.
2. **Decide F265** (above): accept the pre-existing guard as is, or ask for the 3-line deletion or a comment.
3. **Merge message:** the PR body is now accurate (F282 aside); use it.
4. **Risks (unchanged):** `Emit` stays fail-open; `create_domain_session("")` now returns a failure message instead of throwing; `Lower` and `Emit` now throw `InvalidOperationException` for an entity named `DomainResult` and for entity pairs `X`/`XStage`; ten `catalog.golden` files changed (checked line by line).
5. **After merging, check:** `git grep SyntaxModule -- Poly Poly.Mcp src Poly.Benchmarks` is empty; full suite 3069 (assuming master has not moved); `*.cs.golden` has no diff; `create_domain_session("My Domain")` returns the "must be non-empty ..." message.
6. **For Foreman after the merge:** add A3a with its merge SHA to plan line 3 and the wave lists; fix the line citations (F281) and the N4 wording (F283).
