# PR 80 post-merge audit (Warden), 2026-09-28

- **Target**: master `8f8bc8a9f16aad690d56fb6921778c46715b490a` .. `a819bbe5121725ab82d81e151fa1ceac5508f7c5` (squash merge of PR 80, CI `success`).
- **Commits nobody reviewed before the merge**:
  - `dbb58858d7458d3c01c46aca463b606282c6f14d`: W1–W3 fixes.
  - `a3f49f6fb6083237b595cc69a2c3d2a82dd13259`: merge of master after PRs 76 and 79 (F7).
  - `47293489cccfbbb02cede8dc4bb7ee15c9d7ebea`: to-one `ContainsKey` is soft; `get` fails closed without a store.
- The squash tree matches the PR head: `git diff 47293489 a819bbe5` is empty.
- **Mode**: read-only adversarial audit. Nothing was fixed, reverted or pushed to master or the PR branch.
- **Mill**: OpenCode `opencode-go/deepseek-v4.1-flash`, no fallback needed.
  - Run 1 exited 0 but never gave a verdict. It wandered into my scratch probe file and then hit a `/tmp` permission denial.
  - I re-fired it once with the probe file removed. Run 2 exited 0 with 4 findings.
  - I checked every claim at `a819bbe5`: 2 confirmed, 2 folded into other findings, 0 dropped.

## Verdict: **FINDINGS (not clean)**. 3 bugs, 1 suggestion, 1 nit (new or carried). Follow-up PR needed.

| ID | Severity | Where (a819bbe5) | One line |
|---|---|---|---|
| A1 | **bug** | `EffectLoweringPass.cs:42,524`; `DomainToCSharpExporter.Actions.cs:108-110,132-146` | W1 is only partly fixed. A stage-dispatched action that also has an entity-level tail still prints two `previousStage0` locals, so the printed C# fails with CS0136. Base compiles it. |
| A2 | **bug** | `DomainExpressionLoweringPass.cs:167-185` | A path-prefix whose leaf needs statements (a quantifier) moves the loop outside the `rel != null` guard. With the target unlinked, simulate now **throws**; master returned False. PR 79's `if (exists) { … }` wrapper was dropped in the F7 merge. |
| W4 | **bug** (carried, still open) | `DomainExpressionLoweringPass.cs:180-182` ("Value leaves stay the hop") | `Age < advisor Age` with an unlinked advisor still **throws** in simulate and NREs in print. Expected False on both. Master simulate returned False. |
| A3 | suggestion (hand-editability) | `DomainExpressionLoweringPass.cs:78,105-106,261-288`; `EffectLoweringPass.cs:73-77,730` | The replacement for `SourceEntityName` smuggles the entity name in through the `Subject` parameter's type, and falls back to guessing "the first entity with a navigation of that name". |
| A4 | nit | `EffectLoweringPass.cs:591`; `DomainToCSharpExporter.cs:468`; `DomainEntityInstance.cs:1014-1020` | Three files build or parse the `Notify{Stage}Subscribers` name by hand. One shared helper would do it. |

## Findings

### A1: bug. The OnEntry/`previousStage` collision is still reachable through stage-dispatched actions.
- **Code**:
  - `dbb58858` names the capture from a per-instance counter: `previousStage{_previousStageSequence++}` (`EffectLoweringPass.cs:42,524`).
  - `AddStageDispatchedActionMethod` builds one pass per stage branch, plus another for the entity-level tail (`DomainToCSharpExporter.Actions.cs:132-146`, which ends up in `LowerActionToMethodBody` → `new EffectLoweringPass`). Every pass starts its count at 0.
  - The comment at `Actions.cs:108-109` says "their locals come from one name source and cannot collide". That is true for `LocalNames` (`names`, `:110`), which every other local uses, but `previousStage` does not go through it.
  - The doc comments at `EffectLoweringPass.cs:510` ("a unique local") and `DomainToCSharpExporter.cs:363` ("a unique previousStageN capture") overclaim for the same reason.
- **Repro** (scratch test, not committed):
  ```
  WorkItem: entity {
    Code: Text
    Finish: action { transition to Done }
    Draft: stage { Finish: action { transition to Done } }
    Ready: stage { }
    Done: stage { }
  }
  Board: entity { Fires: Number default(0)  items: many WorkItem
    when all items Done { assign Fires to Fires + 1 } }
  ```
  - Printed `WorkItem.Finish()` has `var previousStage0` inside `if (this.CurrentStage == WorkItemStage.Draft) { … }` and then another `var previousStage0` at method scope.
  - Roslyn reports: `(56,17): error CS0136: A local or parameter named 'previousStage0' cannot be declared in this scope`.
  - Base `8f8bc8a9` prints and compiles the same model.
  - It needs any watched target stage: every watched stage gets the capture (see W5), not only `when all`.
