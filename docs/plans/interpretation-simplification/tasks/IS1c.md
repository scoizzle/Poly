# TASK IS1c - qualified type names and using scope in the C# surface

Status: planned. Branch `slice/IS1c`. Lane A (`csharp`). Depends on IS0b, IS1. Found during IS0a review.
Plan: [IS-README](../IS-README.md). Withdrawal that exposed it: [IS0a](IS0a.md) §Amendment.

## Why

IS0a needed `PopCount` to round-trip. Its only faithful spelling is `System.Numerics.BitOperations.PopCount((ulong)x)`, and the surface cannot express that call. The gap is symmetric and larger than the intrinsic:

| Side | Today |
|---|---|
| **Reader** | Every postfix chain is rooted in `Variable` (`CSharpReader.cs:945`; `FoldPostfixTails:837`). `System.Numerics.BitOperations.PopCount(x)` becomes `Invoke(Member(Member(Member(Variable("System"), …))))` — a *value* chain, so analysis cannot resolve it and the VM cannot execute it. |
| **Writer** | `Expr` prints `TypeReference`/`NamedTypeReference` only in "callee position" (`CSharpWriter.cs:848,853`); `MemberAccessText:994` uses `Expr` for the receiver, so `Member(<type>, name)` has no constructed shape and no test. |
| **Usings** | `CSharpReader` parses `using` directives (`:44`) into `CompilationUnitNode.Usings` and never resolves them. `using System.Numerics;` + `BitOperations.PopCount(x)` is unreachable for the same reason. |
| **Intended AST** | The VM already states it: a type name is *"not a VM value; use it as a `Member`/`Invoke`/`New` receiver"* (`AbiEmitter.cs:273,590`). `Invoke(Member(<type ref>, name), args)` is the shape the VM expects. |

Consequence: the C# surface can neither produce nor consume a static call on a qualified type. That is a language-surface hole, not an intrinsic's problem. It also means any node whose only C# spelling is a qualified call is unprintable — today `PopCount`, tomorrow whatever else needs `Math`/`BitOperations`.

## Scope

1. **Reader: a member chain whose root is a type.** Fold to `Member(<type reference>, name)` instead of a `Variable`-rooted chain. Two routes; pick one at implementation time and record why:
   - **Resolution route (preferred).** Keep `CSharpReader` lexical; let name resolution bind a variable-rooted chain to a static call when the root names a namespace or type. No new grammar, one owner for name binding.
   - **Reader route.** A `qualified-name` pattern for a dotted identifier sequence in call-receiver position. It is lexically identical to a value chain, so it still needs a resolution tie-break — say so in the Log if this route is chosen.
2. **Usings.** Decide whether `Read` resolves short type names against `CompilationUnitNode.Usings`. Minimum bar: a using that names a namespace makes its types resolvable in call-receiver position. Alias usings are out of scope — record them as unsupported rather than half-implementing them.
3. **Writer.** Print a type-reference receiver through `TypeText` in callee position, so `Member(<type>, name)` round-trips.
4. **`PopCount` exit criterion.** Restore it only when the round trip *executes*: `Write` prints the library call, and what `Read` gives back executes on the VM to the same value. Then record the form in `PrintedSubsetVmTests`:
   - `Live` if it re-reads as `PopCount`;
   - a new `Expanded` form if it re-reads as a static `Invoke` that executes — meaning is preserved, node kind is not. Adding that form means adding its law ("an Expanded kind's print re-reads to an executable equivalent"), not just a dictionary entry.

## Files

- `Poly/Interpretation/CSharp/CSharpReader.cs` — chain rooting, using resolution if chosen.
- `Poly/Interpretation/CSharp/CSharpWriter.cs` — type receiver in callee position; `PopCount` arm.
- `Poly/Interpretation/CSharp/CSharpGrammar.cs` — only if the reader route is chosen.
- `Poly/Interpretation/Analysis/Semantics/TypeAndMemberResolutionPass.cs` — only if the resolution route is chosen. **This crosses into lane B**: coordinate before taking it, or split the slice.
- `Poly.Tests/Interpretation/CSharpQualifiedNameTests.cs` — new.
- `Poly.Tests/Interpretation/PrintedSubsetVmTests.cs` — the `PopCount` form.
- `Poly.Tests/Interpretation/CSharpWriterFailClosedTests.cs` — flip the IS0a rejection assertion.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `System.Numerics.BitOperations.PopCount((ulong)x)` read | value chain, unexecutable | static invoke (or `PopCount`) that executes | `QualifiedStaticCall_ReadsAsExecutable` |
| `System.Math.Abs(-1)` | value chain | executes | `FullyQualifiedStaticCall_Executes` |
| `using System;` + `Math.Abs(-1)` | unresolved | executes | `UsingScope_ResolvesShortTypeName` |
| a local named `System`, `System.Foo()` | resolves as a value | **still a value chain — locals win** | `LocalNamedLikeANamespace_Wins` |
| `PopCount` write | rejects (IS0a) | prints the library call | `PopCount_PrintsItsOnlyCSharpSpelling` |
| that text, read | — | executes to the same value | `PopCount_RoundTrip_Executes` |

The shadowing row is not optional: a qualified-name rule that beats a local breaks ordinary programs, and it is the failure mode this route invites.

## Done when

