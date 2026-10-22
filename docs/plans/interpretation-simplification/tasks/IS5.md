# TASK IS5 - one invoke result convention

Status: planned. Branch `slice/IS5`. Lane C (`vm`). Depends on IS3.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §5.

## Why

Six invocation paths (`Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs:24,178,195,239,244,311`, plus `AbiEmitter.LambdaBridge.cs:33-430`) answer "how does a call's result reach the caller" in four ways:

| Convention | Where |
|---|---|
| `RingCodec.FromClr` | `:124,129` |
| `BoxToAbi` | `:256` |
| `Convert(Convert(x, t), long)` | `:250` |
| `Assign(slot, 0L)` | `:204, :765` (and the void arm wrote nothing until IS0e) |

plus three competing "is this argument a scalar or a handle?" predicates: `RingCodec.IsStackScalar` (`RingCodec.cs:120`, throws on unknown), `RingCodec.IsInvokeArgumentScalar` (`:137`, never throws), `ReadsRawSlot` (`Invoke.cs:714`, derives it from the declaration). The same unresolved argument is a hard compile error on one path and a deref on another.

And the frame protocol is hand-rolled at `:262-303` where `VmCall.Invoke` (`Poly/Interpretation/Vm/VmCall.cs:14`, already used at `:428,639`) does it.

This is the highest-risk slice in the program and the last one deliberately: it is the hottest code, and it is only safe once IS2c's corpus and the ISB benchmark exist.

## Scope

- `EmitResult(Type)` — one function that turns a call expression plus its return type into the ring outcome (value, heap handle, or `0L` for `void`). Every one of the six paths calls it. No path writes the ring "by hand" afterwards.
- Delete `RingCodec.IsInvokeArgumentScalar` (the non-throwing variant). "Unknown" has one meaning: a compile error, from `IsStackScalar`. `ReadsRawSlot` stays only if it answers a *different* question (declared parameter type) — if it does, rename it so the difference is the name; if it does not, delete it.
- Use `VmCall.Invoke` in place of the hand-rolled frame save/restore at `:262-303`. If a path genuinely needs different frame semantics, that is a finding: stop and report rather than special-casing.
- No behaviour change to the CLR call boundary; the suite and the parity harness are the oracle.

## Files

- `Poly/Interpretation/Vm/Abi/AbiEmitter.Invoke.cs` — the six paths, `EmitResult`, the frame protocol.
- `Poly/Interpretation/Vm/Abi/RingCodec.cs` — delete the non-throwing predicate.
- `Poly/Interpretation/Vm/Abi/AbiEmitter.LambdaBridge.cs` — the bridge's result path.
- `Poly.Tests/Interpretation/*Invoke*`, `*Lambda*`, `SlotInvariantsTests`, `VmTreeInvariantTests`, `AstParityTests` — expectations.
- `docs/interpretation/vm-abi-reference.md` — the invoke result section, if it describes the old conventions.

## Shape matrix

| Path | Before | After | Test that proves it |
|---|---|---|---|
| CLR call returning a value | `FromClr` | `EmitResult` | existing invoke tests + `AstParity` |
| CLR call returning `void` | `0L` (IS0e) | `EmitResult` writes `0L` | `VoidInvoke_LeavesZero` |
| AST invoke | `Assign(0L)` / `CompileNode` | `EmitResult` | `AstInvoke_ResultIsInTheRing` |
| name-dispatched invoke | `BoxToAbi` | `EmitResult` | the dispatch tests |
| lambda bridge call | `Convert(Convert(...))` | `EmitResult` | `LambdaBridge` tests |
| an argument with an unknown kind | `false` on one path, throw on another | throws (one predicate) | `UnknownArgumentKind_IsACompileError` |

## Done when

- One result function; no path normalises a call result itself.
- One scalar predicate; the "unknown ⇒ silently not scalar" path is gone.
- `VmCall.Invoke` is the only frame protocol.
- The ISB benchmark is recorded: **no median regression above 2% on any of the four programs**; any regression inside tolerance is stated with its number.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: one convention, one predicate, one frame protocol, benchmark inside tolerance.
NOT SHIP: a path keeps its own normalisation "for shape reasons" without a measurement and a named reason; the benchmark regressed and was not discussed; the frame save/restore survives beside `VmCall.Invoke`.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*Invoke*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/*Lambda*/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SlotInvariantsTests/*'
```
Then the full suite, and the ISB benchmark command.

## Hand-edit

One function answers "where does the result go", and every call site is a call to it.

## Needs Scot

none.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
