# TASK IS2d - the writer defects the differential found

Status: planned. Branch `slice/IS2d`. Lane A (`csharp`). Depends on IS2a. Issue #279. Found by IS2a.
Plan: [IS-README](../IS-README.md). Gates [IS2c](IS2c.md).

## Why

IS2a built the writer→Roslyn differential over 128 corpus programs and pinned 29 disagreements in `Poly.Tests/Interpretation/AstParity.cs` §`KnownDivergences`. They are 11 defect classes. Two of them produce **valid C# with the wrong answer**:

| Program | VM | Printed |
|---|---|---|
| `1.5 + 3 * 7.0` (mixed) | `22.5` | `23` |
| `-(10 + 5)` | `-15` | `-5` |

Both trace to one decision: a constant is printed with the invariant `G` format, so a whole-valued `double` loses its decimal point. `7.0` prints `7`, which turns a division into integer division and loses the parentheses the AST implies.

That is the exact failure `AGENTS.md` §C# surface forbids — a form that folds to a wrong shape rather than failing closed — and it is silent.

Every other class fails loudly at compile time (CS0136, CS0201, CS0019, CS0149, CS0039, CS1059, CS0103, CS0220, CS0020, plus `ToString()` constants that are syntax errors). Those are bugs, but honest ones.

## Scope

Fix by class, smallest first, with the pinned row as the test. **Remove each `KnownDivergences` entry as its class is fixed** — the list must shrink to nothing, and a row may only be deleted when its program agrees, never because it became inconvenient.

1. **Double literal formatting.** A `double` constant prints as a C# literal that re-reads as the same value: a decimal point or exponent always present, `double.NaN` / `double.PositiveInfinity` printed as the qualified expression rather than the bare name. Round-trip must be exact, including `-0.0` and subnormals. This class alone clears the two silent-wrong-value rows plus four CS0103 rows.
2. **Unary minus parenthesisation.** `-(10 + 5)` must keep the parentheses; a negated literal must not become `--99`. The writer already parenthesises `TypeCast`/`TypeAs`/`Conditional`/`Coalesce` in `MemberAccessText`; the unary arm needs the same rule for a compound operand.
3. **Constant fallback.** A constant with no faithful C# literal (`DateTime`, arrays, collections) prints its `ToString()` today. Per #269's precedent it **rejects** through `Unprintable`, naming the type — unless a faithful literal is cheap (`int[]` via a collection expression is worth doing; `DateTime` is not).
4. **Block shadowing.** An inner `Block` may shadow an outer local in this AST and not in C#. Rename on print (a deterministic suffix) rather than reject — the VM executes it, so the writer must print something that executes. This is the one class where a naming decision is the fix.
5. **Block value.** A block's trailing expression prints as a bare expression statement. C# needs the value, so the trailing expression must reach its consumer, not become a statement. Determine whether the writer or the caller owns this and record the answer.
6. **Coalesce on a non-nullable operand.** `??` needs a nullable left side; print the operand through a `?` cast or reject. Prefer whichever keeps the VM's semantics, and record which.
7. **Lambda callee.** A lambda used as a callee prints without the brackets C# requires.
8. **Nested CLR type names.** `Outer+Inner` must print `Outer.Inner`.
9. **`as` on incompatible types.** `42 as string` is CS0039; the VM's `as` is a runtime try-cast yielding null. Print through a form that compiles.
10. **Checked arithmetic and constant division by zero.** The VM wraps (unchecked) and throws at runtime; C# constant-folds both in checked mode. Print so the printed program matches: an unchecked context, and a non-constant divisor where the VM would throw at runtime.
11. **Lambda root.** A bare lambda has no printed value form.

## Files

- `Poly/Interpretation/CSharp/CSharpWriter.cs` — the arms and the literal formatter.
- `Poly/Interpretation/CSharp/CSharpGrammar.cs` — only where a printed form needs a pattern the reader already accepts.
- `Poly.Tests/Interpretation/AstParity.cs` — shrink `KnownDivergences`.
- `Poly.Tests/DomainModeling/Compile/EmitGolden/**` — expected churn; explain the diff.
- `Poly.Tests/Interpretation/CSharp*` — per-class tests where a unit assertion beats a corpus row.

## Shape matrix

| Class | Before | After | Test that proves it |
|---|---|---|---|
| double literal | `7.0` prints `7` | prints `7.0` / a round-tripping literal | its `KnownDivergences` rows disappear; `DoubleLiteral_RoundTripsExactly` covers `-0.0`, subnormal, `NaN`, `±∞` |
| unary minus | `-(10 + 5)` → `-10 + 5` | keeps parentheses | rows disappear; `UnaryMinus_CompoundOperand_KeepsParentheses` |
| constant fallback | prints `ToString()` | rejects, naming the type | rows disappear; `ConstantWithoutLiteral_Rejects` |
| block shadowing | CS0136 | prints a renamed local | rows disappear; `BlockShadowing_PrintsAndExecutes` |
| every other class | its failure | agrees with the VM | row disappears |

## Done when

- `KnownDivergences` is **empty**, and `AstParityTests` is green with no pinned exceptions.
- Every class has a unit test or a corpus row that fails before its fix.
- `EmitGolden` diffs are explained line by line — the double-literal change will touch many files, and that is expected.
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: `KnownDivergences` is empty, every removal is justified by an agreeing row, and golden churn is explained.
NOT SHIP: a row is deleted without its program agreeing; a class is "fixed" by pinning it under a new name; double literals round-trip *approximately*; a shadowed local is rejected because renaming was harder.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpWriterFailClosedTests/*'
```
Then the full suite.

## Hand-edit

Each class is one arm or one formatter, and the test that pinned it is gone from a list that only ever shrank.

## Needs Scot

Two decisions the card does not settle, both recorded rather than guessed:

1. **Block shadowing (class 4).** Renaming on print keeps executability but makes printed output differ from the source names. The alternative — rejecting — would make a VM-executable program unprintable, which contradicts `printed ⊆ simulatable` in the other direction. Recommended and proceeding unless Scot objects: rename.
2. **Constant fallback (class 3).** Rejecting `DateTime` is honest but means such a program cannot be printed at all. If that turns out to block a product path, the fallback is a comment plus a deliberately unprintable marker, and this card stops and reports rather than inventing one.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
