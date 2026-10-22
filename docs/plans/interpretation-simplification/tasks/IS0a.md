# TASK IS0a - the writer fails closed, and what it prints it prints as itself

Status: planned. Branch `slice/IS0a`. Lane A (`csharp`). Issues #255, #256.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §1 and its step-2 gate.

## Why

`CSharpWriter` is a production artifact producer — domain C# goes out through `CSharpLanguage.Write` (`Poly/DomainModeling/Compile/DomainSession.cs:321,331,343`). Two failures make its output untrustworthy:

1. **The unknown-kind fallback prints the type name as source.** Expression default `:971-972` returns `node.ToString()`; statement default `:266-269` prints the node as an expression statement. `AGENTS.md` §C# surface requires fail-closed: a form that cannot execute soundly throws, it never folds to a wrong shape.
2. **Three kinds do not survive `Write(Read(x))`** (#256): `PopCount` → `Invoke`, `NewArray` → `TypeCast(New)`, `StridedSetBits` → `Comment` — and a comment deletes a VM-executed operation.

This is the gate for everything in lane A: a totality test (IS1) and a writer→Roslyn oracle (IS2a) both certify garbage while the writer lies.

## Scope

- Delete both silent fallbacks. They throw a rejection naming the node kind (`InvalidOperationException`, matching the VM's `RejectCompile` shape at `Poly/Interpretation/Vm/Abi/AbiEmitter.cs:313-314`; `Node` carries no source position, only `NodeId`, so name the kind and include the id).
- `NewArray` (`:962-964`) prints through the existing `new-array` pattern instead of hand-concatenation: `Render("expr-primary", "new-array", TypeText(newArray.ElementType), Expr(newArray.Length))` (pattern at `CSharpGrammar.cs:449`, reader arm at `CSharpReader.cs:962`). It must re-read as `NewArray`.
- `PopCount` (`:958-959`) is spellable and VM-executed, so it round-trips: add a `popcount` pattern to the C# grammar and a reader arm that folds it back to `PopCount`. Print through the pattern, never a literal. The pattern must match the **real C# shape** the writer already emits — `System.Numerics.BitOperations.PopCount((ulong)<expr>)`, built from literal identifier segments plus `Ref("type")`/`Ref("expr")` — because a bespoke `popcount(x)` spelling would print invalid C# and fail IS2a's Roslyn parse gate.
- `StridedSetBits` (`:960-961`) has no faithful C# spelling. It rejects. Do not invent a surface form and do not print a comment that deletes behaviour.
- Inventory every kind that reaches either default today. Verified starting points: `ParameterReference`, `AttributeNode`, `ResolvedTypeReference`, `UnionTypeReference` have no `case` in `CSharpWriter` and are all `Node`s, so `Write(node)` falls through `WriteStatement` → `Expr` → the fallback. (The issue's hint `VariableScope` is **not** a `Node` — `VariableScope.cs:12` does not derive from `Node` — so drop it from the inventory.) Add an arm only where the writer is genuinely supposed to spell the kind; otherwise it rejects. Record the inventory in the card's Log.

## Files

- `Poly/Interpretation/CSharp/CSharpWriter.cs` — the two defaults and the three arms; the new `popcount` render call.
- `Poly/Interpretation/CSharp/CSharpGrammar.cs` — `popcount` pattern (with the reader's pattern name).
- `Poly/Interpretation/CSharp/CSharpReader.cs` — `popcount` arm.
- `Poly.Tests/Interpretation/CSharpLanguageTests.cs` (or the reader/writer class that already owns round-trip facts) — the tests below.
- `Poly.Tests/Interpretation/CSharpWriterFailClosedTests.cs` — new, if the existing class is already large.

## Shape matrix

| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| a kind with no writer arm (e.g. `ParameterReference`) | prints `Poly.Ast.Nodes.ParameterReference` | throws, naming the kind | `UnknownKind_Throws_InsteadOfPrintingItsTypeName` |
| `StridedSetBits` | `/* StridedSetBits */` | throws | `StridedSetBits_IsRejected` |
| `NewArray` | `(T[])(new T[n])`, re-reads as `TypeCast` | pattern-printed, re-reads as `NewArray` | `NewArray_WriteRead_IsKindStable` |
| `PopCount` | `BitOperations.PopCount(...)`, re-reads as `Invoke` | pattern-printed, re-reads as `PopCount` | `PopCount_WriteRead_IsKindStable` |

## Done when

- Both defaults throw; no path returns `node.ToString()` as source.
- `Write(Read(x))` is kind-stable for `NewArray` and `PopCount`; `StridedSetBits` rejects.
- Each new arm is reached by a test, and each rejection is asserted with its message.
- Emit golden files either unchanged or regenerated deliberately, with the diff explained in the Log.
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: the tests above pass, goldens are unchanged or the change to them is explained line by line, and the inventory of newly-rejecting kinds is in the Log with a reason per kind.
NOT SHIP: any kind still prints a type name; `PopCount` or `NewArray` still loses its kind; a golden changed without an explanation; the `popcount` pattern has no reader arm.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpWriterFailClosedTests/*'
```
Then the full suite.

## Hand-edit

The next missing kind is a one-line `case` in a table the file already shows, and its absence throws instead of shipping a corrupt file.

## Needs Scot

Recommended and proceeding unless Scot objects: `PopCount` gets a grammar pattern rather than being refused, because the VM executes it and a refusal would make a legal AST unprintable; `StridedSetBits` refuses because its only faithful form is a comment, which deletes it.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
