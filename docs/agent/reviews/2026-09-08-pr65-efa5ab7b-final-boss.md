# Final Boss re-verify PR 65 — 2026-09-08

- **Target**: PR 65 https://github.com/scoizzle/Poly/pull/65 · branch `fix/sequential-failure-fail-before-mutate` · PINNED worktree `/workspace/Poly-pr65-efa5ab7b` · vs `origin/master`
- **Mode**: re-verify (not rubber-stamp). Razor ship @ `efa5ab7b` treated as claims to prove or reject from this SHA. Foreman: do not block ship on F1–F5 unless a **new** bug is found.
- **Model**: grok-4.6
- **SHA**: `efa5ab7bac3bd99bccb12082dd2ffb812281dab5` (`git rev-parse HEAD` matched before review-doc commit; product HEAD not moved; no production or test edits)
- **Issue counts**: 1 bug, 4 suggestions, 1 nit
- **Verdict**: **not ship** — Occupy-flat assign-then-`if (OpenStays >= 1)` fail-before-mutate holds on this SHA (same Lower tree for simulate + C# print; Nights Failure; `OpenStays==0`; Unique restore cannot explain). Nested `if` / `else if` that read an ancestor-assigned property still probe the **pre-assign** bag (`CollectGuardedBranchProbes` then/else walks start with an empty `assignedRhs`). Scratch InvokeAction on this SHA: nested `confirm=true` nights=0 → Nights Failure with **OpenStays=1**; else-if `confirm=false` nights=0 → same. That is the Item 4 failure mode on shipped DSL, not restore theater.
- **Process notes**: Razor sibling table noted “then/else fresh dict” as coverage, not a bug. Primary dump + InvokeAction this session upgrades that to a contract miss. Do not chain-trust Razor’s “VM AnyExpr throws” (named-action `UseThisReference` true throws; entry/exit VM `UseThisReference` false lowers `StoreQuantifier`).

## Summary

PR 65 is four files vs `origin/master`: `EffectLoweringPass` replaces `ConditionReadsAssignedProperty` skip with `SubstituteAssignedProperties` for guarded ProbeCreate prefixes; Item4 order tests; `IfOnMutatedProperty` retarget (Failure + OpenStays==0 + module order); exporter order retarget. No `DomainEntityInstance` / `RestoreActionState` product hunks (`git diff origin/master...HEAD -- Poly/DomainModeling/Runtime/` empty). Flat Occupy is one tree and fail-before-mutate. Nested/else-if inherit of `assignedRhs` is not: probe conditions inside then/else do not see prior sibling assigns from the outer walk, so taken illegal create runs after `OpenStays = OpenStays + 1`. Suite tests do not force that sibling. Full `Poly.Tests` this session: **2809** pass, **0** fail (does not cover the nested miss).

## PR facts (verified this session)

| Claim | Evidence |
|-------|----------|
| Tip SHA | `git rev-parse HEAD` = `efa5ab7bac3bd99bccb12082dd2ffb812281dab5` |
| PR head | `gh pr view 65` `headRefOid` = same SHA; `mergeable: MERGEABLE`; `mergeStateStatus: CLEAN`; CI Build & test SUCCESS |
| Diff `origin/master...HEAD` | 4 files, +192/−45: `EffectLoweringPass.cs`, `Item4FailBeforeMutateTests.cs`, `ActionEntityReturnTests.cs`, `DomainToCSharpExporterTests.cs` |
| DEI / restore product diff | **none** |
| `RestoreActionState` gate | `git show efa5ab7b:Poly/DomainModeling/Runtime/DomainEntityInstance.cs` `:601-602` Unique-only |

## Razor claim disposition (this SHA, primary evidence)

| Razor claim | Disposition | Evidence |
|-------------|-------------|----------|
| `SubstituteAssignedProperties` for guarded ProbeCreate prefixes; same Lower tree simulate + C# print | **holds for named actions** | `LowerActionBody` `:699-703` prefixes `LowerCreateInConstraintProbes`; exporter `DomainToCSharpExporter.Actions.cs:449` same `LowerActionBody`; named invoke `DomainEntityInstance.cs:727-738` binds module Body (`BindThis`, not a second lower). Scratch export Occupy: `if (this.OpenStays + 1L >= 1L) { ProbeCreate }` then `OpenStays = OpenStays + 1L`. |
| Occupy assign-then-illegal-create fails before mutate (`Nights`, `OpenStays==0`) — not Unique restore | **holds for flat Occupy** | `ActionEntityReturnTests.cs:1194-1235`; scratch InvokeAction nights=0: `Succeeded=False`, `'Nights' must be >= 1.`, `OpenStays=0`, children=0. Restore Unique-only `:601-602`; message has no `"Unique"`. Master oracle was `OpenStays==1` (`StillAppliesPriorAssign`). |
| Item4 order oracles; IfOnMutatedProperty retarget; exporter ProbeCreate-before-assign | **holds as written** | Item4 `:18-50` LowerActionBody + `:53-80` `session.Lower` module; exporter `:2388-2416` order only (F1). |
| No DEI / RestoreActionState product diff | **holds** | `git diff origin/master...HEAD` names the four files above. |
| Nested if / multi-assign “scratch OK”; then/else fresh dict is coverage only | **rejected — bug** | Fresh dict `:805` / `:817` drops outer `assignedRhs`. Nested + else-if dumps probe `if (this.OpenStays >= 1L)` **unsubstituted**. Runtime OpenStays=1 after Nights Failure. |
| AnyExpr substitution latent because VM throws | **narrowed** | `DomainExpressionLoweringPass.cs:336-339`: `UseThisReference` true (named-action module / C# print) throws `Q3NotSupported`; false (entry/exit VM `:247`, subscriptions `:343`) lowers `StoreQuantifier`. Named-action Item 4 path still cannot run a mis-substituted `any`. Keep F4 as suggestion. |

## Sibling-path check

| Semantic | Paths | Invariant on all? | Test forces path? |
|---|---|---|---|
| Flat Occupy `assign OpenStays; if (OpenStays >= 1) create illegal` | Module Body ProbeCreate + subst; export same; InvokeAction | **Yes** — `OpenStays+1 >= 1` then assign; runtime Nights + OpenStays 0 | **Yes** — `IfOnMutatedProperty` + Item4 order + exporter order |
| AssignFalse `assign Create=false; if (Create) create` | Probe `if (false)`; body assign then skip | **Yes** on export dump + runtime | Runtime `AssignFalseSkipsCreate_Succeeds` `:740`; export/Item4 **no** (F1/F2) |
| Else-if on **parameter** (`wait`) after OpenStays assign | Probe `if (!confirm) { if (wait) ProbeCreate }` | **Yes** — taken-ness is wait/confirm, not OpenStays | `ElseIfCreateBranch` `:1163-1189` OpenStays==0 |
| Nested `if (confirm) { if (OpenStays >= 1) create }` after assign OpenStays | Then-walk empty `assignedRhs` `:804-805`; inner condition unsubstituted | **No** — probe uses pre-assign OpenStays; body mutates then fails | **No** suite test. Scratch: OpenStays=1 |
| `else if (OpenStays >= 1) create` after assign OpenStays | Else-walk empty dict `:816-817` | **No** — same miss | **No**. Scratch: OpenStays=1 |
| Multi-assign chain `A=1; B=A+1; if (B>=2)` | `assignedRhs[name] = Substitute(value, assignedRhs)` `:766` | **Yes** on same-list siblings (dump ProbeCreate before `A=1L`) | **No** suite test |
| create vs create-in | `LowerCreateEntityInstanceProbe` / `LowerCreateInProbe` | Same `CollectCreateInProbes` | create Occupy; create-in ConditionDrift Unique |
| Stage entry assign-then-if-create | `AppendInlinedStageEffects` `:311` `priorMutation: true` then same collect | Flat OnEntry would substitute; nested inherit same bug | Exporter `ProbesBeforeCurrentStage` `:2420` order only; if-condition is `Flag` not OpenStays |
| Unique Failure restore | `RestoreActionState` Unique-only `:601-602` | Unchanged; not Item 4 path | ConditionDrift Unique `:702-737` still bag-only (F3) |

## Reachability

- **Occupy-flat**: valid Hotel domain, nights=0. Probe taken via subst. Failure is range, not Unique. Restore does not run. **Intended product change** with tests.
- **Nested / else-if OpenStays**: valid shipped `if` / `else if`. Same nights=0 Nights Failure. Probe **not** taken (`OpenStays` still 0). Body assign runs. Restore does not run. **Wrong outcome on valid inputs** → bug.
- No new throw on valid Occupy. `DomainExpressionRewriteBase.Default` `:28-31` still fail-loud for unhandled expression subtypes; closed switch covers temporal leaves.
- Quantifier-in-condition after assign: named-action Lower throws (`UseThisReference` true); not a silent wrong taken-ness on the Occupy path.

## Invariant-stating comments

- “Same tree for simulate and emit (Item 4 fail-before-mutate)” (`EffectLoweringPass.cs:695-697`) — **holds** for named actions: one `LowerActionBody`, including the nested miss (simulate and print are both wrong together).
- “Probe if-conditions are lowered as after prior sibling AssignEffects … so Occupy-shaped OpenStays+1 then if (OpenStays>=1) probes taken” (`:739-742`) — Occupy-flat **holds**; the general “probe if-conditions” clause is **false** for nested/else-if inner conditions (sibling violates the comment).
- Test comment “Oracle is module ProbeCreate-before-assign, not DEI restore” (`ActionEntityReturnTests.cs:1197`) — **holds** for that test; Nights bypasses Unique restore.

## DEI / RestoreActionState

- PR 65 diff does not touch `DomainEntityInstance` / `RestoreActionState`.
- `:601-602` still Unique-gated. Occupy and nested scratches fail on **Nights**; nested OpenStays=1 cannot be restore.

## Issues

### Issue 1 -- Severity: bug
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:804-805`, `:816-817`
- Description: `CollectGuardedBranchProbes` substitutes only the **outer** `cond.Condition` with `priorAssignRhs` (`:807`, `:820`) then walks then/else with a **new empty** `assignedRhs`. Inner `ConditionalEffect` probes therefore do not see properties assigned by ancestor siblings. Shipped shapes:
  - `assign OpenStays to OpenStays + 1` then `if (confirm) { if (OpenStays >= 1) { create Stay { Nights: nights } } }`
  - `assign OpenStays to OpenStays + 1` then `if (confirm) { } else if (OpenStays >= 1) { create Stay { Nights: nights } }`
  Export this SHA: inner probe is `if (this.OpenStays >= 1L)` (pre-assign), then `this.OpenStays = this.OpenStays + 1L`. Scratch InvokeAction nights=0: Nights Failure, **OpenStays=1**, children=0. Unique restore does not run (`ErrorMessage` is `'Nights' must be >= 1.`). Flat Occupy on the same harness is OpenStays=0 — so this is not dump-harness theater. Same gap existed on master for inner conditions (old skip was outer-condition only); this PR’s substitution invariant does not cover the sibling, and Item 4’s “sequential Failure fail-before-mutate in the Lower tree” claim is false for it.
- Suggestion: Copy `priorAssignRhs` into a **new** dictionary for then and for else (inner assigns overlay; else does not see then assigns). Add InvokeAction + export oracles: nested confirm=true nights=0 and else-if confirm=false nights=0 → Failure contains Nights, OpenStays==0, ProbeCreate-before-assign, and inner probe guard contains `OpenStays + 1` (or `OpenStays + 1L`).
- Status: open
- Reachability: **valid domain, valid inputs** — nested `if` and `else if` are shipped DSL; ElseIfCreate already exists with `wait` instead of `OpenStays`.

### Issue 2 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:2388-2416`
- Description: `Export_IfOnMutatedProperty_ProbesBeforeOpenStaysAssign` only asserts ProbeCreate index before OpenStays assign. An unguarded ProbeCreate before assign would still pass and would break AssignFalse (`if (false)` wrap). Comment claims post-prior-assign condition; suite does not lock `OpenStays + 1` / `>= 1`. Scratch export this SHA **does** emit `if (this.OpenStays + 1L >= 1L)`.
- Suggestion: Assert Book text before first ProbeCreate contains `OpenStays + 1` (or `OpenStays + 1L`) and `>= 1`. Add export sibling for AssignFalse expecting `if (false)` wrapping ProbeCreate.
- Status: open

### Issue 3 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs:18-80`
- Description: Both Item4 tests only assert ProbeCreate-before-OpenStays-assign order. They do not assert `Succeeded==false`, Nights, OpenStays==0, or empty `CreatedChildren`. The strong runtime+order oracle lives only in `ActionEntityReturnTests.InvokeAction_IfOnMutatedProperty_CreateIllegal_DoesNotApplyPriorAssign` (`:1194-1235`). Item4 alone cannot carry fail-before-mutate if that retarget drifts.
- Suggestion: Promote Failure + OpenStays==0 + empty children into Item4 (or call the ActionEntityReturnTests oracle) in addition to order.
- Status: open

### Issue 4 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:702-737`
- Description: `InvokeAction_ConditionDrift_AssignThenIfCreate_DoesNotApplyPriorAssigns` uses Unique Failure. Restore still runs (`DomainEntityInstance.cs:601-602`). Create==false / Occupied==0 are ambiguous with restore theater if the probe prefix regresses. Unlike the Nights Occupy retarget, this test does not assert ProbeCreate-before-assign order.
- Suggestion: Assert module ProbeCreate-before-assign (Create/Occupied), or switch the illegal create to a non-Unique constraint.
- Status: open

### Issue 5 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:787-791`
- Description: `SubstituteAssignedPropertiesRewrite` replaces every `PropertyAccess` whose name is in `assignedRhs`, including names inside `AnyExpr`/`AllExpr`/`NoneExpr`/`CountExpr` bodies and `RelationshipNavigation.TargetProperty`. Named-action / C# print Lower of `any` throws (`DomainExpressionLoweringPass.cs:336-339`, `UseThisReference` true). Entry/exit VM and subscription Lower use `UseThisReference` false and **do** emit `StoreQuantifier` — substitution into related-entity `Active` after subject `assign Active` is live on that sibling, not only “when quantifier lowering lands.”
- Suggestion: Substitute only subject-rooted PropertyAccess (skip quantifier/relationship scopes). Add a regression domain: assign subject `Active`, `if (any rel where Active) { create }`, assert related `Active` is not rewritten. Prefer an OnEntry VM body so the path actually lowers.
- Status: open

### Issue 6 -- Severity: nit
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:763-766`
- Description: Only `AssignEffect` with `Target is PropertyAccess` updates `assignedRhs`. Owned / other assign targets leave conditions unsubstituted (pre-assign bag). Unlikely for current DSL assign surface.
- Suggestion: If owned assigns appear in guards, extend tracking or fail-loud that probe substitution does not cover them.
- Status: open

## Oracle / verification (read-only)

- PR facts: title/body/CI SUCCESS/MERGEABLE CLEAN/4 files — `gh pr view 65`.
- Tip SHA matches worktree HEAD (before this review-doc commit).
- Scratch `DomainToCSharpExporter` + `CSharpGenerator` + `InvokeAction` on this SHA (throwaway `/tmp/pr65-export-dump`, not product): Occupy / AssignFalse / else `if (!false)` / nested / else-if as above.
- `dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false` this session: **2809** pass, **0** fail, **0** skip. `--treenode-filter '/*/*/*/*/*Item4FailBeforeMutate*'` did not isolate (ran the whole suite). Did not implement or fix.

## Checklist

- [x] Diff collected; scope drift noted (none beyond claimed 4 files)
- [x] Stance: adversarial re-verify; assume wrong; not implementer
- [x] Producer/consumer: assignedRhs → probe condition Lower → ProbeCreate prefix → module/export/InvokeAction
- [x] Sibling-path check done (flat Occupy vs nested/else-if vs AssignFalse vs Unique restore vs stage entry)
- [x] Reachability → severity for fail-before-mutate vs fail-after-mutate
- [x] Invariant comments checked against nested sibling
- [x] Primary evidence recomputed (diff, `git show efa5ab7b:`, Restore gate, export dump, InvokeAction)
- [x] Oracles audited (order-only vs Failure+bag+order; nested untested)
- [x] Review + follow-ups written; Razor files not overwritten
- [x] Pass B N/A (mode re-verify, not multi)
