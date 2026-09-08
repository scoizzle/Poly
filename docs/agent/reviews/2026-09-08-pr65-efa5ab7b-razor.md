# PR 65 — Item 4 fail-before-mutate ProbeCreate prefix (Razor) — 2026-09-08

- **Target**: PR 65 https://github.com/scoizzle/Poly/pull/65 · tip SHA `efa5ab7bac3bd99bccb12082dd2ffb812281dab5` · branch `fix/sequential-failure-fail-before-mutate` (worktree `review/razor-pr65-efa5ab7b`)
- **Mode**: standard (adversarial; reviewer is not the implementer; reviews only)
- **Issue counts**: 0 bugs, 4 suggestions, 1 nit
- **Verdict**: **ship** — Occupy-shaped assign-then-illegal-create fails before mutate via substituted ProbeCreate prefix on the same Lower tree for simulate + C# print; not DEI `RestoreActionState` theater
- **Process notes**: Prior “documented miss” oracles that only asserted bag rollback after Unique Failure were ambiguous with `RestoreActionState` (Unique-only). Item 4 correctly retargets a **range** Failure (`Nights`) plus ProbeCreate-before-assign order so OpenStays==0 cannot be restore.

## Summary

Diff is four files: `EffectLoweringPass` replaces `ConditionReadsAssignedProperty` skip with `SubstituteAssignedProperties` (property→prior-assign RHS) for guarded ProbeCreate prefixes; `Item4FailBeforeMutateTests` (order-only); `ActionEntityReturnTests` retarget of `IfOnMutatedProperty` (Failure + OpenStays==0 + module order); exporter test retarget to ProbeCreate-before-OpenStays-assign. No `DomainEntityInstance` / `RestoreActionState` product diff. CI SUCCESS, MERGEABLE, CLEAN.

Primary evidence (this pass): exported C# for Occupy is `if (this.OpenStays + 1L >= 1L) { ProbeCreate... }` then `OpenStays = OpenStays + 1L`; AssignFalse is `if (false) { ProbeCreate... }`; multi-assign chain `A=1; B=A+1; if (B>=2)` probes `1L + 1L >= 2L`; else-after-assign-false probes `if (!false)`. Runtime `RestoreActionState` still runs **only** when `ErrorMessage` contains `"Unique"` (`DomainEntityInstance.cs:601-602`); the retargeted Occupy test fails on **Nights** range, so OpenStays==0 is fail-before-mutate, not restore.

## Issues

### Issue 1 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:2388-2416`
- Description: `Export_IfOnMutatedProperty_ProbesBeforeOpenStaysAssign` only asserts ProbeCreate index before OpenStays assign. An unguarded ProbeCreate before assign would still pass and would break AssignFalse ConditionDrift (always probe). Comment claims “condition lowered post-prior-assign for probe only” but the test never asserts `OpenStays + 1` (or equivalent) in the probe guard. Scratch export on this SHA shows the correct guard; the suite does not lock it.
- Suggestion: Assert the Book method substring before the first ProbeCreate contains `OpenStays + 1` (or `OpenStays + 1L`) and `>= 1`. Add a sibling export test for AssignFalse that expects `if (false)` (or equivalent constant-false) wrapping ProbeCreate.
- Status: open

### Issue 2 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/Lowering/Item4FailBeforeMutateTests.cs:17-80`
- Description: Both Item4 tests only assert ProbeCreate-before-OpenStays-assign order in the lowered/module tree. They do not assert runtime `Succeeded==false`, `ErrorMessage` contains Nights, `OpenStays==0`, or empty `CreatedChildren`. The strong runtime+order oracle lives only in `ActionEntityReturnTests.InvokeAction_IfOnMutatedProperty_CreateIllegal_DoesNotApplyPriorAssign` (`:1194-1235`). Item4 alone cannot carry the “fail-before-mutate” claim if that retarget drifts.
- Suggestion: Either promote the ActionEntityReturnTests assertions into Item4 (simulate + print same body) or have Item4 call through InvokeAction with nights=0 and assert Failure + OpenStays==0 in addition to order.
- Status: open

### Issue 3 -- Severity: suggestion
- File: `Poly.Tests/DomainModeling/ActionEntityReturnTests.cs:702-737`
- Description: `InvokeAction_ConditionDrift_AssignThenIfCreate_DoesNotApplyPriorAssigns` uses Unique Failure. On Unique, `RestoreActionState` still runs (`DomainEntityInstance.cs:601-602`). Observables Create==false / Occupied==0 are therefore ambiguous between fail-before-mutate and restore theater if the probe prefix regresses. Unlike the Nights Occupy retarget, this test does not assert ProbeCreate-before-assign order.
- Suggestion: Add the same FlattenSyntax ProbeCreate-before-assign (Create/Occupied) order checks used in the IfOnMutatedProperty retarget, or switch the illegal create to a non-Unique constraint so restore cannot explain the bag.
- Status: open

