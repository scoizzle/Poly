# TASK IS5 - one invoke result convention

Status: implemented. Branch `slice/IS5`. Lane C (`vm`). Depends on IS3.
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
- The ISB benchmark is recorded: **no median regression above 2% on any of the four programs, measured with `--job medium`**; any regression inside tolerance is stated with its number.
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
| 2026-10-22 | agent | Copilot/VS Code | `759b7e18` | SHIP — one result function (`EmitResult`), one scalar predicate, one caller-side frame protocol; benchmark inside tolerance (worst +0.63 % NQueens) | Findings below. Four result conventions became one; the other invocation paths need none, because a VM callee is entered through `VmCall.Invoke` and its return *is* the ring word. `IsInvokeArgumentScalar` is extinct (`grep -rn IsInvokeArgumentScalar` finds nothing). `ReadsRawSlot` survived as `ParameterReadsRawSlot`. The hand-rolled frame save/restore at `Invoke.cs:262-303` was already removed by #253 — see Finding 5 for what is there now and why `VmCall.Invoke` cannot replace it. Both real defects now have a regression test (`InvokeResultConventionTests` 2/2), each proven red against its pre-fix arm — outputs below. |

### Benchmark (2026-10-22, `759b7e18`)

```
$ dotnet run -c Release --project Poly.Benchmarks -- --filter '*ProgramBenchmarks*' --job medium
```

`Job=MediumRun  IterationCount=15  LaunchCount=2  WarmupCount=10`; Apple M3 Pro, .NET 10.0.12, Arm64 RyuJIT. Before is this branch's HEAD `6d44180c` (the tree untouched); after is `759b7e18`.

| Method | Before Min | Before Median | After Min | After Median | Median Δ |
|--------|-----------|---------------|-----------|--------------|----------|
| NQueens | 163.7 us | 164.2 us | 164.5 us | 165.2 us | +0.6 % |
| Sieve | 96.2 us | 96.6 us | 95.0 us | 96.0 us | −0.6 % |
| Mandelbrot | 122.8 us | 125.1 us | 123.2 us | 124.4 us | −0.5 % |
| Collatz | 129.4 us | 134.4 us | 127.7 us | 132.4 us | −1.5 % |

