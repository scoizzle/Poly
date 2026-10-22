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
