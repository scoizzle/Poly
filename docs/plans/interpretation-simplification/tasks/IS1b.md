# TASK IS1b - Mermaid reads the table instead of re-deriving dispatch

Status: planned. Branch `slice/IS1b`. Lane A (`csharp`). Depends on IS1.
Plan: [IS-README](../IS-README.md). Audit §1 and its research limits.

## Why

`MermaidAstGenerator` (593 LOC, `Poly/Interpretation/Mermaid/`) is a debugging aid with zero production references — its consumers are two test classes. It answers "what does this node mean" twice more (`:236-417` statements, `:436-536` expressions), and its unknown-kind arms are silent: a type name, or nothing. The expression switch drops roughly fifteen kinds' children without a word (`:536`), so a diagram can be missing a whole subtree and look complete.

The audit's instruction is explicit: keep it as a debugging aid fed by the IS1 table, or delete it. This card keeps it, because it is the only view of the tree a human can read at a glance — but it stops being a third place that decides what a node is.

## Scope

- **Labels come from the table.** `NodeFact.Label` replaces the per-kind label switch. A kind the table calls `Mermaid`-claimed gets its label; a kind not claimed does not silently render as its type name.
- **Children come from the tree.** Walk `Node.Children` generically for any kind without a bespoke edge rule. Delete the "unknown kind → no children" arm; the only legitimate reason to omit a child is a declared traversal boundary (none today), and it must be named in one place.
- **Honour the claim.** Every `Mermaid`-claimed kind renders, and each of its direct children appears in the output. That is the assertion the switch could never make.
- Keep the generator's public surface (`Generate`, the `AnalysisResult` overload, direction). Do not add an interface, a renderer abstraction, or a second diagram type.
- Update `Poly/Interpretation/README.md:107,259` to say the generator is table-driven.

## Files

- `Poly/Interpretation/Mermaid/MermaidAstGenerator.cs`
- `Poly.Tests/Integration/MermaidAstVisualizationTests.cs`
- `Poly.Tests/Interpretation/MermaidAstGeneratorTests.cs`
- `Poly/Interpretation/README.md`

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| a claimed kind with a bespoke arm | labelled by hand | labelled from the table | existing visualisation tests stay green |
| a claimed kind with **no** bespoke arm | type name, children dropped | `NodeFact.Label`, all children present | `UnhandledKind_RendersItsLabelAndChildren` |
| every `Mermaid`-claimed kind | only sampled kinds covered | all covered | `EveryClaimedKind_RendersEveryChild` |
| a `Producers == None` kind | renders as a type name | does not render as a claimed node | `UnclaimedKind_IsNotPresentedAsADiagramNode` |

## Done when

- No per-kind label literal remains in `MermaidAstGenerator` that duplicates `NodeFact.Label`.
- The generic child walk has no silent-empty arm.
- The claim test iterates the table, not a hand list.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: labels and coverage both come from one table, and a newly claimed kind is covered without editing the generator.
NOT SHIP: a second hand-kept kind list appears in the generator or the tests; a kind still renders as its CLR type name; the generator was given an interface.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MermaidAstGeneratorTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MermaidAstVisualizationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'
```
Then the full suite.

## Hand-edit

The label table is the same one every other producer reads; the walk is `Children` and a node id.

## Needs Scot

Recommended and proceeding unless Scot objects: keep the generator rather than delete it. It has no production caller, but it is the only human-readable view of an analyzed tree and it costs one table read once IS1 exists. If Scot would rather delete it, this card becomes a deletion plus test removal — say so before implementation starts, not after.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | — | RED: the generator still re-derives dispatch, so the table's claim cannot hold | `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MermaidAstGeneratorTests/*'` → 8 total, 4 failed: `EveryClaimedKind_RendersEveryChild` — `Add: rendered without its table label; And: …; UsingStatement: …` (all 29 claimed kinds); `Generate_UnclaimedRoot_DoesNotRenderItsTypeName` — `Expected to be false but found True`; `UnhandledKind_RendersItsLabelAndChildren` — `Expected to contain ""LabelDeclaration"" but received "graph TB … ["Label outer"]"`; `UnclaimedKind_IsNotPresentedAsADiagramNode` — `Expected to be false but found True`. |
| 2026-10-22 | agent | Copilot/VS Code | `907e7fb2` | GREEN: labels and children both come from the one table | `AppendNodeLabel` and `AppendValue` deleted; `AppendNodeDefinition` prints `NodeFact.Label`. `GetChildren` walks `Node.Children` for every kind; the old child switch survives only as `BespokeEdgeLabels` (labels, matched by `ReferenceEquals`), so a bespoke rule can no longer drop a child, and the `_ => Array.Empty` "no children" arm is gone (`_ => []` now means "no bespoke edge labels"). 46 kinds are `Mermaid`-claimed and every one renders its label and every direct child: `MermaidAstGeneratorTests` 8/8, `MermaidAstVisualizationTests` 12/12, `NodeFactsTotalityTests` 7/7. Full suite 4333 passed, 0 failed; `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` 0 warnings. |
| 2026-10-22 | agent | Copilot/VS Code | `907e7fb2` | Claim audit: none of the 29 claims was wrong; the claim was incomplete and 17 rows were corrected | (a) Every claimed kind rendered and every child of every claimed kind is reachable through `Node.Children` — no disagreement of the kind the card's stop condition asks about, except that the bespoke `Invoke` arm omitted `TypeArguments`, the `TryCatchFinally` arm omitted a catch clause's `ExceptionType`, and the `Block` arm omitted `Variables`; the tree walk renders all three now. (b) The claim itself was incomplete: 17 kinds the old generator labelled by hand — `Block`, `Conditional`, `Constant`, `DoWhileLoop`, `ForLoop`, `IfStatement`, `LabelDeclaration`, `New`, `Parameter`, `Return`, `SwitchStatement`, `ThisReference`, `TypeAs`, `TypeCast`, `TypeIs`, `Variable`, `WhileLoop` — were unclaimed, so enforcing the claim would have erased constants, parameters and every control-flow container from every diagram. They are now `Producers.Mermaid`, evidenced by the corpus cases the claim test exercises (46 claimed of 85 kinds). |
| 2026-10-22 | agent | Copilot/VS Code | `907e7fb2` | Deviations | (a) A kind the table does not claim renders as `unclaimed: <Kind>`: it is neither a silent type name nor absent. Passing such a node's children up to the nearest claimed ancestor was rejected because it loses the kind — the same silent omission this slice removes (`Assignment` over `BitwiseNot` would render as `Assignment` over `Constant`). (b) `NodeFactsTotalityTests.CorpusKind_ClaimedByMermaid_Renders` moved to `MermaidAstGeneratorTests.EveryClaimedKind_RendersEveryChild`: its old `LabelOf(diagram) == fact.Label` assertion required the label to differ from the table label, the opposite of this card. The new test is the same corpus walk, one class nearer the producer. (c) Shape matrix row 1's "existing visualisation tests stay green" could not hold literally — `NodeFact.Label` is the kind name, so `Add (+)`, `Constant 4`, `Cast to Double` became `Add`, `Constant`, `TypeCast`; those assertions were retargeted at the table label or the node id. (d) `GetNodeShape` and `AddStyleAnnotations` still name a few kinds for shape/fill; presentation only, no meaning dispatch, and untouched here. |
