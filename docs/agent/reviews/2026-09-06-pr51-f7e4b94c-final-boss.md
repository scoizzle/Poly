# PR 51 — Final Boss phenomenal-review — 2026-09-06

- **Target**: PR 51 https://github.com/scoizzle/Poly/pull/51 vs `origin/master` (branch `cursor/pipeline-transformation-1a9d`)
- **Mode**: re-verify (not rubber-stamp). Prior not-ship at `2a362ea4` and the 100x “F9 closed” claim treated as claims to prove or reject from **this** SHA.
- **Model**: grok-4.6
- **Protocol**: `docs/agent/phenomenal-review.md`
- **SHA**: `f7e4b94ccab28f86e07025911d3d5423fa60abfd` (`git rev-parse HEAD` matched before review commit; product HEAD not reset)
- **PINNED**: `/workspace/Poly-pr51-f7e4b94c`
- **Evidence**: `git show f7e4b94c:PATH`, `git -C /workspace/Poly-pr51-f7e4b94c diff origin/master...HEAD`, `git -C /workspace/Poly-pr51-f7e4b94c diff 2a362ea45bd8d91f09d806d3c3f5793f7cd19037...HEAD`
- **Parents**: `2a362ea45bd8d91f09d806d3c3f5793f7cd19037` (prior Final Boss not-ship, F9)
- **Issue counts**: 1 bug, 1 suggestion, 0 nits
- **Verdict**: not ship
- **Prior Final Boss**: `docs/agent/reviews/2026-09-06-pr51-2a362ea4-final-boss.md` — not ship, F9. Independently re-checked; not chain-trusted. Do not overwrite those files. F8 remains a non-blocking suggestion.

## Summary

`f7e4b94c` is a two-file F9 patch on `2a362ea4`: `BindCreate` now emits `_collection.Add` when `outs.Count == 1`, and `Export_CreateType_UnambiguousManyRel_EmitsCollectionAdd` slices **BindCreate** (contains `_fines.Add` + `Fine.Create(`) versus **AssessByType** (`this.Create`, does **not** contain `_fines.Add`). That closes the merge false-green (CreateFines no longer satisfies the oracle) and the outbound collection hole. F1 BindThis `Success()` / `Failure(msg)` / adapter `Return(Failure)` still hold (runtime files empty vs `2a362ea4`; Pay dump). HostAbi Type-create still links outbound + reverse (`fines=1 reverse=1`); ambiguous still links neither.

The 100x claim that F9 also closed **reverse** (`backs.Count == 1` → `this` into `Fine.Create`) is **false** on this SHA. `wireUnambiguousBackRef` is wired only onto ESM `IsBackReference`, which is **self-rel only** (`EntityStructureAnalyzer.cs:125-130`). `Fine.patron` is a cross-entity singular nav, so BindCreate still emits `Fine.Create(amount, reason, values["patron"] or null)` then `this._fines.Add`. Generated C# is one-way; simulate is two-way. The new comments at StoreBind `:105-106` and `:271-273` state HostAbi reverse parity — they are a lying invariant (§3.9). That is F10. Do not ship until reverse is actually `this` (same hook as `FindAutoWireBackReference` / CreateFines) **or** the comments and F9 claim are narrowed to outbound Add only and the F5 oracle asserts that honestly.

`git diff 2a362ea4...HEAD` is 2 files, +90/−12. `rg LowerStageTransitions` / `PreprocessRuntimeKeyword` in `*.cs` are empty.

## Claim disposition (this SHA, primary evidence)

### F9 outbound Add + F5 oracle — **CLOSED**

`git show f7e4b94c:Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs`:

- BindCreate `:107-117` uses the same many-rel filter as `HostAbi.TryAutoLinkUnambiguousOutbound` (`:680-686`): `OneToMany`/`ManyToMany` + ordinal target type name; `autoLink = outs.Count == 1`.
- On autoLink, after `Fine.Create` succeeds: `_fines.Add((Fine)created)` then `Success(created)` (`:127-150`). Field formula `_{ToCamelCase(ToPascalCase(outs[0].Name))}` matches exporter backing fields (`DomainToCSharpExporter.cs:237-243`).
- Ambiguous (`outs.Count != 1`) uses `RewrapObjectResult` with no Add (`:152-154`).

