# PR 57 — Final Boss phenomenal-review — 2026-09-06

- **Target**: PR 57 https://github.com/scoizzle/Poly/pull/57 vs `origin/master` (branch `fix/lower-action-body-named-execute`)
- **Mode**: re-verify (not rubber-stamp). Razor claims treated as claims to prove or reject from this SHA.
- **Model**: grok-4.6
- **Protocol**: `docs/agent/phenomenal-review.md`
- **SHA**: `6cb49aba155707ff3b6d76c918d79f3d28a6cf1f` (`git rev-parse HEAD` matched; product HEAD not changed during review)
- **PINNED**: `/workspace/Poly-pr57-6cb49aba`
- **Evidence**: `git show 6cb49aba:PATH` and `git -C /workspace/Poly-pr57-6cb49aba diff origin/master...HEAD`
- **Scope**: 9 files, +121/−47 (`DomainEntityInstance.cs` / `.Runtime.cs` / `.HostAbi.cs` + tests). No scope drift.
- **Issue counts**: 0 bugs, 0 suggestions, 0 nits
- **Verdict**: ship
- **Prior Razor**: `docs/agent/reviews/2026-09-06-pr57-6cb49aba-razor.md` — ship, 0 bugs, 0 suggestions, 0 nits. Independently re-checked against this SHA; not chain-trusted.

## Summary

PR 57 closes the named-execute dual-path residual: when `ExecuteEffectList` is called with `actionName` set and a non-empty effect list, it binds the cached module method or throws (Domain-null or missing method). It never calls `LowerActionBody`. Subscriptions, transition batches, and missing OnEntry still lower at execute. Action-level `require` names are stubbed on the runtime TypeDef so module `this.PolicyName()` type-checks. Oracle `InvokeAction_MissingModuleMethod_Throws_DoesNotRelower` strips `Lot.Issue` from the cached module and asserts throw + zero children. Optional `dotnet run` this session: 2788 passed, 0 failed (treenode-filter did not isolate the named test; full suite ran). No product wrong-outcome on the claimed named vs non-named split.

## Claim disposition (this SHA, primary evidence)

### Named `actionName` never `LowerActionBody` — **PROVEN**

`ExecuteEffectList` (`Poly/DomainModeling/Runtime/DomainEntityInstance.cs:652-663`): `if (actionName is not null)` — Domain-null throws `"without a Domain-bound module"` (`:654-656`); missing method or null `Body` throws `"Module method '{actionName}' is missing"` (`:659-662`); else `tree = BindModuleMethodBody(method)` only. No `LowerActionBody` in that branch.

`InvokeActionInternal` is the only producer of `actionName` (`:563-564`): `actionName: action.Name`.

Baseline (`git show origin/master:Poly/DomainModeling/Runtime/DomainEntityInstance.cs`): Domain-null always fell through to `LowerActionBody` even when `actionName` was set. That sibling is gone.

Oracle: `InvokeAction_MissingModuleMethod_Throws_DoesNotRelower` (`Poly.Tests/DomainModeling/Compile/PipelineTransformationTests.cs:128-161`) mutates the cached `Lot` method list (projection stores `List<MethodDefinitionNode>`, so `is IList` holds), asserts `TryGetModuleMethod` false, `InvokeAction("Issue")` throws containing `"Module method 'Issue' is missing"`, `CreatedChildren.Count == 0` (no Effect-IR create-in). Sibling Domain-null named tests now expect the module-required throw (`DomainEntityInstanceTests.cs:281-286`, `:361-363`).

Empty effect lists return at `:648-649` before the named/non-named split — neither bind nor `LowerActionBody`. Pre-existing; no Effect-IR fallback. Not a dual-path hole.

### LowerActionBody remains only for non-named paths — **PROVEN**

`rg ExecuteEffectList(` in `*.cs` is four call sites:

| Caller | `actionName` | Tree source |
|--------|--------------|-------------|
| `InvokeActionInternal` (`DomainEntityInstance.cs:563-564`) | `action.Name` | bind or throw |
| `ApplyInitialStageEntryEffects` (`:255-256`) | unset; `entryStageName` | module OnEntry if present, else `LowerActionBody` (`:668-675`) |
| `RunTransitionEffectList` (`HostAbi.cs:207`) | unset | `LowerActionBody` (`:675` or `:679`) |
| `ExecuteSubscriptionEffects` (`HostAbi.cs:280`) | unset | `LowerActionBody` (`:675` or `:679`) |

Non-named Domain-bound: `else if (Domain is not null)` (`:665-676`) — OnEntry preference then `LowerActionBody`. Domain-null non-named: `:678-679` `LowerActionBody`. HostAbi comment narrowed (`HostAbi.cs:196-199`): mixed if+create in entry/exit “still compiles via `LowerActionBody` (not a named-action path).” Matches PR stop / ontology residual (subscriptions / transition batches / missing OnEntry). Vacuous `TryGetEntryMethod` OnEntry preference (`:668-671`) is prior suggestion class (PR 51 F2), not this slice.