- **Why the tests miss it**: `WhenAll_OnEntryChain_SimulateAndPrintedCsharp_SameFireCounts` covers the nested entry chain inside one pass, which is the case filed as W1. Separate stage branches are sibling scopes, so they never collide with each other. The collision needs the entity-level tail.
- **Suggested direction**: take the name from the shared generator, `_names.Next("previousStage")` (`EffectLoweringPass.cs:43` already holds `_names`), and delete `_previousStageSequence`. Add this model to `WhenAllSimulatePrintAgreeTests`, including a print compile.
- The mill found this independently (its claim 1, and claim 4 about the comments).

### A2: bug. A path-prefix over a quantifier moves the loop outside the null guard, and simulate regressed from False to throw.
- **Code**:
  - `DomainExpressionLoweringPass.cs:180-185` routes the leaf, then returns `leaf with { Value = rel != null && leaf.Value }`.
  - When the leaf carries statements, for example `any workers where Active` on the target, those statements (`foreach (var item in this.Team!.Workers)`) are left in `leaf.Statements`. They run **before** the guard is evaluated.
  - The comment at `:168-169` says this is the "same meaning as Conditional(exists, whenPresent, false)". It is not the same when there are statements.
- **Lost hunk**: on master `8f8bc8a9`, PR 79 handled exactly this case. When `whenPresent.Statements.Count > 0` it emitted `t = false; if (exists) { statements; t = value; }`. The `a3f49f6f` merge kept PR 80's shape and dropped that branch.
- **Repro**: this is PR 79's own model from `PathPrefixHop_QuantifierOnTarget_SimulateAndGeneratedCSharp_Agree`, with `team` left unlinked and a store attached.

  | `P: policy { team Active == any workers where Active }` | base 8f8bc8a9 | a819bbe5 |
  |---|---|---|
  | simulate | **False** | **throws** `Member 'Workers' requires a non-null instance.` |
  | print | NRE | NRE |

  Print and simulate now agree on failing, which is fail-closed. But simulate lost its False answer, and print never had one.
- **Suggested direction**: when `leaf.Statements.Count > 0`, bring back the PR 79 shape with `rel != null` as the condition: a temp set to false, then `if (rel != null) { statements; t = leaf.Value; }`. Add the unlinked case to the PR 79 agreement test for both paths.

### W4 (carried): bug. The expected behavior is still not met.
- `DomainExpressionLoweringPass.cs:180-182` still returns the bare hop for value leaves ("Value leaves stay the hop"). `Age < advisor Age` is a `Comparison` whose operand is the path-prefix. `IsPathPrefixPredicate(PropertyAccess "Age")` is false, so no guard is added.
- Probe (DSL `advisor: Advisor`, `OlderThanAdvisor: policy { Age < advisor Age }`):

  | | base 8f8bc8a9 | a819bbe5 |
  |---|---|---|
  | simulate, store attached, unlinked | **False** | **throws** `Member 'Age' requires a non-null instance.` (VM guard, `DirectVmAbiEmitter.Expressions.cs:394-397`) |
  | simulate, no store | throws (no DomainInstanceStore) | throws (same message) |
  | print, unlinked | NRE | NRE |

- The expected result was False on both paths, not a throw, so this finding stays open as a bug. The mill confirmed it independently (its claim 2).
- **Suggested direction**: when a comparison operand contains a to-one path-prefix, lift the `rel != null &&` guard to the enclosing predicate. That gives False on both paths, as master simulate did. Add simulate and print assertions next to `EvaluatePolicy_UnlinkedToOneAdvisor_Comparisons_AreFalse`.

