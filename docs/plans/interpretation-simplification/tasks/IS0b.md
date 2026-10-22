# TASK IS0b - a switch case terminates, and the printed switch reads back

Status: planned. Branch `slice/IS0b`. Lane A (`csharp`). Issue #257.
Plan: [IS-README](../IS-README.md).

## Correcting the issue's premise before implementing

#257 says "the reader cannot read a break". Checked on `9522d536`: **it can.**
`CSharpGrammar.cs:324` defines `break;` as a `statement`, `CSharpReader.cs:564` returns `BreakStatement`, and `CSharpWriter.cs:233,237` print `break`/`continue`. Do not write a "add break support" test — it will pass before the change.

What is actually broken:

1. **`SwitchText` never terminates a case.** `CSharpWriter.cs:501-518` prints `case <pattern>:\n<one statement>` with no `break`. Printed C# is invalid (CS0163) unless every case body returns or throws, and the golden/Roslyn paths compile printed output.
2. **`switch-case` consumes exactly one `statement`.** `CSharpGrammar.cs:356-358`. So the shape the writer *needs* to emit — body then `break` — is not re-readable, and an ordinary C# `switch` with breaks cannot be read at all.

Both are one asymmetry: the writer's valid form and the reader's accepted form disagree.

## Scope

- `SwitchText` emits a terminating `break` after a case body that does not already end in `return`, `throw`, `break`, `continue`, or `goto`. `default` gets the same treatment. No braces, no `Block` wrapper — the AST body stays the body.
- `switch-case` gains the two variants that accept that text, plus ordinary hand-written C#:
  - `case <expr> : <statement> break ;`
  - `default : <statement> break ;`
- The reader consumes the trailing `break` as **syntax, not a node**, and returns the statement unchanged. A Poly `SwitchStatement` has no fallthrough, so a trailing `break` carries no meaning; dropping it keeps `Read(Write(x))` structurally identical instead of wrapping every case body in a `Block`.
- A case body that already terminates prints without an added `break`; the reader accepts either form.
- Add a golden file that contains a `switch`, so the golden corpus stops being silent about this kind.

## Files

- `Poly/Interpretation/CSharp/CSharpWriter.cs` — `SwitchText`.
- `Poly/Interpretation/CSharp/CSharpGrammar.cs` — two `switch-case` variants.
- `Poly/Interpretation/CSharp/CSharpReader.cs` — the matching arms and the trailing-break drop.
- `Poly.Tests/Interpretation/CSharpLanguageTests.cs` — round-trip and shape tests.
- `Poly.Tests/DomainModeling/Compile/EmitGolden/**` — one golden with a switch, if no existing golden has one (check first; do not add a duplicate).

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `switch(x){case 1: a();}` | `case 1:\n a();` (CS0163) | `case 1:\n a();\n break;` | `SwitchCase_PrintsTerminatingBreak` |
| that printed text, re-read | fails to parse | `SwitchStatement` with the original body | `Switch_WriteRead_RoundTrips` |
| `case 1: a(); break;` from hand-written C# | fails to parse | reads, break dropped | `SwitchWithBreak_Reads_WithoutAddingANode` |
| `case 1: return x;` | prints as-is | still no added `break` | `SwitchCase_EndingInReturn_GetsNoBreak` |
| printed output through Roslyn | CS0163 | zero diagnostics | `PrintedSwitch_ParsesWithRoslyn` |

## Done when

- Printed output for a switch with plain-statement cases parses with zero Roslyn syntax diagnostics.
- `Read(Write(x))` for a switch is structurally equal to `x` (no injected `BreakStatement`, no `Block` wrapper).
- A hand-written `switch` with `break` reads.
- The golden corpus contains a switch and is green.

## SHIP if / NOT SHIP if

