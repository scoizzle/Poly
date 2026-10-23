# IS — interpretation simplification

Status: **planned** (admitted 2026-10-22 by Scot as the `IS` lane; does not move board row 4).
Source: [`docs/analysis/interpretation-simplification.md`](../../analysis/interpretation-simplification.md) (audit + proposal; #255–#261 are its evidence).
Board: [`docs/plans/poly-eng/board.md`](../poly-eng/board.md) §Interpretation simplification.
Gate: [`docs/agent/pr1-uncommitted-review-gate.md`](../../agent/pr1-uncommitted-review-gate.md). Rules for recording: [`docs/plans/poly-eng/README.md`](../poly-eng/README.md) §Record rules.

These slices are one program, not one PR. Each slice is a `tasks/<id>.md` card, one branch `slice/<id>`, one PR. A slice is done when its own card says so and the suite gate is green on it.

## The frame, in one paragraph

`Poly/Interpretation` is not large for its size; it is large for the number of places that answer the same question and are kept in sync by memory. Five producers answer "what does this node mean"; two methods answer "how do I get a value out of a node"; four emitters re-type the same loop scaffolding; six invoke paths normalise results four ways; two analysis passes each own elision and purity. Every slice below converts one of those questions from memory into mechanism. The test for each slice is the same: **after it lands, can the wrong answer still be written?** If yes, the slice was a refactor, not a simplification, and it does not count.

## Lanes (the parallel-safety contract)

A lane is a **file-ownership group**, not a queue of similar work. One agent owns a lane at a time; PRs inside a lane are sequential. Lanes run in parallel because their file sets are disjoint.

| Lane | Owns | Slices in order |
|---|---|---|
| **A `csharp`** | `Poly/Interpretation/CSharp/**`, `Poly/Interpretation/LinqExpressions/**`, `Poly/Interpretation/Mermaid/**`, `Poly/Interpretation/NodeFacts.cs`, `Poly/Interpretation/README.md`, `Poly.Tests/Interpretation/CSharp*`, `Poly.Tests/Interpretation/Linq*`, `Poly.Tests/Interpretation/NodeFact*`, `Poly.Tests/Interpretation/AstParity*`, `Poly.Tests/Interpretation/LanguageSurfaceTests.cs`, `Poly.Tests/Interpretation/VmCorrectnessTests.cs`, `Poly.Tests/Integration/Mermaid*`, `Poly.Tests/TestHelpers/NodeTestHelpers.cs`, `Poly.Tests/DomainModeling/Compile/EmitGolden/**` | IS0a → IS0b → IS1 → IS1d → IS1b → IS2a → IS2d → IS2b → IS2c → IS1c |
| **B `analysis`** | `Poly/Analysis/**`, `Poly/Interpretation/Analysis/**`, `Poly.Tests/Interpretation/*Analyzer*`, `Poly.Tests/Interpretation/*ControlFlow*`, `Poly.Tests/Interpretation/*SideEffect*`, `Poly.Tests/Interpretation/*Analysis*`, `Poly.Tests/Interpretation/*Const*`, `Poly.Tests/Interpretation/AnyChildTests.cs`, `Poly.Tests/Interpretation/AnalysisFactOwnershipTests.cs` | IS0c → IS0f → IS6 |
| **C `vm`** | `Poly/Interpretation/Vm/**`, `Poly.Benchmarks/**`, `Poly.Tests/TestHelpers/VmTree.cs`, `Poly.Tests/Interpretation/*Vm*` (except `VmCorrectnessTests.cs`), `Poly.Tests/Interpretation/*Invoke*`, `Poly.Tests/Interpretation/*Lambda*`, `Poly.Tests/Interpretation/*Slot*`, `Poly.Tests/Interpretation/*Loop*` | IS0d → IS0e → ISB → IS4 → IS3 → IS5 |

Two one-line exceptions, both comment-only and named so they do not read as ownership: IS2c updates the doc comment in `Poly/DomainModeling/Lowering/DomainExpressionLoweringPass.cs`, and IS3/IS5 update `docs/interpretation/vm-abi-reference.md`.

**Conflict rule.** A slice edits only files its lane owns. If a slice needs a file another lane owns, it stops and reports — it does not take the file. The lane table is the default boundary; **a card's Files section is authoritative for the test files that slice touches**, and a test file a card names belongs to that card's lane for that slice (IS2b, for instance, owns 18 test files by name). `docs/` is shared: a slice may update the one production doc its change invalidates, and the suite owner updates this file.

## Slices

`[ ]` not started · `[~]` in flight · `[x]` merged. A card's own Log is the truth for SHAs; this column is only the queue.

| State | ID | What it removes | Depends | Lane | Card |
|---|---|---|---|---|---|
| [x] | IS0a | writer fallback that prints a type name; three kinds that lose their kind on round-trip (#255, #256) | — | A | [tasks/IS0a.md](tasks/IS0a.md) |
| [x] | IS0b | switch cases printed without a terminator; `case`/`break` asymmetry with the reader (#257) | IS0a | A | [tasks/IS0b.md](tasks/IS0b.md) |
| [x] | IS0c | `AnyChild`'s descendant promise vs one-level walk; catch-body elision (#258) | — | B | [tasks/IS0c.md](tasks/IS0c.md) |
| [x] | IS0d | suspend inside a compiled function body compiling to a resume label nothing dispatches (#259) | — | C | [tasks/IS0d.md](tasks/IS0d.md) |
| [x] | IS0e | void CLR invoke leaving its result slot unwritten (#260) | IS0d | C | [tasks/IS0e.md](tasks/IS0e.md) |
| [x] | IS0f | per-analysis type registry held on the process-wide static analyzer (#261) | IS0c | B | [tasks/IS0f.md](tasks/IS0f.md) |
| [x] | IS1 | five hand-kept producer switches and the `LanguageSurfaceTests.Kinds` dictionary; one capability table + one totality test | IS0a, IS0b, IS0e | A | [tasks/IS1.md](tasks/IS1.md) |
| [x] | IS1d | a `for` header prints its increment as a statement, so the output does not compile | IS1 | A | [tasks/IS1d.md](tasks/IS1d.md) |
| [x] | IS1b | Mermaid's two dispatch switches and its silent child drops | IS1 | A | [tasks/IS1b.md](tasks/IS1b.md) |
| [ ] | IS1c | qualified type names and using scope; a static call on a type is not expressible | IS0b, IS1 | A | [tasks/IS1c.md](tasks/IS1c.md) |
| [x] | IS2a | the assumption that the LINQ oracle is sound; proves the writer against Roslyn over a corpus | IS1 | A | [tasks/IS2a.md](tasks/IS2a.md) |
| [~] | IS2b | 18 test files' dependence on `NodeTestHelpers.BuildExpression` | IS2a | A | [tasks/IS2b.md](tasks/IS2b.md) |
| [B] | IS2c | `LinqExpressionGenerator` — 1,093 LOC, zero production callers | IS2b, IS2d | A | [tasks/IS2c.md](tasks/IS2c.md) |
| [ ] | IS2d | 11 writer defect classes the differential found (#279), incl. two silent wrong values | IS2a | A | [tasks/IS2d.md](tasks/IS2d.md) |
| [x] | ISB | the untracked `/tmp/polybench` recipe; a checked-in baseline for IS3/IS5 | — | C | [tasks/ISB.md](tasks/ISB.md) |
| [x] | IS4 | four copies of the same loop scaffolding | IS1, IS0d | C | [tasks/IS4.md](tasks/IS4.md) |
| [ ] | IS3 | `CompileValue` and its 7 twins; the void special case in #260 | IS2c, ISB | C | [tasks/IS3.md](tasks/IS3.md) |
| [ ] | IS5 | four result normalisations, three scalar predicates, hand-rolled frame save/restore | IS3 | C | [tasks/IS5.md](tasks/IS5.md) |
| [x] | IS6 | elision and purity each authored twice; three facts with no reader | IS0f | B | [tasks/IS6.md](tasks/IS6.md) |

## Waves

Lanes start together; slices inside a lane do not. Cross-lane arrows are real gates. **A single agent runs the waves in order, A → B → C within each wave; a team runs one agent per lane.**

| Wave | Start when | A | B | C |
|---|---|---|---|---|
| 0 | plan merged | IS0a | IS0c | IS0d |
| 1 | lane's previous slice merged | IS0b | IS0f | IS0e, then ISB |
| 2 | IS0e merged (for A) | IS1, then IS1b | IS6 | — |
| 3 | IS1 merged (for C) | IS1d, then IS1b, then IS2a | — | IS4 |
| 4 | IS2a merged | IS2d, then IS2b | — | — (IS3 needs IS2c + ISB) |
| 5 | IS2b merged | IS2c, then IS1c | — | — |
| 6 | IS2c and ISB merged | — | — | IS3 |
| 7 | IS3 merged | — | — | IS5 |

Critical path: IS0a → IS0b → IS1 → IS2a → IS2b → IS2c → IS3 → IS5. IS4 hangs off IS1 and can shorten C's idle time; IS6 is independent of A.

## Suite gate

Run on the last slice and on any slice claiming "perf-neutral":

1. `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` — 0 warnings.
2. `dotnet run --project Poly.Tests/Poly.Tests.csproj` — full suite green; record the pass count in the card's Log.
3. IS3 and IS5 only: `Min`/`Median` for the four `ProgramBenchmarks` against the baseline in [tasks/ISB.md](tasks/ISB.md), using **`--job medium`**. `--job short` is not gateable: two short runs of the same tree differed by up to 2.3x, which ISB measured and recorded. Record the exact command and both numbers.
4. [`pr1-uncommitted-review-gate.md`](../../agent/pr1-uncommitted-review-gate.md) — fail closed; 🔴 and 🟠 block.

Extinction checks at the end of the program — each must return no hits outside this plan's history:

```
LinqExpressionGenerator   CompileValue   LanguageSurfaceTests.Kinds
IsInvokeArgumentScalar    IsPure(        NodeTestHelpers.BuildExpression
```

## What this program deliberately does not do

- **No `NodeKind` enum.** The table is keyed on `Type`. A parallel taxonomy is the twin-tree failure in miniature (`AGENTS.md` §Frozen core: `Node`/`NodeId` is the symbolic primary).
- **No generated visitor over `Node`.** The grammar table is already the single source for the C# surface; a generator adds a build step to hide the same hand-keeping.
- **No interface for one implementation.** `MermaidAstGenerator` and `LinqExpressionGenerator` are deletion or table-read candidates, never new abstractions.
- **No `Lower` or session-pipeline change.** Everything here is producer-side or analysis-side.
- **No analysis-pass precondition machinery.** IS6 owns facts, not a dependency scheduler; the audit defers that until an interpretation-pipeline dependency inventory exists.
- **No writer support invented for a kind the VM cannot execute.** Shipped ⊆ lowerable; a kind with no faithful C# spelling must reject (IS0a), not print.

## Stop conditions

Stop the slice and report — do not guess — when:

1. The card names a decision it does not resolve, or the resolution changes user-visible output.
2. The change needs a file outside the lane.
3. Build or the changed-area tests are still red after two attempts.
4. The card's "Not in this slice" list starts to look necessary; split the card instead.
5. A defect's reproduction does not reproduce. Disposition it in the issue with the evidence; do not "fix" a premise that is false. (IS0b already corrects one.)

## Definition of done

- #255–#261 each closed or dispositioned in its own thread, with a retained repro test when it was a real failure.
- `LinqExpressionGenerator`, `CompileValue`, `LanguageSurfaceTests.Kinds`, the three duplicate loop scaffolds, the two competing scalar predicates, and the duplicate purity predicate are extinct.
- Every concrete `Node` kind has one table entry, and each producer's claim on it is exercised by a test.
- Full suite green; the four benchmark programs within the tolerance IS3/IS5 record.
