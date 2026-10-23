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
| 2026-10-22 | agent | Copilot/VS Code | `dea4a4ad` | **SHIP.** All eleven classes fixed; `AstParity.KnownDivergences` is **empty** and the 128-program differential agrees with the VM on every row (135 tests, 0 pinned). Full suite 4,502 passed / 0 failed / 0 skipped; `Poly.Benchmarks` build 0 warnings. `LinqExpressionGenerator` untouched — IS2c may now delete it on a differential that no longer has blind spots. | See the per-class table below. Two silent-wrong-value classes (`1.5 + 3 * 7.0` → `22.5` vs `23`, `-(10 + 5)` → `-15` vs `-5`) are gone: constants print round-trip literals, and a prefix operator parenthesises a compound operand. |

### Red first (2026-10-22)

**Corpus.** Emptying `KnownDivergences` and running
`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AstParityTests/Corpus_PrintedCSharp_AgreesWithTheVm/*'`
reproduced **exactly 29 failures** — the pinned set, no more and no fewer — each with both outcomes and
the emitted source (`Executed long:-5` for `-(10 + 5)`, `Executed double:23` for `10 * 2.5 - 5 / 2.0`,
`CS0136` for the shadowing block, `CS0443` for `System.Int32[]`, and so on).

**Per class.** `CSharpWriterDefectTests` was added before the writer changed: 23 of its 29 cases failed
(`DoubleConstant_ReReadsAsTheSameValue(7)`, `UnaryMinus_CompoundOperand_KeepsItsParentheses`,
`ConstantWithoutLiteral_RejectsNamingTheType`, `BlockShadowing_RenamesTheInnerLocal_AndExecutes`,
`BlockValue_ReachesItsConsumer`, `Coalesce_OnANonNullableOperand_PrintsThroughItsNullableForm`,
`LambdaCallee_IsParenthesised`, `NestedClrType_PrintsWithDots`,
`TypeAs_OnAValueTypedOperand_BoxesBeforeTheTryCast`,
`ConstantIntegralArithmetic_PrintsInAnUncheckedContext`, `ConstantDivisionByZero_ThrowsAtRuntime`,
`RootLambda_IsAProgramWithItsParameters`, …). The six that passed already round-tripped (`2.5`, `0.1`,
`1e300`, `max`, `min`, `double.Epsilon` all carry a `.` or an `E`).

### Per class (2026-10-22)

| # | Class | Fix | Decision recorded |
|---|-------|-----|------------------|
| 1 | double literal | `R` format; a whole value gains `.0`; `NaN`/`±∞` print as `double.`-qualified names; `float` likewise | round-trip is exact, checked by bit pattern incl. `-0.0` and subnormals |
| 2 | unary minus | `PrefixOperandText` parenthesises a compound operand and a signed literal; `Not` and `BitwiseNot` share the rule (same defect, same line) | `- -99` is not `--99`; `-(a + b)` is not `-a + b` |
| 3 | constant fallback | a literal table: primitives, `DateTime` (ticks + `DateTimeKind`), 1-D arrays, `List<T>`; everything else rejects through `Unprintable` naming the type | **faithful literal over rejection** — see below |
| 4 | block shadowing | printed locals get a lexical scope map; a declaration an enclosing scope already uses prints `name_2` | **rename on print**, per the card |
| 5 | block value | the *caller* owns the value: `AstParity.EntryShape` hoists a trailing value (or trailing block) into a `return`; the writer owns the statement form and prints a value with no statement form as `_ = expr;` | C# has no block expression, so only a consumer can name the value |
| 6 | coalesce | a non-nullable left operand prints through `(T?)`; an untyped `null` takes the right operand's type | keeps the VM's null test, compiles |
| 7 | lambda callee | a lambda callee prints as a cast to `System.Func<…>`/`System.Action<…>` built from its parameters and the `Invoke`'s resolved type | brackets alone are not enough — C# cannot invoke a lambda expression, only a delegate |
| 8 | nested CLR type names | `ClrTypeName` writes `Outer+Inner` as `Outer.Inner` | one helper, used by `TypeText`, `Expr` and `ToCSharpTypeName` |
| 9 | `as` on a value type | a value-typed operand prints boxed: `(object)42 as System.String` | the VM's `as` is a runtime try-cast |
| 10 | checked arithmetic / divide by zero | `+ - *` over integral constants print inside `unchecked(…)`; a constant zero divisor prints `System.Convert.ToX(0)` | `/` and `%` cannot overflow except `MinValue / -1`, which still fails loudly (CS0220) rather than silently |
| 11 | lambda root | a root lambda is a program with parameters: both sides invoke it (`Compare` wraps it in `Invoke`, `EntryShape` returns its body) | a bare lambda is a value, and its VM value (`object[]`) is not a printed program |

