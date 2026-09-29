# PR #82 Final Boss, round 6: walk a navigation's target on that hop (2026-09-29)

- **Target**: PR #82 @ `d7c1da486776a4a7f38fd0440ae91fdee0303b8f`. One hand commit, "Walk a navigation's non-navigation target on that hop", on top of `1312e586` (round 5: NOT SHIP for F48 only).
- **Mill**: Grok Build (`grok-4.6`, `--reasoning-effort high --always-approve`, read-only prompt). It exited 0, wrote nothing, and reported no new finding. That agrees with the probes below, which were run independently.
- **Verdict: SHIP.** F48 is closed. Nothing regresses against 1312e586 or master, nothing breaks the PR contract, and no new finding exists (F49 unused).
- **Bugs: 0.** Open, pre-existing, non-blocking: F46, F47, F42, F44, F7, F41, F4 remainder. Accepted nit: F40.

## The diff (claims verified)
`git diff 1312e586 d7c1da48` touches exactly 2 files, +77/-16:
- `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`
  - `CollectValueHops(operand, subject, subjectPath, hops, seenPaths)` now threads the subject (`:434-447`).
  - `Comparison` seeds it with `_currentSubject` and `""` (`:409-410`).
  - `CollectHopChain` ends with `CollectValueHops(rn.TargetProperty, hop, hopPath, …)` (`:461`), replacing the old `if (rn.TargetProperty is RelationshipNavigation next)`.
  - The doc comment (`:417-433`) is corrected.
- `Poly.Tests/DomainModeling/Lowering/UnlinkedComparisonAgreeTests.cs:258-314`: one test with 6 `Arguments`.
- Nothing else changed, which matches "nothing else changed".

## 1. Foreman's ask: which `Age` does the where-body read?
Values: **self (Customer) Age 50, advisor Age 10, advisor's mentor Age 20**, plus an unrelated outer `mentor: Mentor` on Customer with Age 999 (the `outerMentor` variant). All rows are advisor + mentor linked. Each row ran on head, 1312e586 and master; **sim, print and master were identical on every row**.

| Policy | Value if bare `Age` = advisor's | If `Age` were self's | Head sim / print | 1312e586 | master | Correct? |
|---|---|---|---|---|---|---|
| `(advisor where Age + mentor Age) is 30` | 10+20 = 30 → **True** | 50+20 = 70 | True / True | True / True | True / True | yes, advisor's Age |
| `(advisor where Age + mentor Age) is 70` | False | True | False / False | same | same | yes |
| `(advisor where Age + mentor Age) is 40` (mentor+mentor) | False | | False / False | same | same | yes |
| `(advisor where Age + mentor Age) is 1009` (advisor + outer mentor) | False | | False / False | same | same | yes: the outer mentor is never read |
| `(advisor where mentor Age * 2) is 40` | 20*2 → **True** | | True / True | same | same | yes |
| `(advisor where mentor Age * 2) is 1998` (outer mentor 999*2) | False | | False / False | same | same | yes: the mentor is the advisor's |
| `Age < (advisor where Age + mentor Age)` | 50 < 30 → **False** | 50 < 70 → True | False / False | same | same | yes; a self read would flip it to True |
| `Age < (advisor where mentor Age * 2)` | 50 < 40 → **False** | | False / False | same | same | yes |
| `(advisor where Age + mentor Age) < Age` | 30 < 50 → **True** | 70 < 50 → False | True / True | same | same | yes; a self read would flip it |

Every row was also run with and without the unrelated outer `mentor`, and the results are identical.

**Conclusion.**
- Lowering, simulate and print all read the **advisor's** `Age`, and the mentor is `Advisor.Mentor`.
- `RelationshipNavigation` lowering re-roots the whole target on the hop via `Route(target, NullForgiving(relMember))` (`:180`). That is unchanged since master `9db8868f`, so master already read the advisor's Age.
- The guard walk does not choose an `Age`: a bare `PropertyAccess` adds no hop. Only navigations contribute guards, so the walk cannot pick the wrong entity.

## Guard subject (printed C#, head)
The inner hop is rooted at the advisor, not the customer, with and without an unrelated outer `mentor`:
```
(advisor where Age + mentor Age) is 30 →
  this.Advisor != null && (this.Advisor.Mentor != null && this.Advisor!.Age + this.Advisor!.Mentor!.Age == 30L)
(advisor where mentor Age * 2) is 40 →
  this.Advisor != null && (this.Advisor.Mentor != null && this.Advisor!.Mentor!.Age * 2L == 40L)
Age < (advisor where Age + mentor Age) →
  this.Advisor != null && (this.Advisor.Mentor != null && this.Age < this.Advisor!.Age + this.Advisor!.Mentor!.Age)
```
The guard is `this.Advisor != null && this.Advisor.Mentor != null`, never `this.Mentor`.

On 1312e586 the mentor guard is absent (`this.Advisor != null && this.Advisor!.Age + this.Advisor!.Mentor!.Age == 30L`), which was F48.

Link-state results:
- Head, all 40 AGE rows: correct on sim and print, across none / advisor-only / both and with/without outer mentor.
- 1312e586: 8 of the 40 throw or NRE (advisor linked, mentor unlinked, all four policies). Those 8 are the F48 rows.
- Master: 16 of the 40 throw or NRE.

## 2. New tests on 1312e586, and scope of the diff
Only head's `UnlinkedComparisonAgreeTests.cs` was copied onto 1312e586.
- `HopInsideNavigationTargetArithmetic_AgreesOnSimulateAndPrintedCsharp` **fails 2 of 6** there: the two advisor-linked / mentor-unlinked cases, "Member 'Age' requires a non-null instance".
- The other 4 cases and the other 7 tests in the class pass.
- So the both-linked and none rows are not discriminating, as 100x noted. The value of this test is the mentor-unlinked row.
- The both-linked rows use advisor 25, mentor 30, customer 20, so a self-Age read would give the same True; that is why the probe above uses different Ages.

