# Prompt: Execute the Direct Lowering + ABI Refactor Task List (Simpler Model Version)

**Target audience:** Smaller / cheaper / lower-context models or agents.
**Purpose:** A self-contained prompt that lets a simpler model make steady, correct, verifiable progress on finishing primitive deprecation, full AST node coverage in direct lowering, and the 2-value frame + debug ABI refinements.
**Date:** 2026-07-07

---

## Standing Rules (NEVER violate these)

1. **You are a literal executor, not an architect.** Follow the task list, the DoD, and the invariants exactly. Do not invent new designs, new layers, or "better" abstractions unless the current task explicitly asks for them.

2. **Core Principles (from AGENTS.md) — evaluate every action against them:**
   - Keep only what measurably helps the customer (time-to-value, correctness, operability).
   - Engineer end-to-end behavior with clear ownership.
   - Optimize for shipped capability over completeness.
   - Build working code before extracting abstractions.
   - Operational guardrails only when they have real first consumers.
   - The domain model (here: the AST + direct lowering to VM ABI) is the key artifact.

3. **Never leave the build broken.** After every edit you must run `dotnet build Poly/Poly.csproj` (and the tests project) and it must succeed with 0 errors before you continue.

4. **Validate invariants before claiming progress.** For every task you touch, you must explicitly prove (via command output, grep, test results, or DumpTree inspection) that every listed invariant holds.

5. **Minimal changes.** Use the smallest possible edit that satisfies the DoD. Read the exact method first with the file tool before editing.

6. **One atomic piece at a time.** Use the Recursive Micro-Task Loop below.

7. **Paths:** Always prefer relative paths (`Poly/Interpretation/Vm/DirectVmAbiEmitter.cs`, not absolute).

8. **Test command:** `dotnet run --project Poly.Tests/Poly.Tests.csproj` (this project uses TUnit and a custom runner). For focused runs, experiment with its filter flags (e.g. `--treenode-filter` or similar) or run subsets by name.

9. **Tracking:** Maintain a clear personal checklist of the tasks below. If the `todo_write` tool (or equivalent) is available in your environment, use it to mark status. Otherwise, report status in your final messages using the exact task IDs.

---

## Required First Actions (do these in order at the start of your session)

Use your file-reading and search tools to read **exactly** these (do not read the entire repo at once):

1. `AGENTS.md` — especially Core Principles, Naming, Build & Test commands, Placement rules, Interpretation section, and "Before working here" notes.
2. `docs/plans/direct-lowering-audit-2026-07-07.md` — this is the current baseline snapshot.
3. `docs/plans/finish-direct-lowering-and-abi-refactor.md` — the high-level phased plan.
4. `docs/decisions/README.md` + skim the VM-related decisions (2026-06-08-vm-as-canonical-semantics.md, 2026-06-08-breakpoint-architecture.md, 2026-07-04-primitives-as-canonical-ir.md) for context only.
5. Key implementation files (read in this order, using offset/limit to stay focused):
   - `Poly/Interpretation/Interpreter.cs` (see that direct path is primary)
   - `Poly/Interpretation/Vm/VmProgram.cs`
   - `Poly/Interpretation/Vm/VmState.cs` (focus on Word, CallStackFrame, CallStack class, RunSimulation, and the legacy fields)
   - `Poly/Interpretation/Vm/DirectVmAbiEmitter.cs` (the whole file is long — first read the class summary, Emit method, CompileNode/CompileNodeInner, WithInterrupt, AbiCtx at the bottom, then targeted reads of variable handling, EmitInvoke, EmitBlock, EmitLambda, EmitReturn, etc.)

Only after the above, load the Task List below.

---

## Recursive Micro-Task Discipline (use this for everything)

Apply this loop to any task or sub-problem (adapted from repo patterns for simpler models):

1. **Narrow ruthlessly.** Read only the task description + its DoD + its Invariants + 1-2 directly relevant methods/files.
2. **Decompose.** Turn the current work into 2–5 atomic sub-steps. Example: "For ABI-001 variable access" → (a) add EnterActivation calls at function entry points, (b) change one VariableRead site to use GetCompileTimeVariableOffset, (c) update a test to prove the emitted tree, etc.
3. **Solve exactly one sub-step.** Gather minimal evidence (one grep + one read of <50 lines). Make the smallest edit. Prove the local change works.
4. **Verify locally then against full invariants.** Run build + targeted test + the specific checks listed in the task's Invariants.
5. **Output a tiny synthesis.** "Sub-step Z complete. Evidence: ... Open question for next sub-step: ..."
6. **Recurse or finish the parent task.** Only mark the parent task complete when *every* invariant for that task ID passes.