Two after runs agreed: the repeat read Sieve 96.6, Mandelbrot 125.2, Collatz 133.6 us median (NQueens not captured in that run's tail). Every program is inside the 2 % tolerance; NQueens' +0.6 % is the only regression and is at the size of its own run-to-run spread (its before min/median differ from IS3's readings on the same tree by ~0.5 us).

### Diagnostics (2026-10-22)

```
$ dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
    0 Warning(s), 0 Error(s)

$ dotnet run --project Poly.Tests/Poly.Tests.csproj
  total: 4463 / failed: 0 / succeeded: 4463

$ ... --treenode-filter '/*/*/AstParityTests/*'      -> 145 / 145
$ ... --treenode-filter '/*/*/*Invoke*/*'            -> 33 / 33
$ ... --treenode-filter '/*/*/*Lambda*/*'            -> 49 / 49
$ ... --treenode-filter '/*/*/*RingCodec*/*'         -> 11 / 11
$ ... --treenode-filter '/*/*/SlotInvariantsTests/*' -> 6 / 6
$ ... --treenode-filter '/*/*/*VmTree*/*'            -> 7 / 7
```

### Findings

1. **Four result conventions became one, and two paths needed none.** `EmitResult(call, returnType)` owns the CLR boundaries — the resolved-`MethodInfo` path and the name-dispatched path — including `void` → `0L`. An AST method or a stored closure is entered through `VmCall.Invoke`, whose return value *is* `slots[FramePointer]`; that caller only moves the word into its own slot, and re-encoding it by CLR type would corrupt it (a heap handle is not an `Int64`). So "no path normalises a call result itself" is met by deleting the normalisations, not by routing ring words through the CLR encoder.
2. **`EmitAstInvoke`'s `void` arm is gone because the callee now owns the write.** IS3 kept that arm on the grounds that the caller reserved the slot and nothing else could write it. `CompileFunctionBody` now writes `0L` when the body yields no ring result (it previously left `slots[FramePos]` untouched), which makes the callee half of the frame contract total — the same guarantee, owned by the party that knows. `VoidAstInvoke_LeavesZero` covers a void AST body that yields nothing; `VoidInvoke_LeavesZero` covers the CLR void path.
3. **The name-dispatched path read `float`/`double` returns wrongly, and `EmitResult` fixes it.** Its inline arm was `Convert(Convert(call, t), long)` — a numeric conversion, so a `double`-returning name-dispatched method reached the ring as its truncated integer part. `EmitResult` sends it through `RingCodec.FromClr`, i.e. the IEEE bit pattern the rest of the ABI uses. No test encoded the truncated value, so the fix is pinned by a new regression test, `NameDispatchedDoubleReturn_KeepsItsIeeeBits` (red before the fix, output below).
4. **`ReadsRawSlot` asked a different question and is now `ParameterReadsRawSlot`.** `IsStackScalar(argument)` is the argument's own representation; `ParameterReadsRawSlot(declaration)` is the callee's storage convention, and `EmitAstArguments` needs both (a scalar argument passed to a parameter the callee will read as a handle must be boxed). The rename makes the difference the name.
5. **The frame save/restore the audit cites was already gone — and `VmCall.Invoke` is not the substitute for what remains.** #253 (`fd9f22d6`, 2026-10-22 10:05) removed the `_save0.._saveN` ring copies from the SetArgs inline path, which is the block that range held when the audit was written. What remains there is not a call frame: the lambda body is compiled *inline* into the caller's delegate, no header is pushed, no `SP` is advanced, and there is no `Action<VmState>` to hand `VmCall.Invoke`. The two paths that do cross a call boundary (`InvokeAstFunction`, `InvokeHeapClosure`) already went through `VmCall.Invoke`; they are now the only caller-side frame protocol in the emitter (`grep -n "VmCall\.Invoke"` finds exactly those two). One real defect was left in that block: the restore wrote the root sentinel `0` into `_fp` instead of the caller's frame pointer, so a SetArgs invoke nested inside a function body corrupted that body's parameter addressing; it now saves and restores the caller's `_fp`, as `VmCall.Invoke` does. Regression test, `InlineSetArgsInvokeInsideFunctionBody_KeepsParameterAddressing` (red before the fix, output below). Reported rather than special-cased, per the card.
6. **The lambda bridge needed no result change.** `BuildDelegateArgument` already returns to a CLR delegate / `Expression<D>` through the single decode `RingCodec.ToClr` (with `FromIeee` only for a forwarded parameter, which has no body node to classify) and encodes the call's arguments through the single `BoxToAbi`. Nothing there normalised a call result a second way.
7. **Lane exception, mechanically forced.** `Poly.Tests/Interpretation/RingCodecTests.cs` is not in this card's Files and is in no lane's list, but deleting the predicate left two tests asserting it. They are now `IsStackScalar_StringConcatAdd_IsNotScalar` (the same fact on the surviving predicate) and `UnknownArgumentKind_IsACompileError` (the behaviour the deletion creates: an argument the classifier cannot place is a compile error, not a silent heap dereference). No other file was touched.

### RED, before the fix (2026-10-22)

Each regression test was run with its pre-fix arm put back, then with the fix restored. Both live in
`Poly.Tests/Interpretation/InvokeResultConventionTests.cs`.

Finding 3 — the name-dispatched arm restored as `Convert(Convert(invokeCall, returnClr), typeof(long))`:

```
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InvokeResultConventionTests/NameDispatchedDoubleReturn*'
failed NameDispatchedDoubleReturn_KeepsItsIeeeBits (69ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] AssertionException: Expected to be 4612811918334230528
  but found 2
  at Assert.That(exec.RawValue).IsEqualTo(BitConverter.DoubleToInt64Bits(2.5))
  total: 1
  failed: 1
  succeeded: 0
```

`2.5` reached the slot as `2` — the truncated integer, not `BitConverter.DoubleToInt64Bits(2.5)`.

Finding 5 — the restore restored as `postBody.Add(Assign(ctx.FramePosLocal, Constant(0)))`:

```
$ dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/InvokeResultConventionTests/InlineSetArgs*'
failed InlineSetArgsInvokeInsideFunctionBody_KeepsParameterAddressing (66ms)
  TUnit.Engine.Exceptions.TestFailedException: [Test Failure] AssertionException: Expected to be 62
  but found 5
  at Assert.That(exec.RawValue).IsEqualTo(62L)
  total: 1
  failed: 1
  succeeded: 0
```

The body is `(x) => x * 10 + (setArgs() + x)`, invoked as `f(5)`, so the answer is `62`. With `_fp` sentinelled to `0`, the late read of `x` *and* the callee's own result flush both address from slot 0, so `VmCall.Invoke` returned the argument slot (`5`) instead of the body's value. (An earlier draft of the test made the seeded root parameter the block's last statement, so `RawValue` read the parameter itself — `999`; the seed is now the first statement.)

Both are green on the fixed tree (`InvokeResultConventionTests` 2/2).

### Diagnostics, after the regression tests (2026-10-22)

`InvokeResultConventionTests` are test-only: no production file changed, so the ISB benchmark was **not** re-run, and the table above still describes the shipped emitter.

```
$ dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
    0 Warning(s), 0 Error(s)

$ dotnet run --project Poly.Tests/Poly.Tests.csproj
  total: 4465 / failed: 0 / succeeded: 4465

$ ... --treenode-filter '/*/*/AstParityTests/*'      -> 145 / 145
$ ... --treenode-filter '/*/*/*Invoke*/*'            -> 35 / 35
$ ... --treenode-filter '/*/*/SlotInvariantsTests/*' -> 6 / 6
$ ... --treenode-filter '/*/*/*VmTree*/*'            -> 7 / 7
```
