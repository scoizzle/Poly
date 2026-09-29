# PR #82 re-verify: P3-C retire BindThis and RewriteVoidFailClosedThrow (Final Boss), 2026-09-28

- **Target**: PR #82 @ `0e03bcf3fc7b574ae43159d7fa948015d56a4fd9`. This is one hand commit, `0e03bcf3` "Follow only the to-one hop chain from the comparison's subject", on top of `754d7e72` (reviewed NOT SHIP for F43). The head did not move before filing.
- **Mill**: Grok Build, `grok-4.6 --reasoning-effort high --always-approve`, READ-ONLY prompt. The implementation lineage is OpenCode-majority, so this is the opposite mill. Every mill claim was checked by probe. The scratch worktrees on head, 754d7e72 and master were deleted, and nothing was committed.
- **IDs**: carried items keep F36, F37, F40–F44, F41, the F4 remainder and F7. New findings are F45 and F46.
- **Counts**: **1 bug** (F45), 4 suggestions (F41, F42, F44, F46; all pre-existing, non-blocking), 1 nit (F40, accepted).
- **Verdict: NOT SHIP.** F43 is fixed, and the positive walk is correct for every arithmetic and relationship-chain shape. But because it looks through **arithmetic only**, a to-one value hop inside a grouped `not`/`and`/`or` comparison operand is no longer guarded. `Ready is (not advisor Active)` with the advisor unlinked was False/False at 754d7e72 and now **throws in simulate and NREs in print** (F45).

## Oracles

| Oracle | Result |
|---|---|
| Full suite at head | **2912 / 2912 passed**, 0 failed, 0 skipped |
| CI on head | **pass**: Build & test, run 36522460683 |
| `git merge-tree --write-tree` vs PR 83 `c2265740` | **clean** (exit 0, tree `96695fa7…`) |
| `git merge-tree --write-tree` vs master `9db8868f` | **clean** (exit 0, tree `a83ff2d4…`) |
| New test copied alone onto 754d7e72 | **2 / 2 fail** (below) |
| Probe matrix at head (181 rows) and the same matrix on master | 0 rows worse than master; 72 rows fixed vs master; the 10 mismatches are all the parked F7 parser family (identical on master) |
| F45 family probed on head, 754d7e72 and master | 3 comparison shapes regressed vs 754d7e72 (below) |

## The rewrite (`Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`)

- `:427-437` `CollectValueHops(operand)`: looks through `Add`/`Subtract`/`Multiply`/`Divide`. A `RelationshipNavigation` goes to `CollectHopChain`. Any other operand adds no hop.
- `:442-456` `CollectHopChain`: stops at a binder name, a collection nav, or a predicate-shaped target. Otherwise it records the hop, de-duplicated by dotted path, and follows `TargetProperty` while it is another navigation.
- The quantifier early return is gone. Quantifiers now add no hop simply because they are not arithmetic or a navigation.

## Probe matrix (simulate / print / correct; head)

Values unless noted: Customer Age 20, Ready true; Advisor Age 40; Mentor Age 50. The Order family uses Qty 1, Stock 5, Rating 5, Owner Credit 3, Total 0. Link states: none, advisor only, advisor + mentor. Each family ran with and without an unrelated same-named relation on the outer entity (`mentor` on Customer, `product` on Order, `order` on Customer); both variants always gave identical results.