F5 test `DomainToCSharpExporterTests.cs:1972-2023` no longer asserts `cs.Contains("_fines.Add")` on the whole compilation unit. It takes `BindCreate(string typeName` … `BindCreateIn(string` (method order: `AddStoreBindMethods` emits BindCreate then BindCreateIn at `StoreBind.cs:45-47`, after CreateFines from the nav loop) and asserts that slice contains `_fines.Add` and `Fine.Create(`. AssessByType is brace-matched and must contain `this.Create(` and **not** `_fines.Add`. CreateFines (`Notify.cs:219-223`) cannot false-green that pair.

Read-only export dump at this SHA (`#:project` Poly, F5 domain):

```
BindCreate Fine arm: Fine.Create(..., values["patron"] or null); this._fines.Add((Fine)created);
AssessByType contains this.Create: True
AssessByType contains _fines.Add: False
CreateFines: Fine.Create(amount, reason, this); this._fines.Add(fine);
AMBIG BindCreate _fines.Add: False
AMBIG BindCreate _waived.Add: False
```

Guide `poly-dsl-guide.md:73-79` “C# export likewise emits `_fines.Add`” is now true of BindCreate’s Fine arm (not of the AssessByType body — Add lives in the host bind, which is the intended PR 51 re-home). Did not restore `_lowerStageTransitions`.

### F9 reverse `this` / 100x claim — **OPEN as F10 (bug)**

Rejected. See Issue 1.

`backs` is computed (`:112-116`, same filter as `TryLinkCreateInBackReference` `:698-702`) and `wireUnambiguousBackRef: autoLink && backs.Count == 1` is passed (`:118-120`). `BuildTargetCreateArgs` only consults that flag inside `if (parameter.IsBackReference)` (`:270-277`). `IsBackReference` is set only when `rel.Target.TypeName == entity.Name` (`EntityStructureAnalyzer.cs:125-130`). Fine.patron is not a self-rel. For self-create, `source.Name == target.Name` already passed `this` before this patch — the new flag is redundant there and dead on the F9 Patron/Fine domain.

Create-in sibling still auto-wires reverse: `FindAutoWireBackReference` (`Actions.cs:841-856`) + `Fine.Create(..., this)` in CreateFines (dump). HostAbi dump: `TYPE fines=1 reverse=1`. Export Type-create reverse remains null.

### F1 — adapter fail-closed — **CLOSED** (holds)

`git diff 2a362ea4 HEAD -- Poly/DomainModeling/Runtime/DomainEntityInstance.cs` is empty.

- Adapter: `:819-825` — `when AdapterTypeName(inv) is { } adapter` → `new Return(new Invoke(Member(NamedTypeReference("DomainResult"), "Failure"), Constant("Contract endpoint '{contract}.{endpoint}' has no in-process adapter on simulate.")))`.
- `AdapterTypeName` `:896-900` requires the receiver type name to end with `"Adapters"`.
- Pattern order: Success `:807-811`, Failure `:812-818`, **then** Adapter `:819-825`.

Oracles unchanged: `CrmDogfoodTests.cs:210-219` Capture `Succeeded == false` + `"Billing.Charge"`; `PipelineTransformationTests.cs:173-203` Shop Pay `Succeeded == false`, message contains `"Stripe.Charge"` and `"no in-process adapter"`.

Read-only Pay dump at this SHA: `PAY succeeded=False err=Contract endpoint 'Stripe.Charge' has no in-process adapter on simulate.`

Export sibling (intentional dual-path): `DomainToCSharpExporter.Actions.cs:356-374` prepends `{Contract}Adapters.{Endpoint}(...)`; `:383-401` `BuildContractAdapterTypeDef` method body is `throw new NotImplementedException(...)`.

Reachability (§3.2b): **valid domain, valid inputs** — CRM Capture and Shop Pay.

### BindThis DomainResult arity — **OK**

Success → zero-arg `DomainResult.Success()` (`:807-811`). Failure keeps rebound args (`:812-818`). Create is not dropped: `EffectLoweringPass.LowerRuntimeFactoryCall` (`:901-917`) is assignment + Failure unwrap **before** the Success wrap. Export still appends `Success((T)createdN)` (`Actions.cs:301-308`). BindThis comment at `:803-805` remains stale (F8).

