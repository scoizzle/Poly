# Final Boss review: PR 86, tip ec0a5bbf (slice A1, Artifact id)

- PR: scoizzle/Poly#86 "Add ArtifactId: name path plus type, parse, format, equality (A1)", head `ec0a5bbf2dfe995d9b74995410efdaba41d9415d` (re-checked just before filing: unchanged). One commit, `ec0a5bbf` (Scot Murphy, hand-written; no mill implemented it). Base: master `bde1f7c5` (PR 85 squash). CI green.
- Plan card read: `docs/domain-modeling/pipeline-convergence-plan.md` A1 (lines 166-173), section 0 (release rule, hand-edit gate and its three probes), and `pipeline-convergence-plan.decisions.md` V6 (a rename is a new id; same-named stage actions are one artifact, so the stage is not in the id).
- File name: the box clock (`date`) reads 2026-10-03 (CDT), so the files carry that date.
- Mill: OpenCode `opencode-go/deepseek-v4.1-flash`, read-only, stdout-only prompt, `OPENCODE_DB` unset. It completed on the first try (no fallback to `mimo-v2.5` or `opencode/*-free`). Every mill finding was re-checked by hand (see "Mill disposition"). Beyond reading, I ran the code: the full suite, the new tests in four environments, a probe test of 30 edge inputs plus a 200,000-string round-trip fuzz, and 29 mutation tests on a scratch worktree (not pushed, removed).

## Verdict: SHIP (bugs 0; open: 4 suggestions, 3 nits; hand-editability gate = YES)

`ArtifactId` is a 60-line sealed record with a private constructor, so the only ways to get one are `Create` and `Parse`, and both validate. It holds a name path and a type, nothing else (no stage, no body, no hash). It is referenced only by its tests. Format, parse, equality and rejection all behave correctly on every input I tried, and parse and format are exact inverses on 6,840 random valid ids. The four suggestions (F114-F117) tighten what the tests pin and what the class comment claims; none changes a result today. F117 (the class comment describes compile-time derivation that this type does not do) is a one-sentence edit and I recommend making it in this PR. A1 is on the V11 standing-merge list, so SHIP with gate YES means it can merge.

## Checklist

| # | Check | Result |
|---|---|---|
| 1 | Diff is exactly 2 files; type under `Poly/DomainModeling/Compile/`; parse, format, equality, rejection; `git diff --check` | **PASS.** `git diff origin/master...HEAD --stat`: `Poly/DomainModeling/Compile/ArtifactId.cs` (+60) and `Poly.Tests/DomainModeling/Compile/ArtifactIdTests.cs` (+78), 138 insertions, nothing else (no csproj, no docs). `git diff --check`: clean (rc 0). `Create` / `Parse` / `ToString` / record equality all present; malformed ids throw `FormatException` |
| 2 | Tests cover the shapes; mutation-tested | **PASS with gaps (F114-F119).** 7 test methods, 32 cases. Covered: empty id, empty segments (`Hotel//Confirm`, leading and trailing `/`), missing type (no `#`, `Hotel#`, `#method`), extra `#`, space and newline, case (path and type), round trip (4 shapes), equality plus equal hash. Not covered: null inputs, tab/NBSP/other Unicode whitespace, zero-width and control characters, a very long id, Unicode names, hash inequality, `Segments` order. 29 mutations: 23 caught, 6 survive (table below). Behaviour of the untested shapes was checked by a probe and a fuzz and is correct, apart from the permissive character set (F114) |
| 3 | Not wired | **PASS.** `git grep ArtifactId` outside the two new files hits only `docs/domain-modeling/pipeline-convergence-plan.md:29, 169, 172` (the plan itself). No product file, no `Lower`, no `Emit` |
| 4 | Names the method, not a stage body | **PASS.** The type has two strings: `Path` (names joined by `/`) and `Type`. No stage name, no body text, no hash, no content identity; `Create` takes names and a type and nothing else. The plan wording (A1 scope, V6 "the stage need not be in the id") matches. The class comment never says the stage is excluded (F117) |
| 5 | Full suite; other TZ/culture | **PASS: 2978 total, 2978 succeeded, 0 failed, 0 skipped** (1 m 28 s, `dotnet test Poly.Tests -p:NuGetAudit=false`; the switch only bypasses the known SQLitePCLRaw NU1903 advisory). `ArtifactIdTests` (32 cases) run 32/32 under: `TZ=UTC`; `TZ=Asia/Tokyo LC_ALL=de_DE.UTF-8` from cwd `/tmp` (output switched to German, so the culture was live); `TZ=America/Los_Angeles LC_ALL=tr_TR.UTF-8` (Turkish output; Turkish dotless I is the classic case-mapping trap); `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 TZ=Pacific/Kiritimati`. The probe test also ran under de_DE / Tokyo: `"I#m"` is not equal to `"ı#m"`, and no code path calls a culture-sensitive API (`Split(char)`, `string.Join`, interpolation, ordinal record equality) |
| 6 | Hand-editability gate | **YES**, see below |
| 7 | Poly principles | **PASS**, see below |

