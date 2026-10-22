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