### Action-level require stubs — **PROVEN**

`EnumerateTypeDefPolicies` (`Poly/DomainModeling/Runtime/DomainEntityInstance.Runtime.cs:309-320`) yields action-level policies after entity+stage, stripping `not_` so `require not Foo` stubs `Foo`. `BuildTypeDefNode` (`:250-257`) adds `bool` methods from that enumerator. Needed for programmatic action-only policies (`CreatePersonEntity` `IsActive` lives on the action, not `entity.Policies`) so `CompileChecked` of `BuildActionBodyWithGuards` (`DomainToCSharpExporter.Actions.cs:229-246`) resolves `this.IsActive()`. DSL `require` always copies an entity policy (`PolyDslParser.cs:309-329`); first-match in `ResolvePolicyForNamedInvoke` (`InvokeNamed.cs:76-81`) stays the entity policy. Guards still run via `EvaluatePolicy` before execute (`DomainEntityInstance.cs:511-517`).

## Sibling-path check

| Path | Named-execute invariant (bind module or throw; never `LowerActionBody`) | Test forces this path? |
|------|------------------------------------------------------------------------|-------------------------|
| `InvokeActionInternal` → `ExecuteEffectList(..., actionName: action.Name)` with effects (`DomainEntityInstance.cs:563-564`, `:652-663`) | Holds. Domain-null throw; missing method throw; else `BindModuleMethodBody`. | Yes — `DoesNotRelower` (`PipelineTransformationTests.cs:128-161`); Domain-null (`DomainEntityInstanceTests.cs:261-287`, `:350-364`); happy module identity (`PipelineTransformationTests.cs:58-87`, `:90-126`) |
| Nested `InvokeNamed` → `InvokeAction` (self/cross-entity) | Same named path; Domain required. | Yes — `SelfInvokeHostAbiTests.cs:55-64` (now Domain-bound) |
| Empty `action.Effects` (`ExecuteEffectList` `:648-649`) | Returns null; no bind, no `LowerActionBody`. Cannot re-lower Effect IR. | Indirect — arg-fail tests return before execute; no Domain-null empty-body throw test (not a dual-path miss) |
| First-stage OnEntry (`:255-256`, `entryStageName` only) | Not named. Module entry if present, else `LowerActionBody`. | Yes — `Create_AppliesFirstStageEntryEffects` (`DomainEntityInstanceTests.cs:3667-3683`) Domain-null OnEntry still applies via `LowerActionBody` |
| Transition entry/exit batch (`HostAbi.cs:201-220`) | Not named. `LowerActionBody`. | Pre-existing OnEntry/OnExit invoke tests (`DomainEntityInstanceTests.cs:1896-1936`) |
| Subscriptions (`HostAbi.cs:255-281`) | Not named. `LowerActionBody`. | Pre-existing subscription tests (not this PR) |
| C# export `LowerActionToMethodBody` (`DomainToCSharpExporter.Actions.cs:444`) | Populate/print, not execute. Out of this PR’s stop. | n/a |

Invariant comment (`DomainEntityInstance.cs:633-638`): “Named actions always bind … never `LowerActionBody`.” Holds on every `actionName is not null` branch. Empty-list return is before that branch.

Reachability of new throws (`:654-656`, `:659-662`): Domain-null named invoke with effects is reachable on standalone tests (now asserted). Missing module method is reachable only on a stripped/corrupt module (test constructs it); after normal `GetOrLower` the method exists. Fail-loud on corrupt module is the intended stop, not a valid-domain throw.

## Issues

None.

## Optional verification (read-only)

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -p:NuGetAudit=false -- --treenode-filter '/*/*/*/*/*DoesNotRelower*'
total: 2788, failed: 0, succeeded: 2788, skipped: 0
```

Treenode-filter did not isolate the name (full suite ran). `InvokeAction_MissingModuleMethod_Throws_DoesNotRelower` is in `--list-tests`. No failures. Not used as the sole proof; source + named tests above are.

## Checklist

- [x] Diff collected vs `origin/master`; 9 files; no scope drift
- [x] Stance: adversarial re-verify; not implementer; no product/test edits; SHA unchanged during review
- [x] Razor claims re-dispositioned from `git show 6cb49aba:` / current tree (no chain-trust)
- [x] Sibling-path check: named vs OnEntry vs transition vs subscription vs empty list vs InvokeNamed
- [x] Reachability → severity for Domain-null / missing-method throws
- [x] Invariant comments checked against all siblings
- [x] Fail-closed oracle strips the module method and asserts no create fallback
- [x] Oracles not weakened (standalone named success tests now expect throw)
- [x] Review + follow-ups written under `docs/agent/reviews/`