### A3: suggestion (hand-editability, lanes item 11). The entity name for quantifier targets travels through a side channel and falls back to a guess.
- **What changed**: `LoweringContext.SourceEntityName` was deleted. `DomainExpressionLoweringPass` now reads the entity name off the *type* of `context.Subject` (`:78`, `TypeNameOf` at `:290-296`) or off the first `subject` it lowers (`:105-106`).
- **The side channel**: `EffectLoweringPass.cs:73-77` sets the expression pass's `Subject` to `new Parameter("entity", new TypeReference(entity.Name))` only so the name gets through. The comment says so: "seeds quantifier target resolution without resurrecting LoweringContext.SourceEntityName". The tree root is then passed separately as `ThisReference`.
- **The guess**: when no type is found, `ResolveRelationshipTarget` (`:261-288`) scans every entity and takes the first one with a navigation of that name. Passes built straight from the effect context still hit this path, for example the for-each invoke argument pass at `EffectLoweringPass.cs:730` (Subject is `ThisReference`). Two entities with the same navigation name pointing at different targets would give the wrong target enums inside the quantifier body.
- **Evidence it is not a live bug in the common case**: my collision probe on the action-body path compared against the right enum (`LoanStatus.Overdue`). So this is a suggestion, not a bug.
- **Hand-editability**: a cold reader cannot tell that `Subject` on the expression pass is a type carrier and not the root. Nor would they guess that a missing type silently turns into a domain-wide name search.
- **Suggested direction**: pass the entity name explicitly, as a named context field or constructor argument, and fail closed (or return null) when it is missing instead of scanning.

### A4: nit. The `Notify{Stage}Subscribers` name is built or parsed by hand in three places.
- **W2 is closed**. The name-sniff rewrite and its uncompilable `else` are gone. `EffectLoweringPass.cs:589-593` builds the invoke directly with the `previousStage` argument. The exporter passes only the watched-stage set (`DomainToCSharpExporter.cs:362-369`), and `LoweringContext` documents it. The mill also confirmed the argument and parameter always match (every `Notify{Stage}Subscribers` declares `previousStage`, `DomainToCSharpExporter.cs:441-443,467-473`).
- **What is left**: the string template appears in `EffectLoweringPass.cs:591` and `DomainToCSharpExporter.cs:468`. `BindThis` also parses it back out (`DomainEntityInstance.cs:1014-1020`; this parse existed on base, and PR 80 only adds the forwarded arguments). The mill flagged the `BindThis` parse as a remaining name-sniff (its claim 3). I confirmed it and fold it in here as a nit, because it is a pre-existing consumer bind.
- **Suggested direction**: one `NotifySubscribersMethodName(stage)` helper used by all three.

## Prior-finding status at a819bbe5

| ID | Status | Evidence |
|---|---|---|
| W1 | **closed as filed; not fully fixed** (see A1) | The entry-chain model prints `previousStage0`/`previousStage1` and compiles. `WhenAll_OnEntryChain_SimulateAndPrintedCsharp_SameFireCounts` (`WhenAllSimulatePrintAgreeTests.cs:88-139`) checks simulate 0,1 and compiled print 0,1, and passes. The stage-dispatch variant still hits CS0136 (A1). |
| W2 | **closed** | Name-sniff deleted in `dbb58858`. Invoke built at the source, `EffectLoweringPass.cs:589-593`. Docs at `:503-512` and `LoweringContext.cs` `PostTransitionNotifyStages`. Remaining nit in A4. |
| W3 | **closed** | `P4SubscriptionQuantifierDslTests.cs:8-13` now says Any/All live in the shared lowered handler, which is accurate. The assertions are unchanged (the diff against base is comment-only). |
| W4 | **OPEN (bug)** | Still throws in simulate and NREs in print (table above). |
| W5 | open (nit) | Every `Notify{Stage}Subscribers` still takes `previousStage`, `DomainToCSharpExporter.cs:441-443,470`. It also widens A1's reach. |
| W6 | open (nit) | `NotInWatchedStages` next to `InWatchedStages`, `DomainToCSharpExporter.cs:139,148`. |
| W7 | open (nit) | `NotifyTransition` still has no `<param>` for `previousStageName`, `DomainInstanceStore.cs:447-454`. |
| W8 | open (nit) | `ExecuteCachedSubscriptionTree(Node tree, object? previousStageName)`, `DomainEntityInstance.HostAbi.cs:278-279`. |
| N1 | open (nit) | `CompletePolicyBodies` still keeps the first body per (entity, policy name), `RuntimeAnalysisCache.cs:366-389`. |
| N2 | closed | The "standalone C." text went away with the F7 stubs (grep: 0 hits). |
| N3 | open (acceptable) | Collection path-prefix fail-closed throw, `DomainExpressionLoweringPass.cs:174-178`. |
| B3 | **closed**, and improved in print | Unlinked with a store: `advisor Name is not "Pat"` and `advisor Age < 30` return False in simulate at head (base also False). Print is now False; base NRE'd. Committed oracle `EvaluatePolicy_UnlinkedToOneAdvisor_Comparisons_AreFalse`. |