Exit any step the moment you have a verifiable, minimal example + the local invariants for that micro-step.

---

## The Explicit Task List (with DoD + Invariants)

You must drive work using these exact items. Current known state (from audit): AUDIT-001 and DEPRECATE-001 are complete. Direct lowering is the default path. The compile-time simulator (EnterActivation, GetCompileTimeVariableOffset, etc.) exists in AbiCtx but is not yet the primary driver of emitted code. Legacy FrameBase + DeclareVariable + `ArrayAccess(SlotsLocal, Add(FrameBaseLocal, ...))` + full VmState DebugInterrupt are still dominant. CallStack/CallStackFrame (exactly 2 linkage values) exist as runtime model but are not emitted. StepNodes are populated but not fully used for named debug.

**Tasks (in recommended rough order):**

**DEPRECATE-002**
- Description: Prune obsolete primitive source, tests, and references (after audit confirms safe).
- DoD: Remove/empty `Poly/Syntax/Primitives/` (or mark with Obsolete if kept for migration); delete ExpansionPass.cs if present; migrate unique test coverage or delete obsolete Primitive*Tests/Expansion*Tests. Remove ToPrimitives methods or make them throw+Obsolete. Clean AGENTS.md, docs/plans, Interpretation/README.md, and any other refs. Confirm via build that nothing references the deleted items.
- Invariants to validate:
  - `dotnet build Poly/Poly.csproj` and tests project succeed with no compile errors after removal.
  - Full (or relevant subset) test run passes (fewer tests is OK if obsolete ones removed).
  - `grep -r "ToPrimitives\|ExpansionPass\|from .*Primitives" --include="*.cs" Poly/ | grep -v "/Tests/" | grep -v "/bin/" | grep -v "/obj/"` returns 0 relevant lines (docs and comments are allowed to mention history).
  - No runtime code path in Interpreter or DirectVmAbiEmitter references the old expansion for normal Compile/Execute.

**NODES-001**
- Description: Audit + complete 100% executable AST node coverage in DirectVmAbiEmitter.CompileNodeInner (no default unsupported throws for executable constructs).
- DoD: Every executable node (arithmetic, logic, control flow, blocks, variables, lambdas, calls, EH, suspend, allocations, member access, Await, Switch, ForEach, Using, etc. — exclude pure Type*Reference and Definition nodes) has an explicit non-throwing case. Fill gaps (improve EmitSwitch, assignment destinations, EmitInvoke branches, Await, StridedSetBits, etc.). All new impls must be consistent with the frame/ring model. Extend DirectVmAbiEmitterTests + add behavior assertions.
- Invariants to validate:
  - Running focused direct tests + broad Interpreter/Vm filters never hits the generic "unsupported node" NotSupportedException (or the specific ones in EmitInvoke/Assignment) for nodes that appear in tests.
  - New tests for gaps (e.g. SwitchStatement with multiple cases + default, assignment to IndexAccess if supported, complex nested invokes) pass using `ExecDirect` (or equivalent) and assert correct numeric results.
  - `DirectVmAbiEmitter.DumpTree(...)` on the new cases produces sensible structured output.
  - Existing direct tests continue to pass with identical results.
  - No new catch-all "unsupported" cases are introduced.

**ABI-001**
- Description: Integrate 2-value frame model (PreviousFramePointer + SavedStackPointer only on stack) + make the compile-time simulator (AbiCtx) the source of truth for offsets/sizes used in emitted code.
- DoD: Refactor variable/parameter storage and call prologues to be driven by `EnterActivation` / `GetCompileTimeVariableOffset` / `GetCurrentFrameSize`. Emit exactly the two linkage values at boundaries + use constant offsets (`ArrayAccess(frameBaseLocal, Constant(compileOffset))`) for user vars. Update CallStack runtime if needed. Update prologues in EmitInvoke / CompileFunctionBody / top level / lambdas. Ring is only for temps. Remove or isolate legacy DeclareVariable slot math for user data inside frames.
- Invariants to validate:
  - After lowering a function with arguments + locals + nested calls, inspecting the generated expression tree (via traceExpressions or DumpTree) shows explicit 2-word header setup (or equivalent) per activation and only compile-time `Constant(offset)` accesses for named variables (no `Add(FrameBaseLocal, runtime-computed)` for user variables).
  - Frame sizes reported by the simulator match the emitted SP advances.
  - `CallStack.AllocateFrame` / `GetLocals` / `GetArguments` (in VmState.cs) are consistent with the model (2 values + counts).
  - Direct tests involving Block, Invoke, parameters, and nested scopes still pass and produce correct results.
  - Observable program behavior is unchanged.