### Issue 4 -- Severity: suggestion
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:787-791`
- Description: `SubstituteAssignedPropertiesRewrite` replaces every `PropertyAccess` whose name is in `assignedRhs`, including names inside `AnyExpr`/`AllExpr`/`NoneExpr`/`CountExpr` bodies and `RelationshipNavigation.TargetProperty`. That is correct for subject-property conditions (Occupy / AssignFalse) but can mis-substitute same-named related-entity properties when store-aware quantifiers become lowerable. Today `DomainExpressionLoweringPass.AnyExpr` throws NotSupported on the VM path, so this is latent rather than a live wrong taken-ness on supported domains.
- Suggestion: When quantifier lowering lands, substitute only subject-rooted PropertyAccess (or skip substitution under quantifier/relationship scopes). Add a regression domain: assign subject `Active`, `if (any rel where Active) { create }` and assert related `Active` is not rewritten to the subject RHS.
- Status: open

### Issue 5 -- Severity: nit
- File: `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:763-766`
- Description: Only `AssignEffect` with `Target is PropertyAccess` updates `assignedRhs`. Owned / other assign targets leave conditions unsubstituted (pre-assign bag). Unlikely for current DSL assign surface; document or track if owned assigns become common in guards.
- Suggestion: If owned assigns appear in authoring, extend tracking or fail-loud that probe substitution does not cover them.
- Status: open

## Sibling-path check

| Semantic | Paths | Fix/invariant on all? | Test forces path? |
|---|---|---|---|
| Occupy assign-then-if-create illegal | Module body ProbeCreate with subst condition; export same; InvokeAction | Yes — CS `OpenStays+1 >= 1` then assign; runtime Nights Failure leaves OpenStays 0 | Yes — ActionEntityReturnTests + Item4 order + exporter order |
| AssignFalse ConditionDrift | Probe `if (false)`; body assign false; if not taken | Yes on export scratch + runtime `AssignFalseSkipsCreate_Succeeds` | Runtime yes; export/Item4 **no** |
| Else-branch probe | `Not(substituted)` (`EffectLoweringPass.cs:820-823`) | Yes — scratch `if (!false)` ProbeCreate | Export_ElseIfCreate order only; no AssignFalse-else Item4 |
| create vs create-in | `LowerCreateEntityInstanceProbe` / `LowerCreateInProbe` | Same CollectCreateInProbes walk | create in Item4/Occupy; create-in in ConditionDrift Unique |
| Nested composites / nested if | Composite recurse same `assignedRhs`; then/else fresh dict for inner assigns | Scratch nested if (OpenStays subst outer, nights param inner) OK | No dedicated Item4 nested/multi-assign test |
| Multi-assign RHS chains | `assignedRhs[name] = Substitute(value, assignedRhs)` | Scratch `1L+1L >= 2L` | No suite test |
| Unique Failure restore sibling | `RestoreActionState` Unique-only | Unchanged by this PR; not the Item 4 path | ConditionDrift Unique still soft without probe-order |

## Reachability

No new throw on valid Occupy/AssignFalse domains. `DomainExpressionRewriteBase.Default` remains fail-loud for unhandled expression subtypes (`DomainExpressionRewriteBase.cs:28-31`); closed switch covers temporal leaves. Quantifier-in-condition after assign is unsupported on VM lower pre- and post-change (body would not lower either).

## Invariant-stating comments

- “Same tree for simulate and emit (Item 4 fail-before-mutate)” (`EffectLoweringPass.cs:695-697`) — holds: `LowerActionBody` → module cache and exporter.
- “Occupy-shaped … probes taken, and AssignFalse ConditionDrift probes constant false” (`:739-742`) — holds on exported C#; AssignFalse suite coverage is runtime-only.
- Test comment “Oracle is module ProbeCreate-before-assign, not DEI restore” (`ActionEntityReturnTests.cs:1197`) — holds; Nights Failure bypasses Unique restore.

## DEI / RestoreActionState

- Diff mentions DEI/restore only in comments (tests). No `RestoreActionState` / `DomainEntityInstance` hunks in PR 65.
- `RestoreActionState` remains Unique-gated (`:601-602`). Occupy Nights oracle cannot be explained by restore.

## Oracle / verification (read-only)

- PR facts: title/body/CI SUCCESS/MERGEABLE CLEAN/4 files — verified via `gh pr view 65`.
- Tip SHA `efa5ab7bac3bd99bccb12082dd2ffb812281dab5` matches worktree HEAD.
- Scratch `DomainToCSharpExporter`+`CSharpGenerator` on this SHA: Occupy / AssignFalse / chain / else / nested / create-in as above.
- Focused test list present: Item4 (2), IfOnMutatedProperty retarget, AssignFalseSkipsCreate, Export_IfOnMutatedProperty. Full `Poly.Tests` green in this environment with `/p:NuGetAudit=false` (NU1903 otherwise WarnAsError).

## Checklist

- [x] Diff collected; scope drift noted (none beyond claimed 4 files)
- [x] Stance: adversarial / assume wrong
- [x] Producer/consumer: assignedRhs → probe condition Lower → ProbeCreate prefix → module/export
- [x] Sibling-path check done
- [x] Reachability for fail-loud / restore gating argued
- [x] Invariant comments checked
- [x] Primary evidence recomputed (diff, RestoreActionState gate, export dump)
- [x] Oracles audited (order-only vs Failure+bag+order)
- [x] Review + follow-ups written under `docs/agent/reviews/`
- [x] Pass B N/A (mode standard)
