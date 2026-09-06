# Provisional Interpretation coverage inventory — 2026-09-05

**Branch / PR:** `cleanup/interpretation-coverage` · [PR 53](https://github.com/scoizzle/Poly/pull/53)  
**Mode:** Provisional gap fill (Nested). **Authority for remaining F#:** Sentinel `review/interpretation-coverage` follow-ups (not landed yet).  
**Scope:** `Poly/Interpretation/**` + `Poly/Ast/Nodes/TypeDefinitions/**` as consumed by interpretation vs `Poly.Tests/Interpretation/**`.  
**Not in scope:** PR 51 / 52 product; merges; self-review.

When Sentinel F# lands, close those items in a follow-up PR — this pass only fills clearly missing/thin areas with high-signal TUnit.

---

## What already exists (pre-pass)

~49 test files under `Poly.Tests/Interpretation/` (~12k LOC), including:

| Area | Representative coverage |
|------|-------------------------|
| VM language surface | `LanguageVmTests`, `VmCorrectnessTests`, `DirectVmAbiEmitterTests`, `InterpreterLanguageGotchaTests` |
| Control-flow CFG | `ControlFlowAnalysisTests` |
| Constant folding | `ConstantFoldingTests` |
| Closures / captures | `ClosureVmTests`, `ClosureCaptureTests` |
| Exceptions (analysis) | `ExceptionRegionAnalysisTests` |
| Exceptions (VM) | `ExceptionHandlingVmTests` (thin), `ThrowVmTests`, gotchas |
| Errors / fail-closed | `InvalidProgramTests` (SyntaxTypeCompatibility, JT0001/2/4, TH, …) |
| Generators | `CSharpGeneratorTests`, `LinqExpressionGeneratorTests`; Mermaid mostly under `Poly.Tests/Integration/` |
| ABI / results | `InterpretResultAbiTests` (thin), stabilization SetArgs/Heap |
| Type defs (consumed) | `TypeDefinitionNodeAnalyzerTests`, `PropertyDefinitionNodeTests`, `AstConstructorDefinitionTests` |
| Passes (indirect) | SideEffect / JumpTarget / DA / LambdaReturn wired in pipelines; few direct metadata asserts |

---

## Missing / thin (provisional view)

| Area | Evidence | Status after this PR |
|------|----------|----------------------|
| `AbiValueTypes.IsLongRepresentable` | No dedicated tests | **Closed** → `AbiValueTypesTests` |
| Heap relational compare (`VmHeapComparison` path) | Long/string Equal covered; DateOnly/string LessThan / Guid Equal thin | **Closed** → `VmHeapRelationalTests` |
| `ResolvedJumpTarget` positive stamps | InvalidProgram covers JT errors; metadata stamps unasserted; JT0003 missing | **Closed** → `JumpTargetAnalysisTests` |
| Definite assignment metadata | Pass in pipeline; `IsDefinitelyAssigned` never asserted | **Closed** → `DefiniteAssignmentTests` |
| Side-effect / elision | Pass used; no Pure/Write/CanElide/DEAD_CODE_ELIDABLE oracles | **Closed** → `SideEffectAnalysisTests` |
| `ValueStack` unit behavior | No direct tests (only via VM) | **Closed** → `ValueStackTests` |
| Mermaid under Interpretation/ | Only Integration/ | **Closed (thin)** → `MermaidAstGeneratorTests` |
| TryCatch+Finally / catch-all VM | ExceptionHandlingVmTests had 3 cases | **Closed (augment)** → `ExceptionHandlingVmTests` |
| `InterpreterResult` void / IEEE GetValue / null payload | Thin ABI file | **Closed (augment)** → `InterpretResultAbiTests` |
| Lambda jump isolation / deeper DA on non-lambda points | Not asserted | **Await Sentinel F#** |
| `VmValueMarshaller` / `VmTrace` / `FunctionEntry` internals | Internal; exercised indirectly | **Await Sentinel F#** |
| Full TypeDefinitions matrix via Interpretation | Partial via analyzer/property tests | **Await Sentinel F#** |
| Exhaustive VM op matrix / dual-oracle expansion | Large existing suites; residual holes unknown | **Await Sentinel F#** |
| SideEffect Read/Volatile / Allocate taxonomy | Only Pure/Write + elision here | **Await Sentinel F#** |
| ScopeValidator diagnostic catalog | Errors via InvalidProgram / BlockScope; not inventoried | **Await Sentinel F#** |

---

## What this PR added

| File | Intent |
|------|--------|
| `Poly.Tests/Interpretation/AbiValueTypesTests.cs` | Ring-inline vs heap-resident type classification |
| `Poly.Tests/Interpretation/VmHeapRelationalTests.cs` | DateOnly/string/Guid relational + equality via VM heap path |
| `Poly.Tests/Interpretation/JumpTargetAnalysisTests.cs` | ResolvedJumpTarget stamps + JT0003 |
| `Poly.Tests/Interpretation/DefiniteAssignmentTests.cs` | DA metadata join behavior on lambda bodies |
| `Poly.Tests/Interpretation/SideEffectAnalysisTests.cs` | Pure/Write, elision, AssignmentValueUsed, DEAD_CODE_ELIDABLE |
| `Poly.Tests/Interpretation/ValueStackTests.cs` | Push/Pop/Drop/grow/underflow |
| `Poly.Tests/Interpretation/MermaidAstGeneratorTests.cs` | Interpretation/-local Mermaid generator smoke |
| `Poly.Tests/Interpretation/ExceptionHandlingVmTests.cs` | Catch+finally, no-throw finally, untyped catch-all |
| `Poly.Tests/Interpretation/InterpretResultAbiTests.cs` | Void, IEEE double GetValue, null payload |
| `docs/plans/interpretation-coverage-provisional-2026-09-05.md` | This inventory |

---

## Remaining (await Sentinel F#)

All rows marked **Await Sentinel F#** above. Nested will mill each open F# into `Poly.Tests/Interpretation/` (or justified deferral in PR 53 body) after Sentinel publishes `docs/agent/reviews/*interpretation-coverage-sentinel*`.

Bugs found this pass: **none** (tests only; no product edits).