- A fully-qualified static call reads as an executable call, and a `using` makes the short form work.
- A local shadowing a namespace root still reads as a value.
- `PopCount` prints and its round trip executes; its print form is recorded in the audit table.
- No grammar pattern keyed on one library intrinsic remains.
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: the five rows above pass, the shadowing row included, and `PopCount`'s form is honestly recorded as `Live` or `Expanded`.
NOT SHIP: a bespoke pattern for one intrinsic returns; the short-name route works only because the writer was taught to print a special case; `PopCount` is declared `Live` while its re-read tree cannot execute; using aliases are half-supported.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpQualifiedNameTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/PrintedSubsetVmTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
```
Then the full suite.

## Hand-edit

A static call is a `Member` on a type reference in one place, and the reader's chain fold says so in one line.

## Needs Scot

One decision: **where short-name resolution lives** — the reader (one pass, lexical honesty lost) or analysis (`CSharpReader` stays lexical, one owner for name binding). Recommended and proceeding unless Scot objects: resolution, per the recommendation in Scope 1. If that route needs `TypeAndMemberResolutionPass`, this slice touches lane B and must be coordinated with `IS6`.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `5c1dc5ab` | **SHIP.** Every shape-matrix row passes, the shadowing row included; `PopCount` prints its library call and the round trip executes, recorded `Expanded`. Full suite 4,474 passed / 0 failed / 0 skipped; `AstParityTests` 145/145; `Poly.slnx` build 0 warnings. No lane-B file touched. | See §Route, §Red first, §`PopCount` form and §Writer finding below. |

### Route (2026-10-22)

**Qualified names: resolution route, and it was already in place.** On the untouched tree
`CSharpScript.Run("System.Numerics.BitOperations.PopCount((ulong)7)")` already answered `3` and
`System.Math.Abs(-1)` answered `1`: `DottedTypeNames` + `TypeAndMemberResolutionPass` bind the
dotted spine to a type receiver, and `VariableLifetimePass` keeps a bound root a value. #278's
reader premise ("analysis cannot resolve it") is stale in this tree; what was still missing was
using scope and the writer side. No new grammar pattern, and **no change to
`TypeAndMemberResolutionPass.cs`** — the slice stayed inside lane A.

**Using scope: resolved in `Read`** (`CSharpReader.cs`, which Scope 2 and §Files name for this).
A receiver the unit's own usings import folds to `NamedTypeReference(name, namespace)`, so
`using System.Numerics;` + `BitOperations.PopCount(x)` reaches the same static call as the
qualified spelling. Chosen because binding a *bare identifier* as a type is a scope decision
owned by `VariableLifetimePass`: `ScopeValidator` reports `Variable 'X' is not declared in this
scope` for `Math.Abs(-1)` before `TypeAndMemberResolver` could rewrite the root, so an
analysis-only using route needs `Poly/Interpretation/Analysis/Semantics/VariableLifetimePass.cs`
(lane B). This slice did not take that file. The cost, recorded: a little name binding lives in
the reader.

**The shadowing guard.** `Parser.ValueNames` is the set of identifiers the text spells anywhere
other than before a `.`. Every declaration form in the exported subset leaves its name before a
non-`.` token (`x =`, `x;`, `x,`, `x)`, `x in`, `x =>`), so a declared name is always in the set:
a local, field or parameter wins and the chain stays a value chain. A missed bind is a
conservative under-resolution that fails closed, never a wrong static call. The first directive
naming the type wins, as a namespace import does.

**Recorded unsupported:** alias usings — the `using` rule takes a dotted name only, so
`using X = System;` does not parse. A directive placed after a statement (invalid C#) does not
bind earlier statements; that also fails closed.

### Red first (2026-10-22)

`dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpQualifiedNameTests/*'`
on the new file, before any production change: **3 of 6 failed**.

- `PopCount_PrintsItsOnlyCSharpSpelling`, `PopCount_RoundTrip_Executes` —
  `CSharpWriter rejected PopCount (NodeId …): its only C# spelling is a fully-qualified static call…`.
- `UsingScope_ResolvesShortTypeName` — `Expected to be of type NamedTypeReference … but Value is of
  type Variable`.
- **Passed already, recorded:** `QualifiedStaticCall_ReadsAsExecutable`,
  `FullyQualifiedStaticCall_Executes`, `LocalNamedLikeANamespace_Wins` — the shadowing row was
  expected to be able to pass immediately, and it did.

### `PopCount` form: `Expanded` (2026-10-22)

Prints `System.Numerics.BitOperations.PopCount((ulong)x)`; the text re-reads as
`Invoke(Member(<type reference>, "PopCount"), TypeCast(x, ulong))`, which executes to the same
value. Meaning preserved, kind not — an `Expanded` print, not `Live`. The form is recorded in
`PrintedSubsetVmTests.Forms` **with its law**
(`ExpandedKinds_PrintReReadsToAnExecutableEquivalent`: an Expanded kind's print re-reads to an
executable equivalent), and `NodeFacts` gains `Producers.WriterExpanded` so `NodeFactsTotalityTests`
ties every such claim to a sample the law executes. `AstParityTests` moved PopCount's row from
"writer refused" to both sides agreeing (`long:3`); `VmExecutes_AndWriterRejects_IsAFailure` now
pins that decision synthetically, because the corpus no longer carries the shape.

### Writer finding (2026-10-22)

`Member(<type ref>, name)` already printed correctly through `Expr`'s callee-position type branch;
routing the receiver through `TypeText` instead escaped a keyword-named CLR type and turned every
golden's `object.ReferenceEquals` into `@object.ReferenceEquals` (43 failures). Reverted: the
existing spelling is the rule, and `TypeReceiver_PrintsAsTheTypeName` now pins it (`int.MaxValue`,
`System.Numerics.BitOperations.PopCount`) so the shape cannot regress silently. `EscapeTypeName`'s
keyword escaping is declaration-side and unchanged.
