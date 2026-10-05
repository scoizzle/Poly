# Final Boss re-check: PR 99 (N5) at ec71ff0f

- **PR:** https://github.com/scoizzle/Poly/pull/99, "N5: MCP add and create_domain_session use the DSL name rule".
- **Head:** `ec71ff0f5f14ab2938117743780a35bba177e84c`, verified with `gh pr view` and `pull/99/head`. There is one fix commit on top of `7fec146c`, "A property named Text, Number or Boolean needs a primitive type in MCP add too". It touches 6 files (+90/−31).
- **Master:** `dd7d4204` (T1 merged). The PR is BEHIND master. `git merge-tree` against it is clean, and the merged tree was tested.
- **Prior verdict:** NOT SHIP at `7fec146c` (comment 5987996527, branch `review/finalboss-pr99-7fec146c`). Blockers were F300 and F301.
- **Mill:** OpenCode `opencode-go/deepseek-v4.1-flash`, read-only from a throwaway snapshot. Every finding it reported was spot-checked.
- **Clock:** 2026-10-05, 07:05–07:25 CDT.

## Verdict: SHIP. Hand-editability gate: YES

Both blockers and all the same-pass wording items are fixed. The remaining items are latent or polish (F340–F344), and none of them blocks.

## Fix list

| Item | Result |
|---|---|
| **F300** (blocker) | **Closed.** See the details after this table. |
| **F301** (blocker) | **Closed.** The test now checks that no session carries the refused domain name (`McpSessionStore.ListSessions().Any(... state.Domain.Name == name)`) and has a comment explaining why. `McpNameRuleTests` (now 194 tests) passed in **15/15** runs on its own, where it had failed 2/5 to 8/8 before. It also passed in **3/3** full-suite runs under parallel load. |
| F302 | **Closed.** The tests are renamed to `Add_Entity_WithNameTheDslRefuses_Fails` and `CreateDomainSession_WithNameTheDslRefuses_Fails`. |
| F306 | **Closed.** PR body rows 9–13 are now true, including the new row for value and entity types and the blank-name row. |
| F307 | **Closed.** The `add` description and `InvalidNameMessage` now state the property exception and its type condition. |
| F303, F309, F310 (nits) | **Closed.** A leading `_` is pinned (`NameStartingWithUnderscore_IsAcceptedByAddAndByTheDsl`). `Boolean` is now tested. The `kind` parameter lists all 13 kinds. |
| F304, F305, F308 | Unchanged. They are cards for Foreman and do not hold this PR. |

### F300 details

- **One shared rule.** `DslGrammar.IsPrimitiveType(kind, text, isKnownPrimitiveName)` is now used by both:
  - the parser's `primitive-name` branch;
  - `DslTokenReader.IsPropertyName(name, typeName, isKnownPrimitiveName)`.
- **MCP's known-primitive list** is the session domain's `PrimitiveType` names. The check runs after the missing-`typeName` check and before `Evolve`.
- **Parser restructure.** The `primitive-name` branch is behavior-identical to before: the guard now throws first, and the remaining `else` is the old "identifier and known primitive" branch. The mill agrees.
- **Probe matrix.** I compared MCP `add property` with DSL `apply_dsl`, and re-applied every accepted export. The domain had a value type `Money`, an enum `Color` and an entity `Line`. Names: `Text`, `Number`, `Boolean`, `Okay`, `_x`, `stage`, `1x`, `a-b`, `Date`, `Uuid` (10). Types (15): `Text`, `Number`, `Boolean`, `Date`, `DateTime`, `Duration`, `Uuid`, `Binary`, `Money`, `Color`, `Line`, `Bogus`, `text`, `1x`, `many Line`. Results across the 150 cases:
  - `Text`/`Number`/`Boolean` with `Money`, `Color`, `Line` or `Bogus` are refused by both sides.
  - `Text`/`Number`/`Boolean` with a primitive type are accepted by both, and the export re-applies.
  - Keywords and malformed names are refused by both.
  - The only divergence is not about name syntax (F340).
- **Mutations** (scratch, reverted):

| Mutation | Red |
|---|---|
| MCP ignores the type (known-primitive check always true) | 2 |
| `DslGrammar.IsPrimitiveType` accepts any identifier type | 2 |
| MCP knows no primitive names | 1 (`Text: Date`) |

## Evidence

- **Full suite at the tip:** **3267 total, 0 failed, in each of 3 runs.** That is base `89098229`'s 3073 plus 194 `McpNameRuleTests`.
- **Merged with master `dd7d4204`:** **3276 total, 0 failed.**
- **Goldens:** `git diff --stat origin/master ec71ff0f -- '*.golden'` is empty. The `POLY_UPDATE_GOLDEN=1` regeneration run on the merged tree was stopped on Foreman's instruction and wrote nothing (`git status` clean). It came out clean at `7fec146c`, and this fix changes no printer code.
- **Cultures:** not re-run, by Foreman's instruction to finish. The `7fec146c` sweep (UTC, Tokyo + de_DE, LA + tr_TR, invariant) was clean apart from the old F301 flake. The fix adds only ordinal and keyword comparisons, which do not depend on culture.
- **Not affected by this commit:** the scanner and `ArtifactId`. The scanner's differential proof and `ArtifactId`'s byte-identical restore were verified at `7fec146c`, and this commit does not touch the scanner or `ArtifactId.cs`. The scanner helpers in `DslTokenReader` are unchanged; only `IsPropertyName` changed.