### 47293489 (to-one `ContainsKey` soft; `get` fails closed without a store): **behaves as described**
- `ContainsKey` now uses `MatchOneToOneNavigation`, which never touches the store (`DomainEntityInstance.Dictionary.cs:33-36`, `Runtime.cs:338-352`).
- The indexer and `TryGetOneToOneNavigation` throw when `Store is null || Domain is null` (`Runtime.cs:366-369`).
- PR 79's `Indexer_OneToOne_WithoutStore_ReturnsNull` was deliberately replaced by `Indexer_OneToOne_WithoutStore_ContainsKey_True_Get_Throws` (`DomainEntityInstanceTests.cs:3123-3135`). This is a deliberate reversal of PR 79's null-without-store answer, and the commit message says so.
- Probes, no store: `advisor exists`, `advisor Name is not "Pat"`, `advisor Age < 30` and `Age < advisor Age` all throw "Cannot resolve relationship target without a DomainInstanceStore" at **both** base `8f8bc8a9` and head. So `advisor exists` with no store is unchanged from base.
- No fail-open case found (the mill agrees): nothing returns True for an unlinked target or a missing store.

### Scot's decisions: **still hold after the rebase**
- `when any` is level-triggered. The diff of `P4SubscriptionQuantifierDslTests.cs` against base is comment-only; the goldens `:79,85` are `"FIRED"`. `UniversityDogfoodTests.cs` is byte-identical to base, with `:203` at `2L`. `WhenAnySimulatePrintAgreeTests` 2/2 pass.
- `when all` fires once, on the transition where the last linked record reaches a watched stage.
  - `DomainModelingSurfaceCompletionTests.cs` is byte-identical to base (Finish → `"set"`, `:245-246`), and `Subscription_All_SpreadStages_Fires` passes.
  - The Ready/Done test `WhenAll_MultiStage_SimulateAndPrintedCsharp_SameFireCounts` (0,1,1 on both paths) passes.

## F7 rebase check (a3f49f6f)
- **Method**:
  1. For PRs 76 (`6f7eb116` vs `640025f7`) and 79 (`8f8bc8a9` vs `cfb67af4`), I collected every line each PR added.
  2. I listed the lines removed by `8f8bc8a9..a819bbe5` in the same files and matched the two sets.
  3. I compared `[Test]` method names in every test file each PR touched against `a819bbe5`.
  4. PR 80's own changes reach master whole: the squash tree equals head `47293489`.
- **PR 76**: nothing dropped. 0 of its added lines removed; 8/8 tests present.
- **PR 79**: 245 tests checked, 1 intentionally replaced (47293489, above). Matched removed hunks:

  | Hunk (base 8f8bc8a9) | At a819bbe5 | Verdict |
  |---|---|---|
  | `UseThisReference` (LoweringContext, pass, `QuantifierLoopTests.cs:487-493`) | Removed. The test now builds `new LoweringContext(new ThisReference(), …)` | intentional equivalent (PR 80 deletes the twin); grep shows 0 domain-lowering hits |
  | `SourceEntityName: entity.Name` (Actions.cs) and the context field | Replaced by `_relationshipSourceEntityName` plus the Subject type (A3) | equivalent in the common path; hand-editability suggestion A3 |
  | Path-prefix `ExistsRelated`/`GetRelatedOne` runtime branch | Replaced by `rel != null && leaf` | intentional (S2) **except** that the statement-bearing branch `t=false; if (exists){…}` was dropped: **A2** |
  | `Exists`/`NotExists` `ExistsRelated` arms | Removed; to-one exists lowers through the member | intentional; probes False/True as on base |
  | Actions.cs `names` / `LocalNames` threading | Kept (`Actions.cs:110,133,143`) | ok, but `previousStage` bypasses it: **A1** |
  | LoweringContext `<param>` docs | Replaced by accurate docs for the new params | ok |