**ABI-002**
- Description: Simplify debug hook surface to explicit minimal (Node current, ReadOnlySpan<long> locals, Heap) and wire zero-overhead when null.
- DoD: Introduce or adapt a hook (e.g. `Action<Node, ReadOnlySpan<long>, Heap>?`) for the direct path. In `CompileNode` / `WithInterrupt`, the hook invocation + locals snapshot is generated ONLY inside the `if (hook != null)` branch. Snapshot is built using compile-time offsets from the current frame. Wire `CallStack.GetLocals` (or equivalent slice) to supply the Span. Update VmState if a new field is needed (keep backward compat if possible). Update tests that use hooks.
- Invariants to validate:
  - When hook is null (default Normal/NoDebug paths), the compiled delegate expression tree contains ZERO calls to any debug hook and ZERO extra snapshot allocations or unnecessary PC flushes (prove via DumpTree or expression counting).
  - When a hook is provided, it receives the exact current `Node` and a `ReadOnlySpan<long>` whose Length matches the compile-time local count for the scope and whose values match the live frame locals.
  - Suspend/resume behavior via the hook (or SuspendNode) still works correctly.
  - No performance regression on the null-hook path (simple loop / arithmetic tests).

**ABI-003**
- Description: Wire PC/step -> Node mapping fully + enable named variable resolution in stack traces / debug views using Node scope + layout.
- DoD: Ensure `StepNodes` list is complete (length matches highest step used) and passed to `VmProgram`. Store/restore return step/PC in frame data during invokes. Create or extend a small helper (VmDebugger or static method) that, given a suspended state + program, walks frames using PreviousFramePointer/SavedStackPointer, resolves PCs via StepNodes to Nodes, and maps locals Spans to human-readable names by consulting the Node's VariableScope (declaration order) + the lowering layout. Add a test or example that prints a readable stack trace with names ("x", "sum") instead of slot numbers.
- Invariants to validate:
  - `StepNodes.Count` is correct relative to the highest step assigned during lowering.
  - A non-trivial suspended state (nested calls or lambdas + variables in inner scopes) produces a trace containing resolvable Node info and variable names instead of raw slot indices.
  - Resume from such a suspended state produces the correct final result.
  - No reliance on legacy slot numbers appears in the example debug output.

**ABI-004**
- Description: Implement optional heap-backed environment materialization for frames (used on suspend/debug, not hot path).
- DoD: Add support (controlled by flag, mode, or only on suspend path) to materialize a frame's locals/args into a heap-backed long[] (or object?[]), storing the handle in state or the frame. On SuspendNode (and/or hook suspend), capture CurrentNodeId + PC + env handle(s). On resume, restore from the env. Update relevant suspend/resume tests. Normal (non-suspend) execution must not allocate extra heap envs per frame.
- Invariants to validate:
  - A suspended VmState contains sufficient information (node/PC + env ref or captured values) to resume with identical local values.
  - Post-resume execution matches pre-suspend results.
  - In normal execution (no suspend), you can demonstrate that no extra per-frame heap env allocations occur (inspect heap or count in a test).
  - Proper cleanup on frame deallocation when materialization was used.

**ABI-005**
- Description: Remove or fully isolate legacy frame/slot/ring-for-vars/ClosureHandle/ReturnPC/OldFrameBase machinery from the direct lowering hot path.
- DoD: Emitted expressions and prologues in the direct emitter no longer use legacy FrameBase math for user variables (except any unavoidable trampoline compat), do not use Registers for named locals, and do not rely on the old call linkage fields for direct function calls. The combination of frameBaseLocal + compile constants + ring-for-temps + CallStack concepts must be the model. Clean comments and unused properties where safe.
- Invariants to validate:
  - Grep of `DirectVmAbiEmitter.cs` + representative emitted trees for legacy identifiers (`OldFrameBase`, `ReturnPC`, `ClosureHandle` used for user frames, direct slot math for variables) shows only comments or explicit compat shims.
  - Build + all direct tests still pass.
  - Word-path performance for scalar work is the same or better (spot check with simple loops).
  - VmState surface for the direct path is conceptually simpler.

**NODES-EXT** (optional, only if it clearly helps after coverage)
- Description: Consider adding IVisitor/Accept for node dispatch to avoid giant switch.
- DoD (only pursue if it simplifies): Define minimal visitor support; migrate CompileNodeInner dispatch. Keep behavior 100% identical.
- Invariants: All tests pass with identical results; the giant switch is gone or much smaller; no behavior change.

