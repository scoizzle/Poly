# Final Boss one-pass review: PR 95, slice A3a "Lower registers its trees as artifacts; Emit reads them from the catalog"

- **PR:** https://github.com/scoizzle/Poly/pull/95 (implementer: 100x hand/Grok lineage)
- **Head reviewed:** `1e31f6fa1377364f0e8e492e4bbf1cc5d7dea6f1`. `gh pr view 95` and `git fetch origin pull/95/head` both give this SHA, so it matches the assignment.
- **Base:** PR branch point is `6dbab628` (N3 #93). `origin/master` is now `ab0772cd` (C0 #94, dead DEI helpers). GitHub says the PR is BEHIND but MERGEABLE. `git merge-tree --write-tree origin/master 1e31f6fa` is clean (rc 0), and a real merge into `ab0772cd` gives the same 18 files, +361 -55.
- **Mill:** OpenCode, `opencode-go/deepseek-v4.1-flash`, first attempt (run `pr95-fb`). The implementer is hand/Grok lineage, so this is the opposite mill. Every mill finding was spot-checked against the repo (disposition at the end).
- **Clock:** 2026-10-04, 17:23 CDT. Not V11: on SHIP plus gate YES this goes to Scot via Chieftan for hand-merge.
- **Razor:** SHIP at the same head (comment 5982775617, doc on branch `review/razor-pr95-1e31f6fa`). I read it and re-verified everything below on my own, including the hand-editability gate.

## Verdict: NOT SHIP (documentation only; the code is good)

Everything that is code or test is green and I would merge it. Two sentences that the gate rule treats as false block the ship, and both are fixable in minutes without touching code:

- **F260** `docs/domain-modeling/pipeline-convergence-plan.md:153`: the "Wrong today" paragraph still says "the compiled trees are one list split by entity name in `Emit`" and then says "the rest of this paragraph still holds". After A3a the split is in `Lower` (`RegisterTrees`), and `Emit` only reads trees. The plan's own rule (section 1, "Code is truth") says every "Wrong today" claim was checked.
- **F261** The PR body (which Scot will see as the merge text) says things the head contradicts: "product: DomainSession.cs only", "LowerTreesTests (4 tests)", "Suite ... 3058", and a shape-matrix row saying a bad name makes `Lower` throw `FormatException` and "This is the one edge where behavior changes".

After those two edits (the plan edit is a docs-only commit; the body edit is `gh pr edit`), I expect SHIP without a re-run of the suites. A docs-only commit cannot change a test result, but the head SHA would change, so the SHA in my verdict moves with it.

Findings: 10 (F260 to F269): 2 block (both documentation), 4 suggestions that do not block, 4 nits. No bug in code.

## Checklist (assignment points a to h)

| # | Point | Result |
|---|---|---|
| a | `*.cs.golden` byte-identical, `catalog.golden` changes only the three line patterns | **YES.** See "(a) in detail". |
| b | Razor F2 truly fixed | **YES.** 6 runs of 8 threads x 400 calls (19,200 calls): 0 wrong-domain file sets, 0 exceptions. The in-repo test fails when the bug is put back. |
| c | F1 entry validation | **YES for the MCP entry points, with documented gaps in the C# API.** Same rule function. Error text not tested (F262). Gap findings F263, F264. |
| d | Razor's F5, F8 to F11 | All five verified as non-blocking and accurately described. |
| e | Suites, culture, merge | 3069 on head, 3054 on master (+15). No new culture failure. Golden regen leaves `git status` clean. merge-tree clean. |
| f | Mutation tests | 13 mutations; the ones that matter are red. Two weak spots (F262, F265). |
| g | Hand-editability gate | **Code and tests YES. Docs NO (F260, F261). Gate = NO until those two sentences are fixed.** |
| h | Poly principles, scope, body vs diff | Principles hold. Scope: 2 product files beyond the card (justified by Scot's option a). Body does not match the diff (F261). |

## (a) in detail: goldens

- `git diff --stat 6dbab628 pr95 -- '*.cs.golden'` and the same against `origin/master`: both empty. All C# goldens are byte-identical.
- Ten `catalog.golden` files changed (clinic, crm, hotel, mcp-library, orders, simulate-create-create-in, simulate-create-in, simulate-create-type, university, warehouse). Changed lines, counted: `-SyntaxModule|module|Lower` x10, `+scaffolding|<Domain>|Lower` x10, `+entity|<Domain>/<Entity>|Lower` x48. That is 58 insertions and 10 deletions, matching `--numstat`.
- For each of the ten files, removing the three patterns from old and new leaves identical text (md5 compared). Neither side has a trailing newline, no CR characters. For each file, the `entity|...` lines match, one for one, the entity `*.cs.golden` files in that folder.
- `git grep SyntaxModule` over `Poly`, `Poly.Mcp`, `src`, `Poly.Benchmarks`: empty. It remains only in plan text (history of A2b and A3a). Added lines contain no "legacy" or "fallback".
- `POLY_UPDATE_GOLDEN=1` over the full suite on the head: 3069 passed, `git status --short` empty afterwards.

## (b) in detail: thread safety

Probe (scratch test, not committed): one `DomainSession`, `Parallel.For` with 8 workers, 400 `Emit` calls each (3,200 per run), checking the exact file-name list for each call.

| Variant | Head `1e31f6fa` |
|---|---|
| Alternating two domains, 3 runs | wrong 0 / 0 / 0, exceptions 0 / 0 / 0 |
| Same domain on all threads, 3 runs | wrong 0 / 0 / 0, exceptions 0 / 0 / 0 |

Put-the-bug-back check: I made `Emit` call `Lower(...)` and then read the `ArtifactCatalog` property after `TryAnalyzeForEmit` (the shape Razor reported). The in-repo test `Emit_FromManyThreadsOnOneSession_EachCallGetsTheFilesOfItsOwnDomain` failed 3 of 3 runs, and my probe threw about 1,600 `NullReferenceException`s per 3,200 calls (in `TypesOf`). If the property is read straight after `Lower`, before the analysis, the window is too small and the test passes by luck three times; that is not a shape the code ever had.

Code reading of what A3a added:
- `RegisterTrees` builds a new `ArtifactCatalog` per call from locals. Nothing is shared until the plain property write `ArtifactCatalog = catalog;` (a reference write, last writer wins). `Emit` never reads the property back. No check-then-act, no enumeration of shared state, no lazy init added.
- `ArtifactCatalog` itself is not thread-safe for writes, but a catalog is only mutated inside `RegisterTrees`, before it is published.
- Payloads are `ReadOnlyCollection` wrappers over fresh arrays. The module list and `TypeDefinitionNode`s from the cache are only read.
- Lock ordering: A3a takes no lock. Pre-existing and not touched: `RuntimeAnalysisCache.GetOrLower` checks `holder.Module` outside the lock and returns it with a second read, while `Bind` (under the lock) can reset it when the analysis object changes. That can only race when different analysis objects are used for the same `Domain`. Razor saw master fail that way. It is not in this diff. I list it as a follow-up note, not a finding.

## (c) in detail: entry validation

**Rule function.** `ArtifactId.IsValidPart(string?)` (new, public) is non-empty, no `char.IsWhiteSpace`, no `/`, no `#`. `ArtifactId.RequireValid` now calls it, so `Create`, `Parse`, `DeclareType` and the Lower backstop use the same function. The MCP tools call the same function. There is no twin copy of the rule logic. Only the message text is typed twice (F267). The rule has no reserved words and no length limit.

**Every path that can introduce a name** (product code; `git grep` for `McpSessionStore.Create`, `DomainFactory.Create`, `AddEntity`, `SetDomainName`, `new Domain(`; the MCP tool list has no rename, import or load tool):

| Path | Guarded at entry? | Evidence |
|---|---|---|
| MCP `create_domain_session` | **Yes** | `DomainTools.cs:151`, message from `EvolveTool.InvalidNameMessage` |
| MCP `add` kind `entity` | **Yes** | `DomainTools.cs:531` |
| MCP `apply_dsl` (`domain X`, entity names) and `DslCompiler` | **Parser grammar** (letters, digits, `_`; stricter than the rule) | probe P3 |
| MCP other `add` kinds (property, stage, action, ...) | Not needed today: these names do not feed ids | code reading |
| `McpSessionStore.Create` (internal) | No. Only caller in product code is the guarded tool | probe P4: backstop at export |
| C# API: `DomainFactory.Create`, `EvolutionBuilder.AddEntity`, `new AddEntityChange`, `SetDomainName` | **No.** Backstop only | probe P5, P6 |
| Backstop in `Lower` (via `ArtifactId.Create`) | Throws `FormatException`; `export_domain_to_csharp` and `DslCompiler` catch it and report | probe P4, P5 |
| File load / rename / evolve tools | None exist | grep, tool list |

The author's own PR comment (5982678055) states the C# API and `McpSessionStore.Create` are not validated at entry and lists them for a decision. That is documentation of the gap, in the PR comment (not in code or the body). It is accurate.

**Probe results** (scratch test, run against the head; `IsValid` is `ArtifactId.IsValidPart`):

| Name | IsValid | `create_domain_session` | `add` entity | `apply_dsl` (domain / entity) | `McpSessionStore.Create` then export | C# API entity (evolve, then Emit) | `DomainFactory.Create` then Emit |
|---|---|---|---|---|---|---|---|
| `My Name` | no | refused: `Name 'My Name' must be non-empty with no whitespace, '/' or '#'.` | refused, same text | Parse error: `Expected 'entity', 'enum', ...` | export fails: `Domain-to-C# export failed: Artifact id name 'My Name' must be non-empty ...` | evolve succeeds, `FormatException` at Emit | `FormatException` |
| `A/B` | no | refused, same text | refused | Parse error (`got '/' (Slash)`) | export fails, same text | evolve ok, Emit `FormatException` | `FormatException` |
| `A#B` | no | refused | refused | Parse error (`Unexpected character '#'`) | export fails | evolve ok, Emit `FormatException` | `FormatException` |
| tab | no | refused (`Name '	' must ...`) | `Kind 'entity' requires payload field 'name'.` | Parse error | `ArgumentException` (whitespace) | evolve ok, Emit `FormatException` | `ArgumentException` |
| space / empty | no | refused | `requires payload field 'name'` | Parse error | `ArgumentException` | evolve ok, Emit `FormatException` | `ArgumentException` |
| null | no | refused (`Name ''`) | `requires payload field 'name'` | n/a | `ArgumentNullException` | `ArgumentNullException` | `ArgumentNullException` |
| newline inside, NBSP inside | no | refused | refused | Parse error | export fails | Emit `FormatException` | `FormatException` |
| `Café` (unicode letter) | yes | created | added, export ok | accepted | ok | ok (`Café.cs`) | ok |
| `Café X` | no | refused | refused | Parse error | export fails | Emit `FormatException` | `FormatException` |
| 5000 characters | yes | created | added, export ok | accepted | ok | ok | ok |
| `1Order` | yes | created | added, export ok | **Parse error** (DSL refuses leading digit) | ok | ok (`1Order.cs`) | ok |
| `class` | yes | created | added, export ok | accepted | ok | ok | ok |
| `DomainResult` as entity | yes | n/a | added; **export then fails** `The lowered module defines type 'DomainResult' more than once.` | same | n/a | evolve ok, Emit `InvalidOperationException` | n/a |

Session state after a refused `create_domain_session`: `SessionId` is null, so no session exists. After a refused `add`, the overview does not list the name (tested).

Gaps, none blocking and all inside what Scot's option (a) asked for ("reject at API creation using the ArtifactId rule, clear error, tests, keep the Lower throw as backstop"):
1. The error text is not pinned by any test (F262; mutation M13).
2. MCP `add` accepts names the DSL refuses (`1Order`, `Order-Item`, `A.B`), and `export_dsl` then prints text that `apply_dsl` cannot read back (probe). Pre-existing and outside the ArtifactId rule, so it is a decision for Scot (F263).
3. Entity names `DomainResult` or `{Other}Stage` pass entry and fail at export (F264, Razor F9).
4. The C# API paths rely on the backstop (Razor F8, confirmed).

## (d) Razor's open non-blockers

- **F5 confirmed non-blocking.** `EmitGoldenTests.SampleDomains` is a hand-kept list of 10. The repo has 14 `.poly` files under `docs/probes` and `demo`; `smoke.poly`, `demo/live/checkout.poly` and `demo/live/domain.poly` have no golden, and `nested-invoke-type-mismatch.poly` is the one that does not emit. The file is untouched by this PR (T0 owns it). The description is accurate.
- **F8 confirmed.** The check is written out twice in `DomainTools.cs` (the message is shared); the C# API only fails at Emit (probes P4 to P6). Non-blocking because option (a) names "API creation" and the backstop is kept. Accurate. Carried into F267 and the follow-ups.
- **F9 confirmed with a probe**: `add` entity `DomainResult` succeeds, `export_domain_to_csharp` fails with the duplicate-type message. Before A3a, master would have printed C# that does not compile, so this is not a regression. Carried as F264.
- **F10 confirmed, pre-existing**: stage and property names are not checked as C# identifiers. I add that entity names have the same property (F263).
- **F11 confirmed, a nit**: `Lower_EntityNamedDomainResult_IsRefusedInsteadOfEmittingAnEmptyScaffolding` asserts only that the exception message contains `DomainResult`; the refusal is the duplicate-type one. The name is true as history (this is the F6 case), just not what the assertion proves. Carried in F269.

## (e) in detail: suites

| Run | Total | Failed |
|---|---|---|
| Head `1e31f6fa`, `dotnet test Poly.Tests -p:NuGetAudit=false`, default culture | 3069 | 0 |
| Master `ab0772cd`, same command | 3054 | 0 |
| Difference | +15 | 0 (9 in `LowerTreesTests`, 6 new parameter rows in `UnifiedAddTests`) |
| Master `ab0772cd` plus the PR merged in a scratch worktree | 3069 | 0 |
| Head, full suite, `POLY_UPDATE_GOLDEN=1` | 3069 | 0, `git status` clean |

Foreman's 3069 is right. The first master run was killed by the box (exit 137, memory pressure from my parallel runs) and was repeated alone.

Full suite under cultures (the runner prints localized labels, so I counted from the summary lines):

| Setting | Master (3054) | Merged (3069) | Failed names |
|---|---|---|---|
| `TZ=Asia/Tokyo`, `de_DE.UTF-8` | 5 failed | 5 failed | the five known comma-decimal tests, identical list on both |
| `TZ=America/Los_Angeles`, `tr_TR.UTF-8` | 5 failed | 5 failed | same five |

No new culture failure.

New tests only (`LowerTreesTests`, `UnifiedAddTests`, `SliceCProducerLoopCatalogTests`, plus `EmitGoldenTests`), each run twice per setting by calling the test DLL with a tree-node filter (exit code 0 in all 8 runs, test counts 9 / 27 / 9 / 21):

| Setting | Result |
|---|---|
| UTC | pass x2 |
| `TZ=Asia/Tokyo`, `de_DE` | pass x2 |
| `TZ=America/Los_Angeles`, `tr_TR` | pass x2 |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | pass x2 |

merge-tree against `ab0772cd`: clean. C0 touched DEI runtime files, the plan doc and two other docs; the plan edits do not overlap the A3a lines textually.

## (f) mutation tests

Scratch worktree, never pushed, reset to the head after each. "Red" means a test failed.

| # | Mutation | Result |
|---|---|---|
| M1 | Remove the `create_domain_session` check | red: 3 (`CreateDomainSession_...Rejects_Fails` x3) |
| M2 | Remove the `add` entity check | red: 3 (`Add_Entity_...Rejects_Fails` x3) |
| M3b | `Emit` calls `Lower`, then reads the property after the analysis (Razor F2 shape) | red 3 of 3 runs (`Emit_FromManyThreads...`); probe: ~1,600 `NullReferenceException` per 3,200 calls |
| M4 | Entity artifact type renamed `entity` to `tree` (artifact-list line changes) | red: 2 in `LowerTreesTests`, 2 in `SliceC...`, 10 `catalog.golden` comparisons |
| M5 | Scaffolding type renamed `scaffolding` to `scaffold` | red: 1 + 2 + 10 |
| M6 | Owner check (`owners.Count > 1`) off | red: `..._IsRefusedEvenWithoutStages` (the with-stages case is still caught by the duplicate check) |
| M7 | Duplicate-type check off | red: the DomainResult test |
| M8 | `IsValidPart` accepts `#` | red: 2 (`A#B` on both MCP tools); the `LowerTreesTests` backstop test uses whitespace only, so it stays green |
| M9 | Tree payload a mutable list | red: `Lower_TreePayloadsCannotBeChangedByACaller` |
| M10 | `Emit` writes scaffolding first | red: `Emit_WritesOneFilePerEntityTreeThenTheScaffolding` and the concurrency test; `EmitGoldenTests` stays green (it compares per file name) |
| M11 | Stage enum left out of entity trees | red: 2 in `LowerTreesTests`, 10 golden comparisons |
| M12 | Delete the "no type definitions for entity" guard | **green** (F265) |
| M13 | Replace the MCP error text with `"nope"` | **green**, all 27 `UnifiedAddTests` pass (F262) |

## (g) hand-editability gate

Could Scot open these files cold and change them without an agent?

- `DomainSession.cs`: yes. Lower, `LowerToCatalog`, `RegisterTrees`, `Emit`, `TypesOf` are short, each with a doc comment I checked sentence by sentence against the code; the membership rule is one local function (`Belongs`); no dead guard that I could show except the pre-existing untested one (F265); no legacy or fallback branch. The one `!` on `Find(...)` is safe because the scaffolding tree is always registered a few lines earlier.
- `ArtifactId.cs`, `DomainTools.cs`: yes. The only twin is message text (F267).
- `LowerTreesTests.cs`, `UnifiedAddTests.cs`: yes, readable, one idea per test. Imprecise names are nits (F268, F269).
- Plan and stage map: **no.** Stage map line 149 is accurate. Plan line 153 has a sentence that is false after this PR (F260).
- PR body: contradicts the head (F261).

Per the rule a false sentence is a gate NO, so: **gate NO until F260 and F261 are fixed; code and tests alone would be YES.**

## (h) Poly principles, scope, body versus diff

- **Simple syntax tree is the unit of meaning:** yes. The trees are the lowered `TypeDefinitionNode`s themselves, registered under ids (decision 1: name path plus type; decision 3: one tree per entity plus a scaffolding tree). Emit prints from them; no second copy of the structure.
- **Simulate equals print:** nothing in this diff touches simulation. All `*.cs.golden` files are unchanged, so printed C# is unchanged. Pre-existing gap noted in F263 (names the DSL cannot read back).
- **Every mutation enforces every invariant:** partially, as before. The name rule is enforced for entity creation through MCP, not for the C# API, and not for the `DomainResult` or `XStage` collisions (F264). Not a regression.
- **Code is source of truth:** yes for code; the two false doc sentences are F260 and F261.
- **Hand-editable:** see (g).
- **Decisions 1 to 20, V1 to V11:** no contradiction found. Emit stays fail-open (decision 7 and stage-map difference 3 are still open work, untouched, and the body says so). The plan statement "Stage map 2.5 Definition of done is NOT met until A6" is not claimed as met.
- **Scope against the card:** card Files list names `DomainSession.cs` (and `DomainProgramProjection.cs` only if the split moves; it does not). The diff has 18 files: `DomainSession.cs` (103 lines), plus two product files beyond the card, `ArtifactId.cs` (+11 -4) and `DomainTools.cs` (+14), which implement Scot's option (a) of 2026-10-04. Ten `catalog.golden`, `LowerTreesTests.cs`, `UnifiedAddTests.cs` (+25), `SliceCProducerLoopCatalogTests.cs`, and the plan (8 lines) and stage map (2 lines). No unrelated change. `git diff --check` is clean.
- **Body versus diff:** does not match (F261). The accurate, current statements are in PR comments 5982618368 and 5982678055.

## Findings

Numbering starts at F260. Severity: **S** suggestion, **N** nit. "Blocks" = blocks SHIP.

| ID | Sev | Blocks | Where | Finding |
|---|---|---|---|---|
| F260 | S | **yes** | `docs/domain-modeling/pipeline-convergence-plan.md:153` | "Wrong today" still says "the compiled trees are one list split by entity name in `Emit`" and that "the rest of this paragraph still holds". Since this PR the split is in `Lower` (`RegisterTrees`) and `Emit` reads trees (`DomainSession.cs:178-215`, `Emit` at 219 on). The plan promises every "Wrong today" claim was checked (section 1). Fix: make that clause past tense or move it, and say only that contributors still return plain pairs still holds. Docs-only. |
| F261 | S | **yes** | PR 95 body | False against the head: (1) "product: `DomainSession.cs` only" (the PR also changes `ArtifactId.cs` and `DomainTools.cs`); (2) "`LowerTreesTests` (4 tests)" (there are 9); (3) "Suite 3054 on master, 3058 here" (3069 here, 3054 on master); (4) the shape-matrix row "name not valid in an id: `Lower` now throws `FormatException` ... This is the one edge where behavior changes" (MCP now refuses at entry; `Lower` also now throws `InvalidOperationException` for colliding names); Since Scot hand-merges, the body becomes the merge message. Fix with `gh pr edit`. |
| F262 | S | no | `Poly.Tests/Mcp/UnifiedAddTests.cs:21-45` | The new tests assert `Success == false` (and no session / no entity) but never the message. Scot asked for a clear error with tests. Mutation M13 (message replaced by `"nope"`) leaves all 27 tests green. Add `await Assert.That(response.Message).Contains("must be non-empty")` to both tests. |
| F263 | S | no | `DomainTools.cs:531` (entity `add`), `ArtifactId.IsValidPart` | The entry rule is weaker than the DSL grammar: `add` accepts `1Order`, `Order-Item`, `A.B`, which `apply_dsl` refuses. After adding them, `export_dsl` prints text that `apply_dsl` cannot read (probe: 3 of 5 names fail to re-apply) and C# export emits `1Order.cs`. Pre-existing on master and outside option (a), so not a regression. The error message tells agents only about whitespace, `/` and `#`. Decision for Scot: keep the ArtifactId rule at entry, or also require a DSL-identifier name (follow-up). |
| F264 | S | no | `RegisterTrees` (`DomainSession.cs:178-202`), `StructuralDomainAnalyzer` | `add` entity `DomainResult` (or `XStage` next to `X`) is accepted and the session cannot export afterwards (Razor F9, confirmed). Catching it at mutation time (analyzer) would match "every mutation enforces every invariant". Master printed C# that does not compile, so this PR narrows rather than widens the gap. |
| F265 | N | no | `DomainSession.cs` (`trees.Length == 0` throw in `RegisterTrees`) | Pre-existing guard moved from `Emit`, but untested: deleting it keeps all 9 `LowerTreesTests` green (M12). The PR comment calls it "reachable"; I could not construct a case (`DomainProgramProjection` emits a type for every entity, and duplicate entity names fail earlier in `ToSyntax`). Either add a test that reaches it or say it is a defensive check. |
| F266 | N | no | `DomainSession.cs:42-48` (property doc), `:162-163` (comment) | They name only `Lower` as the thing that replaces `ArtifactCatalog`, but `Emit` also assigns it (`:224`, last writer wins), so under concurrency the property shows whichever call finished last. True but imprecise; say "Lower or Emit". |
| F267 | N | no | `DomainTools.cs:772-773`, `ArtifactId.cs:62-66` | The rule is in one function, but its wording and separators are typed twice (`InvalidNameMessage` vs `RequireValid`), and the check is written out in two tools (Razor F8). `IsValidPart` has no direct unit test. |
| F268 | N | no | `LowerTreesTests.cs:97-120` | The concurrency test is named "gets the files of its own domain" but compares file names only (contents are not compared). It does fail on the real regression (M3b), so this is only a name/assertion gap. |
| F269 | N | no | `LowerTreesTests.cs` (`Lower_EntityNamedDomainResult_...`), `DomainSession.cs:150` (`Lower` doc) | (1) Test name describes the F6 history ("instead of emitting an empty scaffolding") while the assertion and the code path are the duplicate-type refusal (Razor F11). (2) The `Lower` doc ("Stage 3 ...") does not say that `Lower` and `Emit` now also throw `InvalidOperationException` (colliding type names) and `FormatException` (bad names). |

Open at SHIP time (after F260 and F261 are fixed): the non-blocking items above plus Razor's F5, F10 (see the follow-ups file).

## Mill disposition (OpenCode, `opencode-go/deepseek-v4.1-flash`, 8 findings)

- Kept after checking: mill #2 (add entity accepts `DomainResult` / `XStage`; confirmed by probe) = F264; mill #3 (concurrency test compares names only) = F268; mill #4 and #5 (`Emit` also assigns the property; docs do not say so) = F266; mill #6 (message text twin, no direct unit test of `IsValidPart`) = F267.
- **Dropped, false:** mill #1 ("entities `Permit` without stages and `PermitStage` compile fine on master, so the owner check over-refuses"). I ran master `ab0772cd` on exactly that domain: `Permit.cs` and `PermitStage.cs` both define `PermitStage`, so the printed C# does not compile. The refusal and the doc sentence are correct.
- **Dropped, not a defect:** mill #7 (scaffolding file now always emitted). `DomainResult` types are always added (`DomainProgramProjection.cs:117-118`) and an entity named `DomainResult` is refused, so the file is never empty; the PR says so.
- **Dropped, false:** mill #8 (stage map still lists "the syntax module" as an output). `Lower` still returns the module; the sentence lists outputs, not artifacts.

## What Scot needs to merge (once F260 and F261 are fixed)

1. **Rebase:** none needed. `origin/master` is `ab0772cd` (C0), the branch point is `6dbab628`, `git merge-tree` is clean and the merged tree gives 3069 passing. GitHub shows BEHIND, so use "Update branch" or merge as is; either gives the same tree.
2. **After fixing F260:** the head SHA changes (docs-only commit). The suite result cannot change. Re-check that `*.cs.golden` still has no diff.
3. **PR body:** after F261 it is the text that ends up in the history. Take the numbers from the corrected body.
4. **Risks:** (a) `Emit` is still fail-open: the VM analysis runs over the whole module and its errors are ignored, as before (the body notes the 13 of 13 sample domains with errors; H2 owns it). (b) Behavior change you can see: `create_domain_session` and `add` entity now refuse names with whitespace, `/` or `#` (and an empty domain name returns a failure message instead of throwing). (c) `Lower` and `Emit` now throw `InvalidOperationException` for an entity named `DomainResult` or entity pairs `X` / `XStage` (before: C# that did not compile). (d) The ten `catalog.golden` files changed; I checked them line by line. (e) The pre-existing `GetOrLower` race noted under (b).
5. **After merging, check:** `git grep SyntaxModule -- Poly Poly.Mcp src Poly.Benchmarks` is empty; the full suite shows 3069 (3054 + 15); `git diff --stat <merge-base> -- '*.cs.golden'` empty; run the Mcp server once and try `create_domain_session("My Domain")` to see the message.
6. **For Foreman after the merge:** plan line 3 ("Done so far") still lacks N3, C4a and A3a, and the wave lists need A3a with its merge SHA.