### F8 — Success value discard vs CreatedChildren / export Success(value) — **still open (suggestion)**

Not a ship blocker. See Issue 2.

### Pipeline simulate / HostAbi Type-create — **holds**

Runtime / EffectLowering / Actions / guide are empty vs `2a362ea4`. `CreateCore` else-arm (`DomainInstanceStore.cs:224-227`) still calls `TryAutoLinkUnambiguousOutbound` (`HostAbi.cs:678-688`). Store-null sibling `CreateChildInstance` `:635-640` uses the same helper.

Read-only HostAbi dump at this SHA:

```
TYPE succeeded=True child=Fine fines=1 reverse=1
AMBIG succeeded=True child=Fine fines=0 reverse=0 waived=0
```

MCP `TypeOnly_UnambiguousManyRel_ListsAndLinks` (`SimulateCreateDogfoodTests.cs:98-117`) still asserts both directions. `TypeOnly_AmbiguousManyRel_ListsButDoesNotLink` (`:183-227`) still forces `outs.Count != 1`.

## Sibling-path check

| Semantic | Paths | Invariant on all? | Test forces this sibling? |
|----------|-------|-------------------|---------------------------|
| Unbound contract adapter fail-closed | BindThis Adapter arm (`DomainEntityInstance.cs:819-825`) on named `session.Lower` body; export adapter TypeDef throws (`Actions.cs:383-401`) | Simulate: Failure with contract.endpoint. Export: throw. Unchanged vs `2a362ea4` | Yes — Capture (`CrmDogfoodTests.cs:210-219`); Shop Pay (`PipelineTransformationTests.cs:173-203`) + Pay dump |
| BindThis Success/Failure arity | Success strip `:807-811`; Failure keep-args `:812-818`; HostAbi/Store `DomainResult.Success(child)` is CLR, not BindThis | Module bodies compile against non-generic CLR `DomainResult`. Host Success(value) still carries the child for factory returns | CRM walk + Pipeline cached-tree tests. No test asserts VM `DomainResult.Value` on a typed action (CreatedChildren is the simulate contract) |
| Create Type auto-link **outbound** | Store-bound: `HostAbi.Create` → `Store.Create` → `CreateCore` `:224-227`. Store-null: `CreateChildInstance` `:635-640`. Export host bind: `this.Create` → `BindCreate` `:107-150` → `_fines.Add` when `outs.Count == 1` | **Yes for outbound.** Simulate `Store.Link`. Export BindCreate Add. Field name matches exporter `:237-243`. Ambiguous both no-op | Simulate yes — Type-only MCP + HostAbi dump. Export **yes now** — F5 inspects BindCreate not CreateFines; dump `AssessByType contains _fines.Add: False` and BindCreate Fine arm has Add |
| Create Type auto-link **reverse** (`Fine.patron`) | HostAbi `TryLinkCreateInBackReference` after outbound (`:688`, `:696-706`). Export CreateFines `FindAutoWireBackReference` + `Fine.Create(..., this)` (`Notify.cs:126-141`, dump). Export BindCreate `wireUnambiguousBackRef` only on `IsBackReference` (`StoreBind.cs:270-277`) | **No.** Simulate reverse=1. Create-in export passes `this`. BindCreate Type-create passes dict/null. Comments at `:105-106` / `:271-273` claim HostAbi reverse — false | Simulate yes (`:113-116`). Create-in export via CreateFines dump. BindCreate reverse **no** — F5 does not assert `Fine.Create(..., this)` |
| Ambiguous many-rel Type-create | Same `outs.Count != 1` gate on HostAbi and BindCreate | Simulate unlinked. Export BindCreate no Add (dump). Reverse also unset on both (HostAbi returns before back-ref; BindCreate dict/null) | Simulate yes — F6 MCP + dump `fines=0 waived=0`. Export **no** named test (dump only this pass) |
| create-in link | Store-bound: `CreateCore` with `relationshipName` `:210-222`. Export: `BindCreateIn` → `CreateFines` which `_fines.Add` **and** `Fine.Create(..., this)` | Outbound + reverse on the named rel; auto-link helper not used | Yes — Rel-only + combined (`SimulateCreateDogfoodTests.cs:120-157`); CRM `OpenDeal` / `AddContact` |
| One tree / no consumer lowering flag | Named invoke: module method. `CreateEntityInstance` → `LowerRuntimeFactoryCall("Create")` (`EffectLoweringPass.cs:664-668`) | Did not restore `_lowerStageTransitions` | Named path: `PipelineTransformationTests.cs:90-126` |
| Typed action return | Simulate: `CreatedChildren` scan (`DomainEntityInstance.cs:574-590`). Export: `Success((T)value)` (`Actions.cs:301-308`) | Dual-path (F8). Create still runs (assignment before Success) | CRM `OpenDeal` `ResultInstance`; dump AssessByType still `Success((Fine)created1)` |

