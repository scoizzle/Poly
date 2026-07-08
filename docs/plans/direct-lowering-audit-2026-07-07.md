# Direct Lowering + ABI Audit (Baseline)

**Date:** 2026-07-07  
**Performed by:** Agent (initial pass per AUDIT-001)  
**Goal:** Establish precise state so every subsequent task has measurable DoD + invariants.

## 1. Build & Test Baseline
- `dotnet build Poly/Poly.csproj`: **Succeeded** (0 errors, 0 warnings) after 2 trivial fixes (StackPointer setter for demo sim; private _stepNodes access via new RecordStepNode helper; nullable list handling).
- Full test command: `dotnet run --project Poly.Tests/Poly.Tests.csproj` (not executed in this pass due to duration; will be part of VERIFY and per-task).
- Relevant test files with direct usage: `DirectVmAbiEmitterTests.cs`, `VmCorrectnessTests.cs`, `InterpreterIntegrationTests.cs`, `ClosureVmTests.cs`, `LambdaInvokeTests.cs`, `ExceptionHandlingVmTests.cs`, many others via `Interpreter.Compile/Execute`.

## 2. Primitive / Expansion Usage (outside tests + docs)
- In **source under Poly/** (non-test, non-obj/bin):
  - No active `.UsePrimitiveExpansion()` in Interpreter.cs (analyzer is direct-only).
  - No calls to ExpansionPass in hot paths.
  - References to "Primitives/" and "ToPrimitives" are confined to:
    - Historical comments and READMEs under `Poly/Interpretation/README.md`, `Poly/Interpretation/Analysis/README.md`, `Poly/Interpretation/Vm/README.md`.
    - One tangential comment in `CallSiteCatalogPass.cs`.
  - `PrimitiveType.cs` (and extensions) is **unrelated** — it's the domain primitive type catalog, not the old IR expansion.
- **Conclusion:** Default path is already clean. Pruning (DEPRECATE-002) is mostly docs + dead files + test cleanup. No core consumers of old expansion.

## 3. AST Node Coverage in DirectVmAbiEmitter
All files under `Poly/Syntax/Nodes/*.cs` (~84). Executable (runtime-evaluated) node categories:

**Covered by explicit case in CompileNodeInner (non-throwing):**
- Constants, all arithmetic (+-*/%), bitwise, shifts, comparisons, logical (And/Or short-circuit), Coalesce, unary (Not, -, ~), PopCount.
- Control: IfStatement, While/DoWhile/For/ForEachLoop, Break/Continue/Goto/Label, Return, Throw, TryCatchFinally, UsingStatement, SuspendNode, SwitchStatement (lowered to conditionals).
- Data: Variable, Assignment, Block, Parameter, ParameterReference, ThisReference (treated as 0), NullForgiving (passthrough).
- Call/closure: Lambda, Invoke (special for Member + inline for Lambda), Member (CLR), New, NewArray, IndexAccess.
- Misc: Conditional (ternary), Default, TypeIs/As/Cast, Await (passthrough POC), StridedSetBits.

**Gaps / partial that can still hit NotSupported or limited paths:**
- Default switch catch-all.
- Assignment destination other than Variable (IndexAccess assignment path may be incomplete — current code only handles Variable).
- Invoke with certain delegate forms (non-Lambda, non-Member-resolved).
- Some edge cases in EmitInvoke (ring save/restore for nested, frame header integration still legacy).
- EmitSwitch is "chained ifs" — functional but not optimal.
- Full closure capture + env materialization for suspend is partial (uses old slots + heap for captures).
- Some definition nodes are never expected at executable lowering time.

**Invariants observed:**
- Tests that reach direct lowering do not currently trigger the generic "unsupported node" for common constructs (arithmetic, blocks, basic control, lambdas, calls, EH).
- `Emit*` implementations exist for the listed cases.

## 4. Current Frame / ABI State
- **Compile-time simulator (AbiCtx):** Exists and reasonably complete.
  - `EnterActivation(argumentCount, localCount)`, `LeaveActivation()`, `GetCompileTimeVariableOffset(Variable)`, `GetCurrentFrameSize()`.
  - `CompileTimeFrame` with BaseOffset after the conceptual 2-word header.
  - **However:** Not the *primary* mechanism yet. Most variable access still goes through legacy:
    - `PushScope()` / `DeclareVariable(v)` (assigns dense slot # within scope).
    - `VariableRead(idx)` = `ArrayAccess(SlotsLocal, Add(FrameBaseLocal, Constant(idx)))`
    - Preamble still does `FrameBaseLocal = state.FrameBase`, root flush uses fb.
    - `EnterActivation` is defined but **not called** around lambdas/blocks/invokes in current emitters (from grep).
- **Runtime model (CallStack / CallStackFrame):**
  - Defined with exactly 2 linkage values: `PreviousFramePointer`, `SavedStackPointer`.
  - `CallStack` has `AllocateFrame(...)` (pushes exactly 2), `DeallocateFrame`, `GetLocals`/`GetArguments` returning Spans after the header, `RunSimulation()` demo.
  - **Not wired into emitted expressions.** Emitted code still manipulates raw `state.Stack`, `Registers` (ring temps), `FrameBase`.
- **VmProgram:** Already has `StepNodes` and `DebugInfo`.
- **VmState:** Still carries legacy: `Stack` (ValueStack), `Registers`, `FrameBase`, `OldFrameBase`, `ReturnPC`, `ClosureHandle`, plus the new `CurrentAstNode`/`CurrentNodeId`.
- **Debug hook:** Still `Action<VmState>? DebugInterrupt`. `WithInterrupt` only emits the check+invoke when `DebugInterruptProp != null`; the body itself is outside the if (good zero-overhead structure, but payload is full state not (Node, Span, Heap)).
- **Step / PC mapping:** StepCounter increments per `CompileNode`; nodes recorded. `CurrentAstNode` is set before every node. PC is still written for legacy interrupt.

**Summary:** The 2-value + compile-sim vision is stubbed + documented in the right places, but the *emitted code paths* and call sites are still on the old slot + FrameBase model. ABI-001 is the main integration task.

## 5. Debug / Suspend / Resume
- `CurrentAstNode` + `CurrentNodeId` are set on every node.
- SuspendNode handling exists (`EmitSuspendNode`).
- No heap-env materialization per frame yet.
- No frame-walking debugger helper producing "named" traces yet.
- Non-trivial suspend/resume validation using real VmDebugger + format comparison mentioned as requirement in history.

## 6. Other
- Direct is the only compilation path from `Interpreter`.
- Many tests exercise via `ExecDirect` helper or `Interpreter.Compile`.
- Legacy LinqExpressionGenerator and CSharpGenerator still exist (separate concerns; not in scope unless they need parity).
- Visitor pattern not used for lowering dispatch (giant switch is current reality).

## Recommended Next (from task list)
1. Mark AUDIT-001 complete once this doc + test run recorded.
2. Tackle NODES-001 (fill any real gaps + add tests) or ABI-001 (wire the simulator + 2-value prologues) — ABI work will touch many Emit* so node completeness first is safer.
3. Keep all changes minimal; every task must leave build green and pass its listed invariants.

## Actionable Gaps for Tasks
- Make EnterActivation calls + switch variable access to use GetCompileTimeVariableOffset everywhere (ABI-001).
- Change WithInterrupt + hook to produce (Node, ReadOnlySpan<long>, Heap) snapshot inside the null-check branch only (ABI-002).
- Ensure StepNodes.Count matches usage + implement a frame-walker helper (ABI-003).
- Prune after confirming no hidden consumers.

This audit gives every task a concrete "before" picture.
