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
| 2026-10-22 | agent | Copilot/VS Code | `696f91a1` | SHIP — both defaults deleted; `NewArray`/`PopCount` kind-stable; `StridedSetBits` rejected | Nine kinds now reject (inventory below); goldens unchanged; full suite 4289/4289; build 0 warnings |

### Evidence (2026-10-22)

RED — `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpWriterFailClosedTests/*'`:

```
failed UnknownKind_Throws_InsteadOfPrintingItsTypeName (24ms)
  AssertionException: Expected InvalidOperationException but got AssertionException: Expected InvalidOperationException but no exception was thrown
failed KindsWithoutWriterArm_Throw_InsteadOfPrintingTheirToString (24ms)
  AssertionException: Expected InvalidOperationException but got AssertionException: Expected InvalidOperationException but no exception was thrown
failed StridedSetBits_IsRejected (0ms)
  AssertionException: Expected InvalidOperationException but got AssertionException: Expected InvalidOperationException but no exception was thrown
failed NewArray_WriteRead_IsKindStable (29ms)
  AssertionException: Expected to be equal to "new System.Int64[4L];"
  but received "(System.Int64[])(new System.Int64[4L]);"
failed PopCount_WriteRead_IsKindStable (113ms)
  AssertionException: Expected to not be null
  but received null
Test run summary: Failed!  total: 5 / failed: 5 / succeeded: 0
```

(`PopCount` printed `System.Numerics.BitOperations.PopCount((ulong)7L)` before the change too, but the reader folded it to `Invoke`, so `Find<PopCount>` was null.)

GREEN — same filter: `total: 5 / failed: 0 / succeeded: 5`.
Full suite `dotnet run --project Poly.Tests/Poly.Tests.csproj`: `total: 4289, failed: 0, succeeded: 4289, skipped: 0`.
`dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`: `0 Warning(s), 0 Error(s)`.
Goldens: unchanged — no file under `Poly.Tests/DomainModeling/Compile/EmitGolden/` was modified, and both golden suites (read/round-trip and emit) pass.

### Inventory: every kind that reached a writer default

Reachable through `Write(Node)` → `WriteStatement` → `Expr` → `default`. An arm was added only for `NewArray` and `PopCount`; the rest reject.

| Kind | Before | After | Why it does not get an arm |
|---|---|---|---|
| `ParameterReference` | printed `ParameterReference` | rejects | It is a `Member.Value` marker that `MemberAccessText` prints as the member's identifier; there is no standalone form. |
| `AttributeNode` | printed `AttributeNode { … }` | rejects | Printed by `AttributeText` in attribute position; not a statement or expression. |
| `PropertyGetterDefinitionNode` | printed `get;` | rejects | Printed by `PropertyText`; an accessor is not a standalone statement. |
| `PropertySetterDefinitionNode` | printed `set` | rejects | Same. |
| `PropertyInitializerDefinitionNode` | printed `= …` | rejects | Same. |
| `UnionTypeReference` | printed `Int32 \| String` | rejects | Analysis collapses a union before print; a mixed option set has no C# spelling. |
| `ResolvedTypeReference` | printed `ResolvedTypeReference { … }` | rejects | A type position only (`TypeText`); no expression or statement form. |
| `CompilationUnitNode` | printed `CompilationUnitNode { … }` | rejects | Has the dedicated `Write(CompilationUnitNode)` overload; not a statement. |
| `StridedSetBits` | `/* StridedSetBits */` | rejects | No faithful C# spelling; the old form was a comment that deleted a VM-executed operation. |

### Deviations from the card

1. **`Render` → `RenderFills`.** The card's `Render("expr-primary", "new-array", …)` cannot work: the pattern's brackets are named `open`/`close` `Value` positions, so `Render` throws `No print fill for 'open'`. The arms use `RenderFills(…, Names(("open", "["), ("close", "]")), …)`.
2. **`NewArray` loses the redundant `(T[])` cast.** The `new-array` pattern is `new T[n]`, which is already `T[]`; the cast was the reason the old text re-read as `TypeCast(New)`. Printed length keeps the writer's constant spelling, so a literal length prints `4L`.
3. **`PrintedSubsetVmTests.cs` was edited, and it is not a named lane-A file.** The lane table's `Poly.Tests/Interpretation/*Vm*` glob puts it in lane C, but it is the print-form audit *of this writer* and it asserted the exact defect #256 removes (`StridedSetBits` prints `/* StridedSetBits */`). Left alone, the suite cannot be green. The edit is four lines: a `Rejected` print form, `StridedSetBits` mapped to it, and its comment assertion replaced by a rejection assertion. Revert only with a replacement audit for the rejected form.
4. **`CSharpGeneratorTests` two facts** pinned the retired fallbacks: `StridedSetBits` printing the comment and `UnionTypeReference` printing its `ToString`. Both now assert the rejection; `Generate` for `PopCount` pins the printed text exactly.


## Amendment 2026-10-22 — the `popcount` grammar pattern is withdrawn

The first implementation made `PopCount` round-trip by adding a `popcount` pattern to `expr-primary` with four literal-text `Predicate` matches (`System`, `Numerics`, `BitOperations`, `PopCount`). Scot rejected it, correctly: it makes one library intrinsic a member of the language grammar, it does not generalize (the next intrinsic needs another pattern), and it puts four predicate matches in the path of every primary expression.

Withdrawing it exposed the real gap, which is why the withdraw is a slice ([`IS1c`](IS1c.md)) rather than a revert:

- the reader roots **every** postfix chain in `Variable` (`CSharpReader.cs:945`, `FoldPostfixTails:837`), so `System.Numerics.BitOperations.PopCount(x)` reads as a value chain, not a static call;
- the reader parses `using` directives (`:44`) and never resolves them;
- the VM already states the intended shape — a type name is "not a VM value; use it as a `Member`/`Invoke`/`New` receiver" (`AbiEmitter.cs:273,590`).

What changed in this slice instead: `PopCount` now rejects through `Unprintable` with a message that names the gap and points at IS1c. It joins `StridedSetBits` in `PrintForm.Rejected`. Nothing in production constructs `PopCount` — `grep -rn "new PopCount"` hits only the reader arm removed here and tests — so no shipped path loses a capability.

The card's shape-matrix row `PopCount_WriteRead_IsKindStable` is superseded by `PopCount_IsRejected_UntilQualifiedStaticCallsResolve`; IS1c restores the round trip once the surface can express a qualified static call.