## A1 card, line by line

| Card line | Status |
|---|---|
| Scope: tiny `ArtifactId`, written name path plus type (`Hotel/Reservation/Confirm#method`), parse, format, equality | Met. The example string from the card is the first test and the first doc example |
| The id names the method, not a stage body; same-named stage actions are one artifact | Met by construction: nothing in the type can hold a stage or a body. The comment does not say so (F117) |
| No wiring | Met (check 3) |
| Files: new file under `Poly/DomainModeling/Compile/` | Met. The test file sits at the mirrored path under `Poly.Tests/` |
| Done when: unit tests for format, parse, equality and rejection of malformed ids | Met: `Create_FormatsNamePathAndType`, `Parse_ThenToString_RoundTrips` (4), `Equality_SameNamePathAndType_IsEqual`, `Equality_DifferentPathTypeOrCase_IsNotEqual` (5), `Parse_Malformed_Throws` (13), `Create_EmptyPath_Throws`, `Create_InvalidNameOrType_Throws` (7) |
| SHIP if referenced only by its tests (`git grep ArtifactId` outside the new files is empty) | Met (docs lines only, no code) |
| NOT SHIP if wired into Lower or Emit | Not the case |
| Hand-edit: yes, one small file | Met, see gate |

## Hand-editability gate

**Answer: YES.** Scot can open `ArtifactId.cs` (60 lines, one type, no reflection, no attributes) and `ArtifactIdTests.cs` (78 lines, TUnit `[Test]` / `[Arguments]`) cold and change them without an agent:
- Names match what they do (`Create`, `Parse`, `ToString`, `RequireValid`, `PathSeparator`, `TypeSeparator`). The two separators are named constants and both parse and error text use them, so changing `#` to something else is a one-line edit.
- No twin path: `Parse` splits on `#` and then calls `Create`, so there is one validation path. No dead guard: every `throw` is reachable and tested (`ThrowIfNull` is reachable but untested, F116). No fallback / legacy / interim branch. The private constructor means no invalid instance can be built without reflection. Build has 0 warnings (hand-edit probe 1); nothing was removed or renamed (probe 2); no fallback branch (probe 3).
- Not perfect: (a) the class comment (`ArtifactId.cs:4-7`) says what the plan will do with ids, not what this type does (F117); (b) `Segments` is a public member nobody but one test line uses (F120).
F117 is the one item that touches "comments claim only what the code does". I judged it a suggestion because the sentence is the plan's own wording (V6: "ids are derived by every compile") and the author is the plan's author, so no reader is misled about what to edit. If you read that gate clause strictly, F117 flips the gate to NO and blocks; the fix is one sentence.

## Principles

No violations. `ArtifactId` is data: no `Main`, no second interpreter, no twin tree, no side table, no lowering, no analysis, no consumer-specific flag. Tenet 7 (no guardrails without real consumers) is about checks and ceremony; the plan names the consumers (A2a catalog, A3b trace-back) and asks for exactly this slice first. Tenet 6 (no speculative framework): the type is a value with two strings; "libraries may add their own types" is simply an open string, not a registry. The one small extra is `Segments` (F120).

## Findings

Severity: bug = wrong or misleading result; suggestion = fix soon, by hand; nit = polish. No bugs.

### Suggestions

