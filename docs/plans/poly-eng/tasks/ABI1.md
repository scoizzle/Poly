# TASK ABI1 - Simplify the VM ABI (one codec, one frame protocol, one ring discipline)

Status: planned. Branch `slice/abi1`. Lane infra. Review 1. Implement mill: OpenCode (review on the opposite mill).
Plan card: this file (infra track; admitted by Scot 2026-10-19).

## Scope
The VM ABI is four conventions spread across ~4,300 lines of `DirectVmAbiEmitter.*`,
each duplicated between compile time and runtime. No behaviour change; the existing
suite is the oracle. Four slices:
(0) delete dead ABI machinery (`Word`/`CallStackFrame`/`CallStack`/`RunSimulation`,
`VmValueMarshaller`, `AbiCtx` small-array + frame-local-array tracking, the write-only
compile-time frame simulator, and the four unreachable `TryGetFrameLocalBase` branches);
(1) `RingCodec` becomes the sole value-classification and conversion path, replacing
`AbiValueTypes`, `TryValueToLong`, `VmState.ToRing`, `IsDoubleValue`, `ConstantSlotKind`,
`ConvertClrToRing`/`ConvertRingToClr`, and the runtime `BoxToAbi`/`CastAbi`/`TypeAsAbi`/`UnboxNullableToLong`;
(2) `VmCall` becomes the one call-frame protocol; `InvokeHeapClosure` and `InvokeAstFunction`
collapse onto it and the emitted SetArgs frame path drops its always-dead 2-word header push;
(3) `AbiCtx.RingRegion` (`ctx.OpenRing()`) retires the manual `RingDepth - 1` /
`FoldResultToSlot` fold protocol at every call site.

## Files
- `Poly/Interpretation/Vm/RingCodec.cs` (new), `VmCall.cs` (new)
- `Poly/Interpretation/Vm/DirectVmAbiEmitter.{cs,AbiCtx,Expressions,Statements,Invoke}.cs`
- `Poly/Interpretation/Vm/VmState.cs`
- `Poly/Interpretation/AbiValueTypes.cs` (deleted), `Poly/Interpretation/Vm/VmValueMarshaller.cs` (deleted)
- `Poly/Interpretation/Analysis/Semantics/{ValueRepresentationPass,SyntaxTypeCompatibilityAnalyzer}.cs` (use `RingCodec.IsInline`)
- `Poly.Tests/Interpretation/RingCodecTests.cs` (new), `InterpretationStabilizationTests.cs` (drop the `Word` test)
- `Poly/Interpretation/README.md`, `Poly/Interpretation/Vm/README.md`

## Shape matrix
| Concern | Before | After | Oracle |
|---|---|---|---|
| Value classification | 6 overlapping mechanisms (`AbiValueTypes`, `ValueRepresentationMetadata`, `IsDoubleValue`, `TryValueToLong`, `VmState.ToRing`, `ConstantSlotKind`) | one `RingCodec` | `RingCodecTests`, full suite |
| Ring↔CLR conversion | `ConvertClrToRing`/`ConvertRingToClr`/`HoleToClrObject`/`BoxToAbi`/`CastAbi`/`TypeAsAbi`/`UnboxNullableToLong`, plus a dead `VmValueMarshaller` | `RingCodec.FromClr`/`ToClr`/`ToRing`/`Cast`/`TypeAs`/`UnboxNullable` | full suite (VM, interpreter, domain lowering) |
| Call frame | 2-word header re-derived in 4 places; `InvokeHeapClosure` ≈ `InvokeAstFunction` | one `VmCall` layout + `Invoke` | `ClosureVmTests`, `SuspendResumeVmTests`, `LambdaParameterVmTests`, `AstMemberVmTests` |
| Ring depth | ~25 manual `FoldResultToSlot` / `RingDepth - 1` sites | `ctx.OpenRing()` regions | full suite |
| Dead code | ~450 lines of unreferenced ABI machinery | removed | build clean |

## Done when
- `RingCodec` is the only place value representation is classified or converted; no `AbiValueTypes`, `IsLongRepresentable`, `TryValueToLong`, `ConstantSlotKind`, `ConvertClrToRing`, `ConvertRingToClr` remain.
- The frame layout lives only in `VmCall`; `InvokeHeapClosure`/`InvokeAstFunction` delegate to it.
- `FoldResultToSlot` no longer exists; fold sites use `RingRegion`.
- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` clean; full `Poly.Tests` suite green with no public-surface change (`VmProgram`, `VmState`, `SetArgs` signatures unchanged).

## SHIP if / NOT SHIP if
SHIP when the full suite is green and the diff is behaviour-preserving (no test expectation changed except the deleted `Word` test, which was expressly slated for purge with `Word`). NOT SHIP on any public-surface change, any lowered-shape change, or any new fail-open path.

## Tests
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/RingCodecTests/*'`, then the full suite.

## Hand-edit
Each convention now has one named home (`RingCodec`, `VmCall`, `AbiCtx.RingRegion`); a cold reader changes a value kind, a frame layout, or a fold in one file.

## Needs Scot
none (admitted 2026-10-19).

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-19 | OpenCode | local | uncommitted | Slices 0-3 implemented on `9838889a`; `dotnet build Poly.Benchmarks` clean, `Poly.Tests` 4149/4149 green | none yet |
| 2026-10-19 | verifier | local | uncommitted | Verify round 1: 1 🟠, 2 🟡, 1 🟢. Fix round: reserved SetArgs parameter slots (+ red/green test), `RingCodec` internal, `IsIeee` exact, docs updated. Full suite 4150/4150 green. Re-verify: all closed, no new finding | 🟠 SetArgs locals clobbered a parameter; 🟡 public codec type; 🟡 nullable IEEE classification; 🟢 stale marshaller docs |
| 2026-10-19 | reviewer | local | PR #239 | Review: no runtime defect. Applied both suggestions: ABI reference/README now describe `VmCall` (not `CallStack`), and `OpenRing`/`RingRegion` are internal. Build clean, 4150/4150 green | 2 suggestions |