| Family | Shape | none | advisor only | both | Result |
|---|---|---|---|---|---|
| F1 | `Age < advisor Age` | F/F (F) | T/T (T) | T/T (T) | ok |
| F1 | `Age < advisor mentor Age` | F/F | F/F | T/T | ok |
| F1 | `advisor mentor Age > Age` | F/F | F/F | **F/F** (T expected) | F7 self-compare, pre-existing, identical on master |
| Arithmetic | `Age < advisor Age + advisor mentor Age` | F/F | F/F | T/T | ok; printed `this.Advisor != null && (this.Advisor.Mentor != null && …)`, one guard per path, outer first |
| Arithmetic | `Age < advisor Age * 2 - advisor mentor Age` | F/F | F/F | T/T | ok |
| Arithmetic | `Age + 1 < advisor mentor Age` | F/F | F/F | T/T | ok |
| Arithmetic | `(advisor Age) - (advisor mentor Age) < Age`, `(advisor Age + advisor mentor Age) > Age`, `Age < (advisor Age) + (advisor mentor Age)` | F/F | F/F | T/T | ok |
| Arithmetic | `advisor Age - advisor mentor Age < Age` (no parens) | CS0019 / VM throw | same | same | F7 family: the parser folds a leading path comparison over the RHS. Pre-existing, identical on master; fails loudly |
| F43 | `Ready is (advisor where Age < mentor Age)` | F/F | F/F | T/T | **fixed**; `this.Mentor` is gone |
| Nested bool | `Ready is (Age < advisor Age)` | F/F | T/T | T/T | ok |
| Nested bool | `Ready is (Age < advisor mentor Age or Age > 100)` | F/F | F/F | T/T | ok |
| Nested bool | `not (Age < advisor mentor Age)` | T/T | T/T | F/F | ok |
| Nested bool | `Ready is (advisor mentor Age > 30)` | F/F | F/F | T/T | ok |
| **F45** | `Ready is (not advisor Active)` | **throw / NRE** (F) | F/F | F/F | **regressed vs 754d7e72 (F/F there)** |
| **F45** | `Ready is (advisor Active and Ready)` | **throw / NRE** (F) | T/T | T/T | **regressed vs 754d7e72** |
| **F45** | `Ready is (advisor Active or Ready)` | **throw / NRE** (F) | T/T | T/T | **regressed vs 754d7e72** |
| Bool leaf, comparison | `Ready is advisor Active`, `Ready is not advisor Active`, `(advisor Active) == true` | F/F | ok | ok | ok |
| F46 (pre-existing) | `advisor Active`, `not advisor Active` as a policy root | throw / NRE | ok | ok | same on master and 754d7e72 |
| F46 (pre-existing) | `advisor where Active and mentor Active` (also as `Ready is (…)`) | F/F | **throw / NRE** (mentor unlinked) | T/T | same on master; at 754d7e72 the `Ready is` form was F43's CS1061 |

| Family (Order) | Shape | product linked, supplier unlinked | product unlinked | both linked | Result |
|---|---|---|---|---|---|
| F36 | `(count items where Qty < product supplier Rating) > 0` | F/F | F/F | T/T | ok |
| F36 | `any items where Qty < product supplier Rating` | F/F | F/F | T/T | ok |
| F36 | `all items where Qty < product supplier Rating` | F/F | F/F | T/T | ok |
| F36 | `Total < count items where Qty < product supplier Rating` | F/F | F/F | T/T | ok |
| F36 | `(count items where Qty < product Stock) > 0`, `(any items where …) == true` (product linked) | T/T | — | — | ok |
| Mixed | `Total < (count items where Qty < product Stock) + owner Credit` | owner linked T/T; owner unlinked F/F | | | ok |
| Mixed | `Total + (count items where Qty < product Stock) < owner Credit` | owner linked T/T; unlinked F/F | | | ok |
| Mixed | `Total < owner Credit - (count items where Qty < product supplier Rating)` | owner linked T/T; unlinked F/F | | | ok |
| Mixed | `(owner Credit) > count items where Qty < product Stock` | owner linked T/T; unlinked F/F | | | ok (without parens it is rejected by analysis: F7 family, pre-existing) |
| Collection | `(count items) > 0`, `Total < count items` | T/T | | | ok |
| Collection | `Total < (count items) + owner Credit` | owner linked T/T; unlinked F/F | | | ok |
| F44 (pre-existing) | Order `product: many` + Item `product: Product`, `any items where Qty < product Stock` | export throws "requires exactly one linked target" | | | loud; same on master |

| Family (binder) | Shape | Result (simulate / print / correct) |
|---|---|---|
| F37 | `if (Balance < order Total)`, with and without an outer `order: Order` | flag T/T (T) ok |
| F37 + arithmetic | `if (Balance < order Total + advisor Credit)` | advisor unlinked F/F (F); linked T/T (T) ok |
| F37 + arithmetic | `if ((order Total) - (advisor Credit) > Balance)` | advisor unlinked F/F; linked T/T ok |
| F7 family | `if (order Total - advisor Credit > Balance)` (no parens) | CS1061 / VM throw ('Advisor' has no 'Balance'); pre-existing, identical on master |
| F42 (pre-existing) | `if (Balance < advisor order Total)` where the advisor has its own order | T/T; correct is F. Same on master |
| Binder multi-hop | `if (Balance < order customer Credit)` | rejected by analysis (previous round; code path unchanged) |