- **F114 [suggestion] The accepted character set is "anything except whitespace, `/` and `#`", and that is neither stated nor tested.** `ArtifactId.cs:54-59`. Probe results (accepted, `OK`): NUL (`\u0000`), ESC (`\u001B`), zero-width space (`\u200B`), BOM (`\uFEFF`), a lone surrogate (`\uD800`), `..`; also Unicode names such as `Hôtel/Réservation/確認#méthode` and emoji (these are fine). Accepted ids still round-trip (fuzz below), but a name with an invisible or control character reads the same as the clean one in a catalog dump, a file name or a diff, and `Parse` echoes the raw text into its `FormatException` message (`:48`), so a control character can reach logs. The `///` on `Create` says "contains whitespace, `/` or `#`" and so is accurate; it just never says the rest is allowed. Two honest options: (a) reject `char.IsControl(c)` and format characters (`CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format`, which covers `U+200B`, `U+FEFF`) and surrogates that are not part of a pair; or (b) keep it permissive and add one test and one sentence ("any characters except...; names come from the DSL, which has already restricted them"). Pick before A2a stores these in a catalog.
- **F115 [suggestion] `Segments` order is not pinned.** `ArtifactIdTests.cs:12`: `Assert.That(id.Segments).IsEquivalentTo(new[] { "Hotel", "Reservation", "Confirm" })`. TUnit's `IsEquivalentTo` ignores order by default, so mutation M16 (`Segments` returns the names reversed) passes all 32 cases. Fix: assert order (`CollectionOrdering.Matching`, or compare `string.Join("/", id.Segments)` to `"Hotel/Reservation/Confirm"`), and add one `Segments` check after `Parse` of a single name (`Hotel#module` gives one segment).
- **F116 [suggestion] Null inputs are untested, and null elements behave differently from a null list.** `Parse(null)` and `Create(null, ...)` throw `ArgumentNullException` (probe), but no test says so: mutations M11 (`path ?? []`) and M12 (`text ??= ""`) pass 32/32. A null element in the list, or a null `type`, throws `FormatException` (probe; `RequireValid` takes `string?` and treats null as empty), and its message prints `''`. That is defensible, but it is a second null convention in a 60-line file and it is only visible by reading `RequireValid`. Fix: add `[Test]`s for `Parse(null)`, `Create(null, "m")` (`ArgumentNullException`) and `Create([null!], "m")`, `Create(["a"], null!)` (pin whichever you intend), and say in the `Create` summary that a null name or type is also a `FormatException`.
- **F117 [suggestion] The class comment describes the plan, not the type.** `ArtifactId.cs:6-7`: "Ids are derived on every compile from the names in the domain, so renaming an element gives a new id. The type is an open string: libraries may add their own." Nothing in this type, or anywhere in the PR, derives ids or lets a library add a type; the first sentence is V6's decision and the second is true only in the weak sense that any valid string is accepted. It also does not say the thing the A1 card is about: the stage is not part of the id. Suggested replacement for lines 6-7: "Holds only the names and the type: no stage, body text or hash, so same-named stage actions are one artifact. The type is any valid string, so a library can use its own." Move the derive-per-compile sentence to the slice that derives (A2a).

### Nits

- **F118** The whitespace tests use only a space and `\n` (`ArtifactIdTests.cs:53-57`). Narrowing the check to `c == ' ' || c == '\n'` (M10b) still passes 32/32, so tab, NBSP (`U+00A0`) and `U+3000` are unpinned. The code is right (probe: all three throw); add `[Arguments("Hotel\tConfirm#method")]` and `[Arguments("Hotel\u00A0Confirm#method")]`. A whitespace-only segment (`a/ /b`) is also correct (probe) and unpinned.
- **F119** The equality test pins only that equal ids have equal hash codes (`:31`). A constant `GetHashCode` (M7b) and a path-only hash (M7c) survive. Equal-hash is the contract, so this is not a defect; a one-line `parsed.GetHashCode() != other.GetHashCode()` check on two different ids would catch a degenerate hash before A2a uses ids as dictionary keys. (Identity hash, M7, is caught.)
- **F120** `Segments` (`ArtifactId.cs:25`) is a public member with no caller except one test line, it is not in the card (which lists parse, format, equality), and it re-splits `Path` into a new array on every access (probe: `ReferenceEquals(a.Segments, a.Segments)` is false). Cheap and harmless; either cut it until a slice needs it, or store the segments at construction. If kept, fix F115 so the one test it has means something.

## Evidence

### Mutation tests (scratch worktree of `ec0a5bbf`, never pushed, removed)

Each row: one edit to `ArtifactId.cs`, rebuild, run the 32 `ArtifactIdTests` cases.