- **PR 80**: 335/335 tests at head present. `Analysis_ForEachInvoke_StoreDependentPredicatePolicy_Rejected` (present at 76ab9346) is absent. That is intentional: PR 79 removed it on master (present at `cfb67af4`, gone at `8f8bc8a9`) because quantifier policies now print.
- **Stubs gone**: `ContainsStoreQuantifierJob`, `StoreAwarePolicyThrowStub`, `IsRelationshipNavigation`, `ExistsRelated`, `GetRelatedOne` all have 0 hits in `Poly/`.
- **Quantifiers are plain nodes**: any/all/none/count lower to `ForEachLoop`/`IfStatement`/`Assignment` (`DomainExpressionLoweringPass.cs:426-500`). They are not store calls, so both paths can step through them.
- **Print**: `QuantifierLoopTests` 19/19 pass, including `Quantifiers_GiveSameAnswers_SimulatedAndInExportedCSharp` and `PathPrefixHop_QuantifierOnTarget_…` (linked case).
- **Result**: nothing from PR 76 or PR 80 was dropped. From PR 79, one hunk changed without anyone saying so: the guarded statement branch for a path-prefix over a quantifier (A2).

## Hand-editability gate (lanes item 11) on the unreviewed commits: **FAIL**
- **Comments claim only what the code does**: fail.
  - `Actions.cs:108-109` and `EffectLoweringPass.cs:510`/`DomainToCSharpExporter.cs:363` claim uniqueness that does not hold (A1).
  - `DomainExpressionLoweringPass.cs:168-169` claims `Conditional(exists…)` meaning that does not hold with statements (A2).
  - The W3 comment is now accurate.
- **No dead guards or twin paths**: pass. The name-sniff is gone, and so are the stubs.
- **Names match**: mostly pass. `Subject` doubling as a type carrier (A3) is the exception.
- **Tests read human**: pass (the new entry-chain test is linear).
- **No agent paperwork in product code**: pass. Grepping the added lines for PR numbers, W/F tags, Warden and Slice finds nothing.
- **Simpler to hand-edit**: mixed. W2's cross-file rewrite is gone (a win), but A3 adds a hidden entity-name channel.

## Build and test evidence (detached worktree at a819bbe5)

| Command | Result |
|---|---|
| `dotnet build Poly.slnx -p:NuGetAudit=false` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` | **Passed 2895/2895** (failed 0, skipped 0) |
| filter `WhenAllSimulatePrintAgreeTests` / `WhenAnySimulatePrintAgreeTests` | 2/2, 2/2 |
| filter `P4SubscriptionQuantifierDslTests` / `UniversityDogfoodTests` | 3/3, 2/2 |
| filter `QuantifierLoopTests` / `Subscription_All_SpreadStages_Fires` | 19/19, 1/1 |
| GitHub CI on a819bbe5 | `CI completed success` |

- **Scratch probes**: one throwaway test file (models above), run at a819bbe5 and at base 8f8bc8a9 in detached worktrees. Base also needed the four compile/reflect helpers copied from `WhenAnySimulatePrintAgreeTests`. The probes were deleted and none were committed.
- Plain `dotnet build` still fails on NU1903, which is environmental and already known.

## Mill claims
Every mill claim checked out. Claims 3 and 4 are folded in rather than listed on their own:
- **Claim 1** (A1): confirmed by probe.
- **Claim 2** (W4): confirmed by probe. Its line cite `:184-190` is off by a few lines; actual `:180-185`.
- **Claim 3** (`BindThis` name-sniff): confirmed, but it existed on base (`8f8bc8a9` `DomainEntityInstance.cs:1013-1017`). Folded into A4 as a nit, not a PR 80 regression.
- **Claim 4** (the "unique" comments overclaim): confirmed and folded into A1.
- **"No finding" answers**: Q2 (argument/parameter match), Q4 (no fail-open) and Q5 (quantifiers intact, stubs gone) all agree with my own checks.
- **Missed by the mill**: A2 and A3 (hand pass only).

## Not counted (pre-existing)
- In the A1 model, the printed `Draft` branch of `Finish()` assigns `CurrentStage = Done` twice (the stage copy plus the entity body). Base prints the same two assignments. I did not check the intended semantics. Worth a look separately.

## Follow-ups (for one follow-up PR)
- [ ] **A1**: `previousStage` through `LocalNames`; stage-dispatch plus entity-tail agreement test with a print compile.
- [ ] **A2**: restore the guarded statement block for a path-prefix over a quantifier; unlinked agreement test.
- [ ] **W4**: lift the guard for value-operand path-prefixes (False on both paths); simulate and print oracle.
- [ ] **A3**: explicit entity name for quantifier target resolution; no domain-wide scan.
- [ ] A4, W5–W8, N1, N3: nits.
