# Interpretation System Simplification Audit

**Status:** research + proposal. **Admitted 2026-10-22 (Scot) as the `IS` lane**; the execution queue is [`docs/plans/interpretation-simplification/IS-README.md`](../plans/interpretation-simplification/IS-README.md). This document stays the evidence and the abstraction record; the suite holds the slices, their dependencies, and the gate. PR #254, whose VM ring-scope changes were the audit baseline, merged as `9522d536` on 2026-10-22.
**Scope:** `Poly/Interpretation` (19,885 LOC) — VM emitter, analysis framework, C# surface, AST producers.
**Method:** three read-only surveys (analysis pipeline; VM emitter; producer dispatch and parity). Every claim below was checked against code at the cited line. Defects that survived checking are filed as #255–#261.

Line numbers are against the worktree as audited, which carried uncommitted VM work (`SlotLocation.cs`, `FrameLocalArrays.cs`, `ABI6`). Treat a citation as "this file, around here" if that work has since moved. The seven defect reports/candidates are tracked as GitHub issues [#255](https://github.com/scoizzle/Poly/issues/255)–[#261](https://github.com/scoizzle/Poly/issues/261); consult each issue for its evidence and reproduction status.

This document is research input and the abstraction record, not a queue. The actionable entries are the issues; the slices are [`IS-README.md`](../plans/interpretation-simplification/IS-README.md), admitted once on the board.

## The frame

The system's complexity is not in its size. It is in **the same question being answered in several places and kept in sync by memory**. Four questions dominate, and every defect in #255–#261 is a place where the memory failed:

| Question | Asked by | Kept consistent by |
|---|---|---|
| What does this node mean? | 5 producers | memory |
| How do I get a value out of a node? | `CompileNode` + `CompileValue` | memory, 7 arbitrary twins |
| How do I wire a loop? | 4 emitters | memory, six of seven steps re-typed |
| Who owns this fact? | 2+ analysis passes each | memory, plus a comment |

Each proposal below converts one question from memory into mechanism. The test for each: **after it lands, can the wrong answer still be written?** If yes, it is a refactor, not a simplification.

## 1. One producer table, one totality test

Five independent type switches answer "what does this node mean":

| Producer | Shape | Unknown kind |
|---|---|---|
| `AbiEmitter.CompileNodeInner` `AbiEmitter.cs:230-296` | switch expression | throws (`:296`) — correct |
| `LinqExpressionGenerator` `:225-288` | switch expression | throws |
| `CSharpWriter` `:172-268` (statements), `:833-974` (expressions) | two switch statements | prints `node.ToString()` (`:972`) — #255 |
| `CSharpReader` `:451,492,707,929` | string switch after table match | throws |
| `MermaidAstGenerator` `:236-417`, `:436-536` | two switch statements | type name / empty — silent |

None is exhaustive at compile time. The only cross-check is `LanguageSurfaceTests.Kinds` (`Poly.Tests/Interpretation/LanguageSurfaceTests.cs:25-100`), and its test (`:173`) asserts that every concrete `Node` has *a dictionary entry* — never that a producer honours it. A producer can be missing an arm and the suite stays green.

**Proposal.** A single producer-capability table, keyed by node `Type` (location to be chosen with the AST ownership boundary): `Type → { Label, Children, Surface, Shape, ProducerCapabilities }`.

- Mermaid's two switches become table reads; the class loses its dispatch entirely.
- `LanguageSurfaceTests.Kinds`'s hand-kept dictionary is deleted, reading the table instead.
- A totality test: for each kind the table records which producers *claim* it, and each claim is exercised — VM claim ⇒ a node of that kind compiles; writer claim ⇒ `Write(Read(x))` is kind-stable; reader claim ⇒ `Read(Write(x))` is kind-stable; oracle claim ⇒ executes.

**Choice removed:** "did I update all five?" becomes "did the test fail?" Adding a node kind can no longer silently succeed in three places and fail in two. This is a registry for facts and test claims, not a new `NodeKind`, generated visitor, or parallel semantic dispatch.

**Risk:** low — the table is data, not behaviour, and it does not change any semantics. It also does not make dispatch total; it makes *drift detectable*, which is the achievable goal.

**Non-goal:** a `NodeKind` enum. `AGENTS.md` keeps `Node`/`NodeId` as the symbolic primary; a parallel taxonomy is the twin-tree failure in miniature. Key the table on `Type`. This table is a test-enforced capability declaration, not a promise of compiler-enforced switch exhaustiveness.

## 2. Delete the second interpreter

`LinqExpressionGenerator` (1,093 LOC, `public sealed`) has **zero production references**. Its only role is a differential oracle for tests (8 files). As an oracle it is unsound:

- It has *fewer* arms than the VM: no `TypeOf`, `ThrowExpression`, `NewArray`, `InterpolatedString`, `StridedSetBits`, `Comment` (`:225-288`), so it cannot check what the VM alone supports.
- It *accepts* kinds the VM rejects: `ParameterReference` → `Expression.Default(typeof(object))` (`:270`) versus `AbiEmitter.cs:250` (deliberate reject).
- It mis-models `this`: `ThisReference → Expression.Default(GetClrType(...))` (`:269`) — the expression is `null`, so any `this`-based comparison is vacuous.
- Its harness *skips* the interesting half: `VmCorrectnessTests.AssertVmMatchesLinq` returns early on "VM compile rejected" (`:345`), so "LINQ executes, VM rejects" is invisible by construction.

**Proposal.** Replace it with a differential that does not need a second in-house interpreter: compile the AST with `CSharpWriter`, compile that with Roslyn, compare exit values. One meaning-source (the writer) verified by an external authority. `ParityScenario.cs:43-56` already proves this shape works — it just covers the domain level, not the AST.

Staged: (1) build the writer→Roslyn differential over the existing corpus; (2) port `NodeTestHelpers.BuildExpression` consumers to VM-execution asserts; (3) delete `LinqExpressionGenerator`.

**Choice removed:** "which of two interpreters is right?" — there is one, plus a compiler that is not ours to argue with.

**Blast radius:** −1,093 LOC, one fewer hand-kept switch, ~20 test files' oracle changed. This is the largest single simplification available.

**Gates:** #255 and #256 first. A differential built on a writer that prints `node.ToString()` and turns `StridedSetBits` into a comment would certify garbage.

`MermaidAstGenerator` (593 LOC, zero production references) is a separate, smaller call: it drops ~15 kinds' children silently (`:536`). Either keep it as a debugging aid fed by the §1 table, or delete it. It is not worth abstracting.

## 3. Make `CompileNode` total, delete `CompileValue`

`CompileValue` (`AbiEmitter.cs:511-571`) is a second answer to "how do I get a value out of a node?":

- **20 arms**, of which **13 are already pure delegations** — 11 exact `SpillRingRead(CompileNode(n, ctx), ctx)` (`:555-565`), the `EmitIndexAccessValue` fallback (`:577`), and near-duplicates for `Constant`/captured `Variable`.
- **7 kinds are separately implemented twice** rather than delegated: `And`, `Or`, `Not`, `UnaryMinus`, `BitwiseNot`, `PopCount`, and comparison (`CompileCompareTest` versus `EmitComparisonValue`), each also reachable through `CompileConditionAsBool`.

So each arm is a fresh judgement call — "does this need a value form?" — answered 7 times yes, 13 times unnecessarily, and the rest no. That is the definition of avoidable choice. The correct shape already exists in the file: `EmitBinaryArithmetic` (`AbiEmitter.Expressions.cs:80`) is a one-line delegate to `EmitBinaryArithmeticValue` (`AbiEmitter.cs:596`).

**Proposal.** Make every `CompileNode` arm leave its result in the ring (void ⇒ `0L`), then delete `CompileValue`; it becomes `SlotLocation.Ring(RingDepth - 1).Read`. This also deletes the void-result special case in #260 by construction.

**Choice removed:** "ring form or value form?" — one form.

**Risk:** medium-low. Cost is one extra ring slot for kinds that currently compute directly into a value. It must be measured on the benchmark harness (`/tmp/polybench` recipe, nqueens/sieve/mandelbrot/collatz) before and after, and it is a *higher* ring high-water mark, which interacts with the advice in `docs/interpretation/vm-abi-reference.md`. Sequence it after §1 so the totality test proves nothing was lost.

## 4. One loop scaffold

Four emitters (`Statements.cs:222,257,498,559`), and **six of seven scaffolding steps are byte-identical**: the label pair, `PushLoopScope`, `EmitLoopIterationGuard`, `IfThen(Not(test), Goto(break))`, `Loop(body, break)`, `PopLoopScope` + `RingDepth` restore. What differs:

- **Genuinely semantic (must stay distinct):** test before versus after the body (`do`), where `continue` lands relative to the increment (`for`, `:546-547`) and relative to the test (`do`, `:270`), `foreach`'s per-iteration binding plus enumerator lifetime/dispose (`:563-567,597-604`), and `for`'s own declaration scope (`:510-514`).
- **Incidental (currently re-typed four times):** label names, the ring save/restore order, and the compile-then-rewind idiom.

**Proposal.** `EmitLoopScaffold(LoopPlan, ctx)` with `LoopPlan = (test: Before|After, perIterationPrelude, body, afterContinue)`. `while` uses neither hook, `for` passes the increment as `afterContinue`, `do` selects `After`, `foreach` passes `MoveNext` + binding as the prelude. Enumerator setup/dispose and the `for` declaration scope stay outside.

**Choice removed:** the order in which ring depth, labels, and scope pushes are emitted is decided once instead of four times slightly differently — the class of bug #259 lives in.

**Risk:** low. The four emitters' outputs can be diffed against the pre-change tree for the existing suite, and the tree-invariant helper landing with #254 (`Poly.Tests/TestHelpers/VmTree.cs`, `AssertInvariants`: ring prefix matches `MaxRingDepth`, no dead locals, no constant-index `_slots[...]`) turns a mis-ordered scaffold into a loud failure rather than a wrong value. That helper is on `master` as of `9522d536` (`Poly.Tests/TestHelpers/VmTree.cs`, `Scan` and `AssertInvariants`).

## 5. One invoke result convention

Six invocation paths (`Invoke.cs:24,178,195,239,244,311` plus `LambdaBridge.cs:33-430`) with:

- **4 different result normalisations** — `RingCodec.FromClr` (`:124,129`), `BoxToAbi` (`:256`), `Convert(Convert(x,t),long)` (`:250`), `Assign(0L)` (`:204,765`) — and the void case writing *nothing* (#260).
- **3 competing scalar/handle predicates** — `IsStackScalar` (`RingCodec.cs:120`, throws), `IsInvokeArgumentScalar` (`:137`, never throws), `ReadsRawSlot` (`Invoke.cs:714`, from the declaration).
- **Hand-rolled frame save/restore** at `:262-303` duplicating what `VmCall.Invoke` does.

**Proposal.** `EmitResult(Type)` for every path; delete the non-throwing scalar variant so "unknown" has one meaning (a compile error); call `VmCall.Invoke` instead of re-deriving the frame protocol.

**Choice removed:** which convention *this* call site uses.

**Risk:** highest in the set — this is the hottest code. Do it last, gated by the §2 corpus and the benchmark harness.

## 6. One owner per fact

The analysis pipeline has 13 passes whose order is convention (registered in a fluent chain, `Interpreter.cs:20-34`; `AnalyzerBuilder`'s doc says "the pipeline file is the schedule"), and several facts with more than one author:

| Fact | Authors | Hazard |
|---|---|---|
| Elision | `SideEffectAnalysisPass:48` **and** `ControlFlowAnalysisPass:69,675,717` | two writers, one reader |
| Purity polarity | `IsPure` (`ControlFlow:678-681`) **and** `HasSideEffects` (`SideEffect:107-111`) | two implementations of one answer; they are exact complements (see the correction below) |
| Binding lookup | `IsBoundUse`, `BindsTo`, `BindingOf` (`TypeAndMember:59,90`; `SyntaxTypeCompatibility:517`) | three re-derivations |
| Constant booleans | `IsConstantTrue`, `TryGetConstBool`, `GetConstValue`, folding | four null conventions |

**Proposal.** One owner per fact (elision: CFG contributes *reasons* to a single producer; purity: delete the duplicate predicate — the two are complements, so no polarity choice is involved), and declared pass preconditions — a pass states the bags it reads; a missing bag fails closed instead of reading `null`.

**Choice removed:** "who wrote this fact last?" and "what does missing metadata mean here?"

**Also:** three facts have no production reader — `IsDefinitelyAssigned`, `IsInfiniteLoop`, `IsMustExecute`. `AGENTS.md` tenet 7: guardrails only with real consumers. Either find the consumer or delete the fact; the shallow-walk bug in #258 is currently live only in these unread facts, which is why it has not bitten yet.

**Risk:** medium. No polarity change is involved (the correction below); the one decision is elision precedence, and it needs a deliberate call rather than a drive-by.

## Sequencing

Ordered by (confidence × value) ÷ risk. The dependencies are real: each step is the gate for the next.

| # | Step | Removes | Gate |
|---|---|---|---|
| 0 | Resolve #255–#261 correctness reports; land minimal repros, fix or disposition each | seven reported failure modes | suite green, repros retained |
| 1 | `NodeFacts` table + producer totality test | 2 switches; sync-by-memory for 5 producers | step 0 (the table encodes the truth, so the truth must first be true) |
| 2 | Writer→Roslyn differential; delete `LinqExpressionGenerator` | the second interpreter, 1,093 LOC | step 1 |
| 3 | `CompileNode` total; delete `CompileValue` | 20 arms, 7 twins, #260 | step 2 corpus + bench |
| 4 | `EmitLoopScaffold` | 4× duplicated scaffolding | step 1 |
| 5 | Invoke unification | 4 normalisations, 3 predicates | steps 2–3 |
| 6 | Analysis: one owner per fact, declared preconditions | 4 double-authored facts | independent |

## What this plan deliberately does not do

- **No generated visitor over `Node`.** The grammar table already is the single source for the C# surface; the drift is in the two hand-kept operator maps (`BinaryChain` hard-codes `" + "` at `CSharpWriter.cs:1015-1021`; `BinaryOpPrecedence` `:1102` and `BinaryNodePrecedence` `:1086` re-derive precedence). Putting operator text in the grammar deletes them. A code generator would add a build step to hide the same hand-keeping.
- **No new abstraction for one implementation.** `MermaidAstGenerator` and `LinqExpressionGenerator` are candidates for deletion, not for interfaces.
- **No "NodeKind" enum** for exhaustiveness. The totality test is the check; a parallel taxonomy is a second tree.
- **No change to `Lower` or the session pipeline.** Everything here is producer-side or analysis-side.

## How each step is proven

- `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` — 0 warnings.
- `dotnet run --project Poly.Tests/Poly.Tests.csproj` — full suite at the pre-ship gate.
- Benchmarks before/after for steps 3 and 5 only (`exec_min`/`exec_med` for nqueens, sieve, mandelbrot, collatz); steps 1, 2, 4, 6 are compile-time and must show flat numbers.
- Every step carries its own fail-closed case, per `AGENTS.md` §C# surface.

## Admission and ownership

Admitted 2026-10-22 (Scot) as the `IS` lane in [`docs/plans/poly-eng/board.md`](../plans/poly-eng/board.md) §Interpretation simplification. The confirmed defects are tracked by GitHub issues #255–#261; execution is [`IS-README.md`](../plans/interpretation-simplification/IS-README.md), one card per slice. Do not create a second `CURRENT` and do not re-derive the slices here. The tracked issues are sufficient record for the defect investigations; this artifact is the cross-cutting abstraction record.

The issue set is broader than the initial producer sweep: #255–#258 and #261 came from the audit; #259–#260 capture independently corroborated VM defects from the emitter survey. They are separate correctness work from abstraction steps and must be fixed or explicitly dispositioned before an abstraction built on those paths is trusted.

## Research limits and corrections

- The `NodeFacts` proposal needs a precise ownership decision before implementation. `Poly/Ast` should own only facts intrinsic to `Node`; C# surface grammar and producer support may instead belong in `Poly.Tests` or `Poly/Interpretation/CSharp`. Avoid making AST depend on Interpretation merely to centralize coverage.
- `AggregateChildren` is explicitly a direct-child aggregation API, so it is not itself a defect. Recursive traversal must be a distinctly named operation with traversal-boundary rules (notably whether lambdas/type declarations are barriers). #258 concerns `AnyChild`'s mismatch between its documented descendant semantics and its direct-child implementation, including the private shadow.
- The reported double-authoring of `ElisionMetadata` is a design lead, not automatically a behavior bug: CFG can establish unreachable statements while side-effect analysis establishes safe-to-elide expressions. Unification must preserve both reasons and define precedence before changing defaults.
- **Purity polarity (corrected 2026-10-22):** `IsPure` and `HasSideEffects` do **not** have opposite defaults. For a non-null node both reduce to `metadata is not null && Kind == Pure`; for `null`, `IsPure` returns `true` and `HasSideEffects` returns `false`, which is the same complement. "Missing as pure" describes a `null` node, not absent metadata. De-duplicating them is behaviour-neutral, so [`IS6`](../plans/interpretation-simplification/tasks/IS6.md) carries no polarity change. The only open decision there is elision precedence.
- **#261's blast radius (corrected 2026-10-22):** "the field-backed queries are exercised mainly by tests" is false. `DomainEntityInstance` holds the analyzer as its provider in production (`HostAbi`, `CompileBody`, `ExecuteDynamicTree`, `ExecuteEffectList`, `ModuleAwareTypeProvider`); moving to the per-analysis provider from `AnalysisContext.TypeDefinitions` touches five call sites in the domain runtime and seven test files. [`IS0f`](../plans/interpretation-simplification/tasks/IS0f.md) carries that widened scope — retyping a field to `ITypeDefinitionProvider` is a fact-ownership fix, not new domain-runtime capability.
- **Qualified type names and `using` (found 2026-10-22 during IS0a):** the C# surface cannot express a static call on a type. The reader roots every postfix chain in a `Variable` (`CSharpReader.cs:945`), `using` directives are parsed and never resolved (`:44`), and the writer prints type references only in callee position (`CSharpWriter.cs:848,853`) — while the VM already expects a type reference as a `Member`/`Invoke`/`New` receiver (`AbiEmitter.cs:273,590`). A first attempt to make `PopCount` round-trip added a `popcount` pattern to `expr-primary`; it was withdrawn because a library intrinsic does not belong in the language grammar and it does not generalize. [`IS1c`](../plans/interpretation-simplification/tasks/IS1c.md) owns the general fix; `PopCount` is `Rejected` until then.
- **Floating point (measured 2026-10-23, after the program):** the VM's FP cost is the emitted representation, not dispatch — hand-written C# in the emitter's exact shape matches the Poly row (2,350 vs 2,226 us), and nested double expressions are 1.8x faster (1,320 us). Ring slots are `long` locals, so a double cannot stay in a register across an operation. See [`floating-point-representation.md`](floating-point-representation.md). Not part of this program's slices: it needs its own measurement gate and a native FP baseline that does not exist in the suite today.
- The analysis pipeline dependency idea is deferred pending a dependency inventory for the **interpretation** pipeline. Existing `PassDependencyDeclarationTests` cover a different pipeline; do not generalize the finding into an implementation prescription without checking which metadata facts are actually required, optional, or deliberately absent.
- Deleting `LinqExpressionGenerator` is conditional on replacing its test role with a corpus that proves VM behavior independently. The writer cannot be treated as the external oracle until #255–#257 and kind-preserving round trips are resolved; a writer→Roslyn route alone does not verify the interpreter's semantics. Stagewise comparison should retain explicit unsupported-kind checks rather than skip VM failures.
- `MermaidAstGenerator` is a debug/visualization producer, not a meaning authority. Its missing child traversal can make diagrams incomplete, but it does not by itself justify adding an AST-wide abstraction; first establish actual consumers and desired output contract.
- `CompileNode` totality and invoke normalization are performance-sensitive hypotheses. Measure baseline and result on the merged #254 tree, and keep the existing ring liveness/invariant evidence; a simpler vocabulary does not justify increasing high-water slots without data.
- **Checked 2026-10-22 while planning the slices, against `9522d536`:** (a) #257's premise about `break` is wrong — `CSharpGrammar.cs:324` defines `break;`, `CSharpReader.cs:564` produces `BreakStatement`, and `CSharpWriter.cs:233` prints it; the defect is the missing case terminator and the `switch-case` shape. (b) The `/tmp/polybench` recipe does not exist in the repository (`grep -rn polybench` is empty), so the IS3/IS5 perf gates need a checked-in harness first ([`ISB`](../plans/interpretation-simplification/tasks/ISB.md)). (c) `CompileValue` has ~50 arms at this commit, not 20 — of which ~10 are exact `SpillRingRead(CompileNode(...))` delegations, plus `EmitIndexAccessValue` and the `Constant`/captured-`Variable` near-duplicates. The seven twins are as reported, and the argument holds a fortiori.
