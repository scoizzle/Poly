# Final Boss re-check: PR 98 (T1, ValidDomain fixture) at 74ed1ddf

- **PR:** https://github.com/scoizzle/Poly/pull/98, "T1: hand-built test domains declare the core primitives (ValidDomain fixture)".
- **Head:** `74ed1ddfb2f72b85b2a372ef4392a7e7d28ecadf`, verified with `gh pr view` and `pull/98/head`. It contains two commits on top of the reviewed `1b79fdb7`:
  - `4905db66`: the `ValidDomain.cs` doc and a new `ValidDomainTests.cs`;
  - a merge of master `076a9014`.
- **The merge commit is exactly what git would produce.** The tip tree equals `git merge-tree 076a9014 4905db66`, with an empty diff.
- **Master:** `076a90143aa1`, unchanged at filing. `git merge-tree` of the tip against master is clean, and `mergeStateStatus` is CLEAN.
- **Diff vs master:** 13 files. That is T1's 11 test files plus `ValidDomain.cs` and `ValidDomainTests.cs`. Only test code is touched.
- **Prior verdict:** NOT SHIP at `1b79fdb7` (comment 5988488141), with blocker F320.
- **This pass:** a short re-check of the fix list (F320–F323). It was taken over from a crashed worker; its notes are kept outside the repo.
- **Mill:** OpenCode `opencode-go/deepseek-v4.1-flash`, read-only. Every finding it reported was spot-checked.
- **Clock:** 2026-10-05, about 00:25–00:45 CDT.

## Verdict: SHIP. Hand-editability gate: YES

The class doc is rewritten and every load-bearing sentence is true. `ValidDomainTests` pins the fixture and goes red when `Text` is dropped. The code is unchanged, and the `.Where(ExtensionCatalog.Core.Contains)` filter is kept. Two new nits (F330, F331) are worth a later touch-up; neither blocks.

## Fix list

| Item | Result |
|---|---|
| F320 (gate): the doc must not claim "analyzes cleanly" | **Closed.** The claim is gone. The doc now says the fixture declares primitives and "validates nothing else"; tests may pass invalid content on purpose and then get analysis errors. See F330 for one imprecise clause. |
| F321: `ValidDomainTests` | **Closed.** There are 3 tests: the 5 canonical types analyze clean (the test also pins the catalog to exactly Boolean/Number/Text/Uuid/Binary); `Text`+`Date` with temporal is clean; `Text`+`Date` without an extension gives exactly `Property 'P1' references unknown type 'Date'.` |
| F322: document that a non-Core id adds no primitives | **Closed.** "An extension id outside `ExtensionCatalog.Core` is recorded but adds no primitives, as `AddDomainExtensionChange` does." This is true (`DomainChange.cs:684-685`). |
| F323: say "built in code" precisely; extensions default to none | **Closed.** The doc names `new Domain(..)` / `DomainTestFactory`, which add no primitives (`DomainTestFactory.cs:15-38`). "Unlike `DomainFactory.Create`, extensions default to none" is also true: `DomainFactory.Create` defaults to `ExtensionCatalog.ProductLanguage` (`DomainFactory.cs:24,37`). |
| Keep `.Where(ExtensionCatalog.Core.Contains)` | **Kept.** `git diff 1b79fdb7 4905db66 -- ValidDomain.cs` touches only the `<summary>` (+9/−4). The code is byte-identical. |

### Sentence check of the new doc

1. "Builds a domain with the primitives a parsed domain starts with: the canonical built-ins plus the primitive seeds of each core extension listed." True for the case it describes: a domain parsed with those extensions starts with `CanonicalBuiltInTypeCatalog.CreateChanges()` plus the session's library `PrimitiveSeeds` (`PolyDslParser.EnsurePrimitivesOnce`, `:133-146`). One nuance is in F331.
2. "A domain built with `new Domain(..)` or `DomainTestFactory` has none, so Analyze reports 'unknown type 'Text''." True.
3. "It validates nothing else: a test may pass invalid types or relationships on purpose, and the result then has analysis errors." True for what `ValidDomain` adds, but imprecise. Relationships go through `DomainTestFactory.Create`, which throws `ArgumentException` for a relationship whose source entity is not in `types`. A probe confirmed it: `ValidDomain.Create("T", [], [R: Ghost→Item])` throws. In that case there is no result and no analysis error. That check is the documented fail-closed behavior of the factory the doc cross-references, and it matches the fix-list wording ("does not validate"), so I file it as a nit (F330), not a gate failure.
4. "An extension id outside `ExtensionCatalog.Core` is recorded but adds no primitives, as `AddDomainExtensionChange` does." True.
5. "Unlike `DomainFactory.Create`, extensions default to none." True.