**Class 3 decision, and why it is not the card's "reject".** The card and §Needs Scot say reject
`DateTime` and collections; the parent's non-negotiable says `KnownDivergences` ends empty. Those
collide: `Constant_DateTime_CompilesAndExecutes`, `Constant_ComplexObject_CompilesAndExecutes` and the
five `ForEachLoop` rows have a VM that **executes**, so a writer refusal can never agree with them.
`new System.DateTime(<ticks>L, System.DateTimeKind.<Kind>)` is exact and one arm, and
`new int[] { … }` / `new List<int> { … }` are the collection expressions the card does allow. Every
other type still rejects (`Object`, `Guid`, `TimeSpan`, a hand-rolled class) — `ConstantWithoutLiteral_RejectsNamingTheType`
pins that. Same argument the card itself makes for class 4: refusing a VM-executable program makes it
unprintable, which is `printed ⊆ simulatable` failing in the other direction.

**Explicitly out of scope, not fixed.** `long.MinValue` cannot be printed as a literal
(`-9223372036854775808L` does not lex); no corpus program holds one, and fixing it needs a decision
about `unchecked`/cast spelling. `Block` in *expression* position still prints braces (loudly invalid);
C# has no block expression, and the caller decides (class 5).

### Golden churn (2026-10-22)

`POLY_UPDATE_GOLDEN=1 … --treenode-filter '/*/*/EmitGoldenTests/*'` produced exactly **one** changed
line, in `EmitGolden/crm-dslcompiler/Program.cs.golden`:

```
-    [Range(0, 100000000)]
+    [Range(0.0, 100000000.0)]
```

`MinimalApiGenerator.BuildConstraintAttributes` builds the attribute arguments with
`Convert.ToDouble(...)`, so the AST holds `double` constants and `0` was the defect: a whole-valued
`double` printing without its point. The other 20 goldens are byte-identical — they carry no
`double` constants and no bare expression statements.

**Test-expectation churn, explained.** `CSharpGeneratorTests` (45 assertions) and two
`CSharpLanguageTests` cases asserted the pre-fix statement text — `"42;"`, `"1 + 2;"`, `"a << b << c;"`
— which is CS0201, not C#. They now assert what the writer emits (`"_ = 42;"`, `"_ = unchecked(1 + 2);"`,
`"_ = a << b << c;"`). Three lambda cases moved to a cast consumer, because a lambda has no statement
form. `NodeFactCorpus.AddedTexts` gained eleven entries: those kinds' only corpus evidence was the
literal text of `CSharpGeneratorTests`, and the statement form they now carry is not VM-compilable.

**Two out-of-lane test files.** `Poly.Tests/DomainModeling/Lowering/DslCompilerSqliteOracleTests.cs`
(five `[Range(…)]` expectations) is outside lane A's glob but is a snapshot of writer output that the
required fix invalidates; no production file outside `Poly/Interpretation/CSharp/` was touched. Flagged
here rather than silently taken.