SHIP: the five tests above pass, goldens green, and `SwitchText` has no unreachable-`break` output.
NOT SHIP: a `BreakStatement` is injected into the read tree; the writer emits a break after a terminating body; a golden was regenerated without showing the switch in the diff.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpRoundTrip*/*'
```
Then the full suite.

## Hand-edit

`SwitchText` is one loop whose new line is the terminator, and the grammar shows both accepted forms next to the old one.

## Needs Scot

Recommended and proceeding unless Scot objects: the trailing `break` is treated as syntax and dropped on read, rather than becoming an AST `BreakStatement`. A case in this AST cannot fall through, so a node for it would be a second representation of the same fact — and injecting it would make `Read(Write(x))` differ from `x` on every switch.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-22 | agent | Copilot/VS Code | `8acbd5bc` | SHIP — `SwitchText` terminates every case; `switch-case` reads the printed form; trailing `break` dropped as syntax | RED 4/5 named tests fail (CS0163/CS8070 captured); GREEN `CSharpLanguageTests` 193/193; full suite 4309/4309; build 0 warnings; one hand-authored golden added |

### Evidence (2026-10-22)

RED — `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CSharpLanguageTests/*'`:

```
failed SwitchCase_PrintsTerminatingBreak (7ms)
  AssertionException: Expected to be 3, because each case body and the default case need a terminator
  but found 0
failed SwitchWithBreak_Reads_WithoutAddingANode (4ms)
  FormatException: Expected type declaration, got 'class' (line 1, col 1)
     at Poly.Interpretation.CSharp.CSharpReader.Parser.Take(String rule, String context)
failed Switch_WriteRead_RoundTrips (5ms)
  AssertionException: Expected to be 3, because the round trip must read back the terminating break the writer prints
  but found 0
failed PrintedSwitch_ParsesWithRoslyn (1s 032ms)
  AssertionException: Expected to be empty
  but collection contains items: [CS0163 Control cannot fall through from one case label ('case 1:') to another,
    CS0163 Control cannot fall through from one case label ('case 2:') to another,
    CS8070 Control cannot fall out of switch from final case label ('default:')]
Test run summary: Failed!  total: 190 / failed: 4 / succeeded: 186
```

`SwitchCase_EndingInReturn_GetsNoBreak` passed before the change, as it must: before the writer emitted any terminator there was nothing to suppress, and after the change it guards against adding one to a body that already leaves the switch.

GREEN — same filter: `total: 193 / failed: 0 / succeeded: 193` (three more rows: the new golden × Roslyn-agrees / round-trip / parses).
Full suite `dotnet run --project Poly.Tests/Poly.Tests.csproj`: `total: 4309, failed: 0, succeeded: 4309, skipped: 0`.
`dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj`: `0 Warning(s), 0 Error(s)`.

Printed form now produced (and read back structurally identical):

```
switch (code)
{
    case 1:
    System.Console.WriteLine("one");
    break;
    case 2:
    return 20L;
    default:
    System.Console.WriteLine("other");
    break;
}
```

The case body and its terminator share `indent + 1`, which is the body indent the writer already used; the case label sits at the same column. Setting the body deeper is a formatting change the card does not ask for.

### Deviations from the card

1. **`PrintedSwitch_ParsesWithRoslyn` uses a compilation, not `ParseText`.** `CSharpSyntaxTree.ParseText` never reports CS0163 (it is a semantic diagnostic), so a parse-only assertion is vacuous and passes before the change — verified in the first RED run. The test compiles the printed switch against the shared-framework reference set (`TRUSTED_PLATFORM_ASSEMBLIES`) and asserts zero error diagnostics; the RED run reported CS0163 twice plus CS8070.
2. **The golden lives in a new directory, `EmitGolden/handwritten/Switch.cs.golden`.** No existing golden contained a `switch`, and every existing directory under `EmitGolden/` is asserted 1:1 against a sample domain's emit (`Emit_MatchesGolden`); adding a file to one of them would break that assertion. `CSharpLanguageTests.GoldenFiles()` globs recursively, so the new file is covered by all three golden tests.
3. **`--treenode-filter '/*/*/CSharpRoundTrip*/*'` runs zero tests in this tree.** There is no `CSharpRoundTrip` class yet (it arrives with IS2a); the named `CSharpLanguageTests` filter is the whole changed-area surface.
4. **`SwitchText` now delegates each case to `CaseText`.** The terminator is one appended line plus the pattern choice (`case` vs `case-break`), and `Terminates` recurses into a `Block` body so a block ending in `return` prints without a break.