**VERIFY-001**
- Description: End-to-end verification, new coverage tests, docs update, and performance spot-check after all prior tasks.
- DoD: Full `dotnet run --project Poly.Tests/Poly.Tests.csproj` passes (0 failures, ignoring any intentionally pruned obsolete tests). Add dedicated tests for: named locals in stack traces, simplified hook, frame-based suspend/resume with heap envs, and coverage for previously gappy nodes. Spot-check benchmarks or micro-tests (ring depth, arithmetic loops) show no regression. Update the plan file, audit file, Interpretation README, and any impacted docs. Mark the overall effort complete.
- Invariants to validate:
  - All invariants from prior tasks still hold collectively.
  - No `ToPrimitives` / Expansion in the default pipeline (double-check).
  - A manual or test "debugger" walk produces a correct named stack trace and can step/resume.
  - The three original requirements are met: (1) reliable simulation of real code, (2) common debugger operations (break/step + inspect) at symbolic Node level with names and frames, (3) the resulting code is simpler to maintain by a tiny team.
  - Docs clearly describe the final model.

---

## Workflow You Must Follow

1. Pick the next pending task (start with DEPRECATE-002 or NODES-001 unless told otherwise).
2. Decompose it using the Recursive Micro-Task Loop.
3. For each micro-step:
   - Read the minimal code.
   - Make the smallest edit with `search_replace` (after reading).
   - Immediately run build.
   - Run relevant tests (use ExecDirect helper from DirectVmAbiEmitterTests where possible for verification).
   - Explicitly check the invariants that apply to this micro-step + the parent task.
   - Report progress with concrete evidence (paste command output, grep results, test names that passed).
4. Only after a parent task's **full** set of invariants are proven, treat it as complete and move on.
5. If a task is large, create numbered micro-tasks for yourself (e.g. "NODES-001.3: implement full EmitSwitch as chained ifs + add test").
6. When you make progress on a task, suggest the exact next micro-step or task ID.
7. At natural checkpoints (end of a task or phase), also run a broader test filter and note results.
8. If you encounter an obstacle that would require a significant new decision, stop and describe it (do not implement).

**Output format after completing a micro-step or task:**

```
**SUBSTEP / TASK <ID> — <short title>**

Changes: (1-3 bullet, file + what)
Build: succeeded (paste last lines if relevant)
Tests: <names or filter> — all passed
Invariants checked:
- <copy the relevant invariant> → proven by: <grep output / test assertion / tree snippet>
- ...

Open questions / next micro-step: ...
```

---

## Key Technical Reminders (current reality)

- The target is **direct structured AST lowering** into LINQ Expression trees that implement the bespoke VM ABI.
- Ring (Registers in VmState) is for fast temporaries only.
- User variables / parameters / frame data should move to the 2-value frame model (PreviousFramePointer + SavedStackPointer linkage + compile-time known counts/offsets).
- `AbiCtx` already has the simulator methods — your job in ABI-001 is to **call them** and make emitted code use the results.
- `CurrentAstNode` / `CurrentNodeId` are already being set.
- `StepNodes` collection exists and is passed to VmProgram.
- `CallStack` / `CallStackFrame` in VmState.cs is the runtime view you should align with.
- The debug hook simplification must have literally zero cost on the common (null hook) path.
- Preserve exact observable results for all existing direct tests.
- "Format comparison" / parity with other paths may still be useful for validation in tests.

---

## Success Criteria for the Whole Effort

When VERIFY-001 is complete:
- Primitives/expansion are fully out of the critical direct path.
- DirectVmAbiEmitter handles 100% of executable AST nodes without throwing "unsupported".
- The new ABI (2-value frames + compile-time simulator + simple Node+Span+Heap hook + PC→Node + named locals) is wired and used.
- A debugger can breakpoint/step and get names + proper stack traces using only Node + frame walk + StepNodes.
- Full test suite is green on the direct path.
- The resulting code is noticeably simpler to understand and maintain.
- All three requirements (reliable simulation, debugger support at symbolic level, maintainable by tiny team) are demonstrably satisfied.

---

**Begin now.**

First action after reading the required files: 
- Re-state (in your own words, briefly) the current state from the audit.
- List the task IDs that are still pending according to the list above.
- Tell me which task (or first micro-step of a task) you will attack, and decompose it into 2-4 atomic sub-steps.

Then execute.

You may begin.