Invariant-stating comments checked (§3.9):

- `:819` “Fail closed: export adapter throws; simulate must not silent-success” — holds.
- `:803-806` “entity TypeDefs are not assignable-to object under PR53 overload scoring” — **first sentence false** after `55b5a588` (`TypeDefinitionExtensions.cs:119-123`). F8.
- StoreBind `:105-106` “Same outs.Count == 1 rule as HostAbi.TryAutoLinkUnambiguousOutbound; reverse this only when TryLinkCreateInBackReference would” — **outbound true; reverse false** on Fine.patron. **Lying invariant → Issue 1.**
- StoreBind `:271-273` “HostAbi TryLinkCreateInBackReference: unique singular back to source gets this” — the flag does not touch Fine.patron. **Lying invariant → Issue 1.**
- `Actions.cs:277-278` “Type-create may trail with collection.Add (void) after the created local assignment” — still false for AssessByType (Add is in BindCreate). Dead skip, not a valid-input wrong wrap (dump last statement is `Success((Fine)created1)`). Not re-filed.
- `poly-dsl-guide.md:76-77` “C# export likewise emits `_fines.Add`” — **true** of BindCreate Fine arm on this SHA. Runtime sentence still claims reverse; C# sentence does not.

## Issues

### Issue 1 -- Severity: bug
- File: `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs:270-277` (flag pass `:118-120`; comments `:105-106`, `:271-273`; IsBackReference definition `Poly/DomainModeling/Analysis/EntityStructureAnalyzer.cs:125-130`; working create-in hook `FindAutoWireBackReference` `DomainToCSharpExporter.Actions.cs:841-856` + `Notify.cs:126-141`; HostAbi sibling `DomainEntityInstance.HostAbi.cs:678-688` / `:696-706`; F5 oracle `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs:1972-2023` does not assert reverse)
- Description: F9 re-home closed **outbound** Add. Reverse is still the HostAbi / CreateFines sibling, not BindCreate. `wireUnambiguousBackRef` is consumed only for ESM `IsBackReference` (self-relationships). Canonical F9 domain `Fine.patron` is `IsBackReference: false`, so BindCreate emits `Fine.Create(amount, reason, ContainsKey("patron") ? … : null)` then `this._fines.Add((Fine)created)` — dump at this SHA. Simulate `TryLinkCreateInBackReference` still sets Fine.patron (`TYPE reverse=1`). CreateFines still passes `this`. The new comments claim HostAbi reverse parity; they are false advertising (§3.9). F5 cannot catch this: `Contains("_fines.Add")` on BindCreate is true via the Add statement; it never requires `Fine.Create(..., this)`. 100x “reverse when backs.Count==1” is rejected. Reachability: valid Patron+Fine, typed `AssessByType` / `create Fine { … }`. Exported graph is one-way (`Patron.Fines` has Fine, `Fine.Patron` is null); simulate is two-way. Child Rel-exists / `patron` path-prefix disagree across siblings. Not a regression vs `2a362ea4` (reverse was already missing there) — it is the F9 patch claiming a sibling it did not implement.
- Suggestion: Pass `this` for the unique singular nav that `FindAutoWireBackReference(target, source.Name)` would wire (same as CreateFines), **or** delete `wireUnambiguousBackRef` / narrow the comments to outbound Add only and drop reverse from the F9 claim. Harden F5: BindCreate Fine.Create args must include `this` (not `ContainsKey("patron")`) on the unambiguous domain; keep AssessByType `DoesNotContain("_fines.Add")`. Optional: export F6 domain (`fines` + `waived`) and assert BindCreate contains neither `_fines.Add` nor `_waived.Add`. Do not restore `LowerStageTransitions`.
- Status: open
- Reachability: valid Type-create export on the F5 domain. Ambiguous sibling agrees (both unlinked) — not this bug.