| Mutation | Result |
|---|---|
| M1 `parts.Length != 2` to `< 2` | caught (1 fail: extra `#`) |
| M2 drop `char.IsWhiteSpace` | caught (6) |
| M3 drop the `/` check | caught (2) |
| M4 drop the `#` check | caught (2) |
| M5 `ToString` as `Type#Path`; M5b `ToString` with `-` | caught (5, 5) |
| M6 equality ignores case (`Equals`/`GetHashCode` on `ToString()`, OrdinalIgnoreCase) | caught (2) |
| M7 hash is the object-identity hash | caught (1) |
| M7b constant hash; M7c path-only hash | **survive** (F119) |
| M7d `Equals` compares path only (+ hash on path); M7e type only | caught (2, 3) |
| M8 remove the empty-path guard | caught (1) |
| M9 allow empty parts | caught (7) |
| M10 whitespace narrowed to `' '` | caught (1: the `\n` row) |
| M10b narrowed to `' '` or `'\n'` | **survives** (F118) |
| M11 null path becomes empty; M12 null text becomes `""` | **survive** (F116) |
| M13 no type validation | caught (7) |
| M14 no name validation | caught (10) |
| M15 `Parse` does not split the path on `/` | caught (9) |
| M16 `Segments` reversed | **survives** (F115) |
| M16b `Segments` unsplit; M16c `Segments` first two only | caught (1, 1) |
| M17 `Parse` trims | caught (4) |
| M18 type lower-cased; M19 path lower-cased | caught (2, 6) |
| M20 path joined with `.` | caught (4) |
| M21 constructor swaps path and type | caught (5) |

(The `Equals` mutations were written as non-virtual `Equals(ArtifactId?)` because the type is sealed; my first attempt used `virtual` and failed to compile, so it is not counted.)

### Probe test and fuzz (scratch, deleted)

- `Parse(null)`, `Create(null, "m")` throw `ArgumentNullException`; `Create([null], "m")` and `Create(["a"], null)` throw `FormatException`.
- Rejected (`FormatException`): tab, NBSP, `U+3000`, whitespace-only segment, `a/#m`, `#a/b`, `a/b#`, `#`, `a##m`, `a#m/x`.
- Accepted: ZWSP, BOM, NUL, ESC, lone surrogate (F114); `Hôtel/Réservation/確認#méthode`; `a/😀#m`; `../a#m`.
- Very long: a 1,000,000-character name parses and formats (length 1,000,002); 100,000 segments build and `Segments.Count` is 100,000.
- Equality: equal ids `Equals`, `==` and equal hash; case differs gives not equal with different hashes; `e` + combining acute is not equal to precomposed `é` (ordinal; fine for names from one source); `a with { }` equals `a`; `ToString()` is `a/b#m`.
- Fuzz: 200,000 random strings (ASCII-heavy and full BMP); 6,840 parsed. For each: `Parse(id.ToString())` equals `id`, `id.ToString()` equals the input, and `Create(id.Segments, id.Type)` equals `id` with the same hash. 0 failures.

## Mill disposition

Accepted after hand checks: finding 2 (zero-width and control characters accepted, F114; the mill's tab argument was muddled, since tab is whitespace and is rejected, and I confirmed the real survivor is M10b, F118) and its parse-message note (finding 9, folded into F114); finding 4 (null coverage, F116; confirmed by M11/M12, and I verified the null element / null type behaviour in the probe); finding 7 (comment overclaim, F117; the mill called it a bug, it is a suggestion because the sentence is the plan's own decision wording); finding 6 (`Segments` allocates, F120, nit).
Downgraded or dropped: finding 1 labelled "bug" is self-contradictory and describes no defect (`Hotel#method` is a valid id by design, the type is an open string, equality is ordinal as the doc says); dropped. Finding 3 labelled "bug" says `Segments` returns a fresh array; true but not a bug (`Segments` is not part of equality; `with` cannot change the get-only properties); folded into the F120 nit. Finding 5 (culture invariance "untested") is not a finding: I ran the tests under de_DE, tr_TR and invariant globalization and no code path is culture-sensitive. Finding 8 says "fine" and is not a finding. The mill's mutation list items 4-6 are not live mutations (record equality cannot be mutated by editing source as it described; the separator-swap case is caught by the round-trip test).
Missed by the mill: `Segments` order unpinned (M16, F115), the hash-quality survivors (F119), all mutation evidence, the full-suite and environment runs, the fuzz.