| Family (temporal) | Shape | Result |
|---|---|---|
| Date | `Due + 14 Days > advisor Due`, `Due < advisor Due` | advisor unlinked F/F; linked T/T and F/F as expected. ok |
| Date | a hop inside the date operation: `advisor Due + 14 Days > Due`, `Due < (advisor Due) + 14 Days`, `(advisor Due + 14 Days) > Due`, `Due < (advisor mentor Due) + 14 Days` | all **rejected by analysis** ("bare duration with no temporal left operand"), on master too, so an unguarded `DateOperation` hop is not reachable. If `DateOperationFold` ever accepts a navigation date, `CollectValueHops` will need a `DateOperation` arm. |

## F8: still live

- The path key and fold order are unchanged in effect (`:409-412`, `:449-455`).
- **Mutation re-run at head**: an unconditional `hops.Add` fails `HopReachedTwice_IsGuardedOnce_AndIsFalseWhenInnerHopUnlinked` with "Expected to be 1 but found 2".
- **Mutation re-run at head**: a forward fold fails `MultiHopValuePath_UnlinkedInnerHop_IsFalse_OnSimulateAndPrintedCsharp` with "Member 'Mentor' requires a non-null instance".

## New test

`Poly.Tests/DomainModeling/Lowering/ForeignRootedHopAgreeTests.cs:119` `ConditionNavigationOperand_IsGuardedOnItsOwnHop(bool outerHasMentor)`. I copied it onto 754d7e72:

- `(False)` fails with "VM compile rejected: Type 'Customer' does not contain a member named 'Mentor'".
- `(True)` fails with "Expected to be true" (the silent wrong answer).

Both pass at head, so the test is meaningful. It covers only the linked case; the unlinked cases are covered by the matrix above, not by a test.

## F45 (bug; regression within this PR, introduced by 0e03bcf3)

- **Where**: `DomainExpressionLoweringPass.cs:429-436`. `CollectValueHops` has no arm for `Not`/`And`/`Or`. A value-shaped navigation is lowered as a bare `NullForgiving` hop with no null check (`:180-182`), so the comparison's guard is the only thing that makes an unlinked hop false. At 754d7e72 the child walk descended into `Not`/`And`/`Or` and collected the hop.
- **Repro** (accepted by analysis):

  ```text
  Advisor: entity { Active: Boolean default(true) }
  Customer: entity { Ready: Boolean default(true)  advisor: Advisor
    P: policy { Ready is (not advisor Active) } }
  ```

  With the advisor unlinked, head prints `this.Ready == !this.Advisor!.Active`. Print throws a NullReferenceException, and simulate throws "Member 'Active' requires a non-null instance". At 754d7e72 it printed `this.Advisor != null && this.Ready == !this.Advisor!.Active`, giving False/False. `Ready is (advisor Active and Ready)` and `Ready is (advisor Active or Ready)` behave the same way. Master also throws (no guards there), so this is a hole in this PR's F1 contract ("an unlinked hop makes the comparison false") that 0e03bcf3 reopened.
- **Direction**: add `case Ontology.Not or Ontology.And or Ontology.Or:` to the look-through in `CollectValueHops`. A nested `Comparison` child still adds nothing because it guards itself. Add `Ready is (not advisor Active)` with the advisor unlinked to an agreement test, and update the summary line "Only arithmetic is looked through".

## F46 (suggestion, pre-existing): boolean value hops outside a comparison are unguarded

- **Where**: `DomainExpressionLoweringPass.cs:180-182`. A navigation whose leaf is a plain Boolean property is value-shaped, so it gets no `rel != null`.
- `advisor Active` or `not advisor Active` as a policy root throws in simulate and NREs in print when the advisor is unlinked.
- `advisor where Active and mentor Active` with the mentor unlinked does the same, because the inner value hop sits under `And`, not under a comparison.
- Identical on master and 754d7e72, so this PR didn't cause it. Not blocking.
- **Direction**: guard a Boolean-leaf navigation at the navigation itself (predicate shape). That would also cover F45 without the comparison look-through.