## 3. Sweep: 566 unique rows × 3 revisions
The round-5 matrix, rebuilt from the retained harness (comparison operands, arithmetic, not/and/or nested and mixed, quantifiers, binders, condition navigations, exists, is-not, temporal, count, policy roots, require, action if/assign/create, entry, subscription bodies, F46 roots and where/quantifier bodies, collection hops, F42/F44/F7), plus the new AGE rows. Each row is simulate vs printed C# vs the hand-computed value, with unrelated same-named relations where meaningful. Plus 4 require-guard rows (F47), identical on all three.

| Revision | OK | wrong/throw | rejected | no expectation |
|---|---|---|---|---|
| head d7c1da48 | 460 | 68 | 33 | 5 |
| 1312e586 | 446 | 82 | 33 | 5 |
| master 9db8868f | 293 | 235 | 33 | 5 |

- **Head vs 1312e586:** 0 regressions, 0 rows changed for the worse, **14 rows fixed**. All 14 are the F48 shapes: 8 AGE, 4 `WBODY2` and 2 `WBODY`, each the advisor-linked / mentor-unlinked state, in plain and outerMentor variants.
- **Head vs master:** 0 regressions, 167 rows fixed.
  - 5 rows differ from master while both are wrong for the same reason.
  - 4 of those are the BINDER/F7 row `if (Ready is ((order Total > Balance) and not advisor Active))`: both sides fail because `Balance` is not an Order property (a probe-model error, same on both; only the C# error column differs).
  - 1 is `Ready is (advisor where Active or mentor Active)`, where the `or` ends the `where` body. Head gives False per the contract with the outer mentor unlinked; master threw. My expected value there is the artifact.
- The remaining 68 non-OK rows are the carried pre-existing families, identical on 1312e586 and master: F46, F44, F42, F7, plus the assign/create value contexts.

## 4. F8 mutations at head (each reverted)
- Unconditional `hops.Add`: `HopReachedTwice_IsGuardedOnce_AndIsFalseWhenInnerHopUnlinked` fails ("Expected to be 1").
- Forward fold loop: **3 tests fail**, all "Member 'Mentor' requires a non-null instance": `MultiHopValuePath_UnlinkedInnerHop_IsFalse_OnSimulateAndPrintedCsharp`, plus the two new `HopInsideNavigationTargetArithmetic…` rows in the none state (advisor unlinked), one per policy. The new tests therefore also pin fold order.

## 5. Comment vs code, and the gate
- The comment at `:417-433` is **honest**, and the overclaim from round 5 is gone.
  - It now says the walk reads from a subject (the comparison's, or the hop a navigation's target is read on).
  - It says `advisor mentor Age` and `advisor where mentor Age * 2` both yield `.Advisor` and `.Advisor.Mentor`.
  - It says the walk stops at a binder name and at a navigation to a condition, whose own hop lowering guards, "and hops inside the condition are guarded by the comparisons there".
  - The last claim is accurate for comparisons; bare boolean leaves under and/or/not stay F46. Its example is a comparison, so it does not overclaim.
  - The F42 paragraph is unchanged and still accurate.
- **Hand-edit gate: YES.**
  - `CollectValueHops` is still a two-arm switch.
  - `CollectHopChain` is three stops, record, then one recursive call with the hop as subject.
  - The new test copies the existing store-plus-export agreement pattern.
  - Scot can open these two files cold and change a stop, an arm or a row without an agent.

## 6. Suite, CI, merges
- **Full suite:** 2921/2921 passed (matches the claim).
- **CI:** `Build & test` passed, run 36525988098.
- **Merge-trees, both clean (rc 0):**
  - vs PR 83 `c22657404de3af5575ffdceb35716e0aea28f04c` → tree `9a2c2f6d58a9fd946408a6a91ae6693ef9a052fd`.
  - vs master `9db8868ff6086e6050c654b409ed75aec972edb8` → tree `5dd56b4a76b6818b06bb7688dff4c7e384d2a3ff`.

## Carried list (all unchanged; pre-existing unless noted)
| Id | Path:line | Status |
|---|---|---|
| F46 | DomainExpressionLoweringPass.cs:180-182 | Open, DOES-NOT-BLOCK: bare boolean or value leaves outside a comparison at their own scope (root, `where`-body, quantifier-body, `assign`/`create` values). Identical on master. |
| F47 | DomainToCSharpExporter.Actions.cs:908-932 | Open, DOES-NOT-BLOCK: the require-guard walk recurses into quantifier bodies with the outer subject. File not in this PR. |
| F42 | DomainExpressionLoweringPass.cs:158-164, :452 | Open, DOES-NOT-BLOCK: binder matched at any depth. Documented as a known issue. |
| F44 | DomainExpressionLoweringPass.cs:258-259, :174-177 | Open, DOES-NOT-BLOCK: `IsCollectionNav` is outer-scoped. Loud throw only. |
| F7 | DslExpressionParser.cs:190-199 | Open, DOES-NOT-BLOCK: `FoldPathCmp` scopes the right-hand side. Parked. |
| F41 | StructuralDomainAnalyzer.cs:69-74 | Open, DOES-NOT-BLOCK: enum-only check. |
| F4 remainder | tests | Open, DOES-NOT-BLOCK: missing allow-case tests. |
| F40 | UnlinkedComparisonAgreeTests.cs:204-205 | Accepted nit. |

Closed: F48 (`:461`, test `:258-314`), F45, F43, F37, F36, F8, F1-F3, F5, F6. New findings: none.