`ValidDomainTests`:
- The comment "Extensions default to none, so a temporal type is unknown until the test imports temporal" is true.
- The test names describe what they assert.
- There are no dead branches.

## Evidence

- **Mutation**, in a scratch worktree, reverted and never pushed: the fixture drops `Text` (`.Where(p => p.Name != "Text")`). **All 3 `ValidDomainTests` go red.**
- **`ValidDomainTests` across environments:** 3/3 pass under UTC, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, and `TZ=Asia/Tokyo` + `de_DE.UTF-8`.
- **Full suite at the tip (merged with master), built with `-m:1`:** **3082 total, 0 failed.** That is master's 3079 plus the 3 new tests. No OOM.
- **Unchanged since the prior pass, not re-run:** the T1 waiver probe (115 → exactly the 10 waived), goldens clean, and no new culture failure in the 11 T1 files. Commit `4905db66` touches only the fixture doc and adds the new test file.

## Findings (new)

| ID | Sev | Blocks | Where | What |
|---|---|---|---|---|
| F330 | nit | no | `ValidDomain.cs:11-12` | "It validates nothing else … the result then has analysis errors" leaves out that `DomainTestFactory.Create` throws `ArgumentException` for a relationship whose source entity is not in `types` (probe-confirmed). Suggested text: "It adds no checks of its own (`DomainTestFactory` still throws for a relationship whose source entity is missing); other invalid types or relationships give analysis errors." Found independently by the mill (its F1). |
| F331 | nit | no | `ValidDomain.cs:7-8` | "the primitives a parsed domain starts with" assumes the domain lists its extensions. A source with no `uses` is parsed with the session's seed extensions (`DomainSession.ForSource`, `:118-121`), so it can carry temporal primitives that `ValidDomain` (default none) does not. The doc's later "extensions default to none" sentence keeps the overall meaning right. Mill F4. |

Still open from the prior pass (unchanged, non-blocking):
- F324: no action.
- F325: record the waived 10 and their owners in the plan's T1 card at merge (Foreman docs).

## Mill disposition (`opencode-go/deepseek-v4.1-flash`)

| Mill | Claim | Disposition |
|---|---|---|
| F1 | "validates nothing else" is inaccurate (`DomainTestFactory` throws for an orphan source) | Confirmed by probe → F330 (nit, see the sentence check) |
| F2 | Test 3 pins the exact message and diagnostic set | Dropped: the fix list asked for "reports unknown type 'Date'", and an exact pin is intended. A wording change should update this test. |
| F3 | The five-name catalog assertion fails if the catalog grows | Dropped: it is the deliberate anchor from the fix list (it keeps the test from passing trivially by asking the catalog what to check). Growing the catalog should be a conscious edit. |
| F4 | A parsed no-`uses` domain gets session seeds | Confirmed → F331 (nit) |
| Q4 | Only the doc changed in `ValidDomain.cs` | Agrees |

Bugs found: none.

## Merge notes for Scot (not V11; you merge by hand)

- `mergeStateStatus` is CLEAN. The tip already contains master `076a9014`, and the merge commit equals the merge-tree result. No rebase is needed. A squash merge gives T1's 11 test files plus `ValidDomain.cs` and `ValidDomainTests.cs`, and no product code.
- **Risk:** none for the product (test-only). The only behavior change is in the tests: the waived 10 now fail for their stated analysis errors, as accepted in the prior pass.
- **After merging:**
  - the full suite should be master + 3 (3082 on today's master) with 0 failures;
  - `git grep "analyzes cleanly" Poly.Tests/TestHelpers` should find nothing.
- **Optional touch-up later:** F330 and F331 (doc wording, one clause each).
- **Foreman:** F325, record the waived 10 and their owners in the plan T1 card.