### Issue 2 -- Severity: suggestion
- File: `Poly/DomainModeling/Runtime/DomainEntityInstance.cs:807-811` (comment `:803-805`; consume `:574-590`, `:680-682`; export sibling `DomainToCSharpExporter.Actions.cs:301-308`; assignability `TypeDefinitionExtensions.cs:119-123`)
- Description: BindThis still rewrites every `DomainResult.Success(*)` to zero-arg `Success()`, discarding the argument. After `55b5a588`, CLR `object` accepts modeled AST types, so `Success(BindThis(arg))` against `DomainResult.Success(object? value = null)` may compile without stripping. Simulate typed returns ignore that payload. Export still emits `DomainResult<T>.Success((T)value)`. The comment at `:803-805` still claims TypeDefs are not assignable to object — false on this SHA. Create/CreateIn are **not** lost: they are assignments before the Success wrap (`EffectLoweringPass.cs:901-917`). No valid-input wrong outcome found on Capture / Pay / OpenDeal / AssessByType **simulate**. Unchanged vs `2a362ea4`.
- Suggestion: Either keep `Success(BindThis(arg))` now that object accepts entity defs (and drop the stale comment), or state in CORE / `docs/interpretation/domain-execution-model.md` that simulate action returns ignore Success payload and use CreatedChildren only.
- Status: open
- Reachability: valid typed create actions succeed via CreatedChildren today; export and simulate disagree on **where** the value lives, not on whether the child exists. Not a ship blocker. Foreman: do not block on F8 unless a new bug — the new bug is Issue 1, not this.

## Oracle (optional, read-only)

`--treenode-filter` still cannot isolate: `/Poly.Tests/Poly.Tests.DomainModeling.Lowering.DomainToCSharpExporterTests/Export_CreateType_UnambiguousManyRel_EmitsCollectionAdd` ran **zero** tests (exit 8). Did not run the full suite this pass. Did not treat zero-ran as green.

Dumps against this SHA (`#:project` `Poly/Poly.csproj`, empty `Domain("_", [])` seed, not test edits):

```
PAY succeeded=False err=Contract endpoint 'Stripe.Charge' has no in-process adapter on simulate.
TYPE succeeded=True err= child=Fine
TYPE fines=1 reverse=1
AMBIG succeeded=True err= child=Fine
AMBIG fines=0 reverse=0 waived=0
BindCreate Fine arm: Fine.Create(..., values["patron"] or null); this._fines.Add((Fine)created);
AssessByType contains this.Create: True
AssessByType contains _fines.Add: False
CreateFines: Fine.Create(amount, reason, this); this._fines.Add(fine);
AMBIG BindCreate _fines.Add: False
AMBIG BindCreate _waived.Add: False
```

`[Test]` attribute count in `Poly.Tests/**/*.cs`: **2671** (recomputed this SHA; F9 edited an existing test). Did not implement. Did not merge.

## Checklist

- [x] Diff collected vs `origin/master...HEAD` (merge-base `54a4caca`; 74 files, +4479/−1587) and vs `2a362ea4...HEAD` (2 files, +90/−12)
- [x] Stance: adversarial re-verify; not implementer; no product/test edits; SHA `f7e4b94c` unchanged until review commit
- [x] F9 / F5 oracle / F1 / BindThis arity / F8 / HostAbi sibling re-dispositioned from `git show f7e4b94c:` + dumps (no chain-trust)
- [x] Sibling-path table (adapter; Success strip; outbound Add vs reverse this; CreateCore vs CreateChildInstance vs BindCreate vs CreateFines; named module vs re-lower)
- [x] Reachability before severity (F1 valid Capture/Pay; F9 Add closed on valid Type-create export; F10 reverse null on valid Type-create export; F8 dual-path not wrong-outcome on simulate)
- [x] Invariant comments checked against siblings (StoreBind reverse comments fail; guide Add sentence holds)
- [x] Counts recomputed (`[Test]` 2671)
- [x] Review + follow-ups written under `docs/agent/reviews/`
- [x] Prior Final Boss items dispositioned from current source
- [x] Did not overwrite `docs/agent/reviews/2026-09-06-pr51-2a362ea4-final-boss*.md`