## Hand-editability gate: YES

- The rule lives in one place (`DslGrammar.IsPrimitiveType`), and both callers name it.
- The comments are true:
  - the `IsPropertyName` doc gives the `Number: Text` / `Text: Date` / `Text: Money` examples, which I checked;
  - the parser comment points to the MCP twin;
  - the F301 test comment explains the race.
- There are no dead guards.
- Two smaller items are polish: a confusing pair of names (F342) and a message that is true but over-long (F343).

## Findings (new, none blocking)

| ID | Sev | Blocks | Where | What |
|---|---|---|---|---|
| F340 | S (pre-existing, card) | no | `add property` with an entity `typeName` | `add property` named `Date` or `Uuid` (or any existing type name) with entity type `Line` is accepted. Export prints `Date: Line`, which the DSL reads as a navigation, and re-applying fails with "Relationship name 'Date' collides with a type of the same name". This is not name syntax (`Okay: Line` round-trips). It is an evolution invariant that only the DSL's navigation path enforces, because an MCP property with an entity type is not a relationship. It exists before this PR (any name was accepted on master). Related to F264: `add property` with an entity type should either be refused or become a relationship. |
| F341 | S (latent, card) | no | `DomainTools.cs:542` vs `PolyDslParser.IndexPrimitiveNames` | Mill F1. MCP's known-primitive list comes from the domain's `PrimitiveType` set, while the parser builds its list from the catalog, the session seeds and the `uses` seeds. Today they are equal in every reachable state (canonical + temporal; probe and mill agree). They would drift if a future `ProductAuthoring` library adds `PrimitiveSeeds` that the bootstrapped domain lacks (`McpSessionStore.Create` builds with `ProductLanguage`, then sets `Extensions = ProductAuthoring`). Consider feeding both from one source. |
| F342 | N | no | `PolyDslParser.cs:283-285` | Mill F5. `DslGrammar.IsPrimitiveType(kind, text, known)` sits next to the parser's private `IsPrimitiveType(kind)`, which duplicates `DslGrammar.IsPrimitiveTypeKind`. Replacing the private method with `DslGrammar.IsPrimitiveTypeKind` would remove the confusion. |
| F343 | N | no | `InvalidNameMessage` | Mill F6. The property-only parenthetical is shown for every kind and for `create_domain_session`. It is true but irrelevant there. |
| F344 | N | no | `McpNameRuleTests.cs:134-135`, `:52-61` | Mill F7/F8. The export text is read by reflection on an anonymous type. The parity check uses `ApplyDsl().Success` over resolvable types only. For an identifier name with an unknown type, `IsPropertyName` is true and the DSL refuses, but `add` still fails with "references unknown type", so the user-visible outcome matches. |

Bugs introduced: none.

## Mill disposition (`opencode-go/deepseek-v4.1-flash`)

| Mill | Disposition |
|---|---|
| F1 primitive set from two sources | Confirmed as latent; no divergence today → F341 |
| F2 identifier name ignores the type | True, but the outcome is the same (refused by analysis); folded into F344 |
| F3, F4 sets equal today; restructure behavior-identical | Agree |
| F5 twin `IsPrimitiveType` names | Confirmed → F342 |
| F6 message parenthetical on all kinds | Confirmed → F343 |
| F7, F8 reflection read; parity check is apply-level | Confirmed → F344 |
| F9 F301/F302/F303/F307/F309/F310 fixed | Agree. F306 is fixed in the PR body (verified with `gh`). |

## Merge notes for Scot

- **Standing-merge eligible**, exact tip SHA `ec71ff0f5f14ab2938117743780a35bba177e84c`.
- BEHIND master `dd7d4204` is fine: merge-tree is clean, and the merged tree passes 3276/0. Use update-branch or squash at merge.
- **Risk:** low. MCP clients can no longer create names that are digit-first, contain `-` or `.`, or are keywords, and can no longer name a property `Text`/`Number`/`Boolean` with a non-primitive type. That is intended.
- **After merging, check:**
  - `git grep IsValidPart` is empty;
  - the suite count is master + 194 with 0 failures;
  - `create_domain_session("1Domain")` is refused;
  - `add property` `Text: Money` is refused and `Text: Date` is accepted.
- **Foreman cards:** F304 (printer `NeedsQuotes`), F305 (contract `Success=false`), F308 (N5 card in the plan; F263 closed when this merges), F340 (with F264), F341.