## Comment and summary (`:400-406`, `:417-426`)

They claim only what the code does: "Only arithmetic is looked through; any other operand adds no hop", the stop conditions, and "binder … at any depth". Leaving out the collection stop is harmless, since the summary says "to-one hops". The parenthetical gives only the quantifier reason for "adds no hop"; it is accurate but not exhaustive.

**F42 as parked**: honest about the behaviour. The summary states the at-any-depth binder read plainly and the follow-up keeps it tracked. It reads as design rather than a known issue, though; add "(known issue, F42)" so a hand editor doesn't treat it as the spec.

## Carried items

| Id | Status | Evidence |
|---|---|---|
| F36 (bug) | **CLOSED** | quantifiers add no hop (`:429-436`); `ForeignRootedHopAgreeTests.cs:22`; matrix F36 rows |
| F37 (bug) | **CLOSED** | `:445`; `ForeignRootedHopAgreeTests.cs:71`; binder rows |
| F43 (bug) | **CLOSED** | `:447` stops at a predicate target; `ForeignRootedHopAgreeTests.cs:119`; F43 rows |
| F40 (nit) | **Accepted** | `UnlinkedComparisonAgreeTests.cs:204-205` (it fails loudly, never falsely passes) |
| F41 (sugg, pre-existing) | Open, not touched | `StructuralDomainAnalyzer.cs:69-74` |
| F42 (sugg, pre-existing) | Parked | `DomainExpressionLoweringPass.cs:158-164`, `:445`; wrong answer reproduced on head and master |
| F44 (sugg, pre-existing) | Open | `:258-259` (`IsCollectionNav` is outer-scoped). Grok also showed the same thing outside quantifiers: `advisor mentor Age` with Customer `mentor: many` passes analysis and then throws at lowering (`:174-177`). It fails loudly either way. |
| F4 remainder | Open, not touched | allow-case and unknown-enum-member tests |
| F7 | Parked; evidence broadened | `Poly/DomainModeling/Language/DslExpressionParser.cs:190-199`. Beyond the silent self-compare, the leading-path comparison fold also yields loud failures: `advisor Age - advisor mentor Age < Age` (CS0019), `order Total - advisor Credit > Balance` (CS1061), and `owner Credit > count items …` (rejected). All identical on master; parenthesizing the path avoids them. |
| F1–F3, F5, F6, F8 | Closed | F8 mutations live (above) |

## Hand-editability gate: **YES**

`CollectValueHops` is a two-arm switch and `CollectHopChain` is one stop condition plus a follow. The summary says exactly what is looked through. The F45 fix is one added `case` that Scot could write by hand from the summary. The new test copies the two existing ones in the same file. The F42 wording should be labelled a known issue (above), but that doesn't block editing.

## Mill disposition (Grok Build grok-4.6)

- F43 fixed, F8 unchanged, new test meaningful, F42 parked honestly (with a spec-wording caveat): **agree** (verified).
- F45 (`not`/`and`/`or` operand unguarded): **kept as a bug**. Reproduced on head; False/False at 754d7e72; master throws.
- F45 sibling `advisor where Active and mentor Active`: **moved to F46** (pre-existing; same on master).
- `DateOperation` not looked through: **not filed**. A navigation date is rejected by analysis in every spelling I tried (head and master).
- The `IsCollectionNav`-at-depth analogue outside quantifiers: **folded into F44** (it fails loudly).
- Gate YES: **agree**.

## Checklist to ship

- [ ] F45: look through `Not`/`And`/`Or` in `CollectValueHops` (or guard Boolean-leaf navigations at the navigation, which also fixes F46). Add `Ready is (not advisor Active)` / `Ready is (advisor Active and Ready)` with the advisor unlinked to an agreement test, and update the summary.
- [ ] (optional) Label the binder-at-any-depth sentence as F42; F46, F44, F41, F4 remainder, F40 as listed.
