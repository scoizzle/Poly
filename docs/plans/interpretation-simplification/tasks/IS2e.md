# TASK IS2e - a dual-dispatch proof that does not need a deleted engine

Status: planned. Branch `slice/IS2e`. Lane A (`csharp`). Depends on IS2c. Created by IS2c.
Plan: [IS-README](../IS-README.md). Claim: [`customer-trust-proof-map.md`](../../reference/customer-trust-proof-map.md) §3.1.

## Why

`Poly.Grammar`'s claim — the table's structure dispatch and the readers' handlers agree — was never proved inside the shipped language. Its only instrument was `Poly.Tests/Integration/C99ParserInterpreterTests.cs`, a C99 subset parser whose `DualRun_*` cases drove both dispatch paths and compared results.

IS2c deleted that harness with the second interpreter. It could not be rescued: its parser models locals as assignable `Parameter` nodes, which the VM rejects by design (`assignment destination must be a Variable, Member, or IndexAccess, got Parameter`), and its execution tests were green only because the deleted LINQ oracle was *more permissive* than the VM. So the harness was never evidence about Poly — and §3.1 now reads **Yellow, evidence withdrawn**.

Deleting an instrument is allowed; leaving the claim it supported looking proved is not. Either the claim gets an instrument built on the shipped language, or it is retired.

## Scope

A dual-run whose substrate is shipped:

- Drive the **same source** through the two dispatch paths the shipped grammar exposes (for the C# surface, `CSharpGrammar.Table` through `Matcher<TToken, TTokenKind>` versus the reader's handler path; for the DSL, `Grammar` through `Matcher` versus `DslExpressionParser`'s RD handlers). Compare the resulting **AST**, node for node.
- Where the program is executable, run both ASTs on the VM and compare the **result** — the same expectation the C# reader/writer corpus already holds itself to.
- Where the program is illegal, both paths fail closed with the **same message**.
- Keep it small: a handful of representative programs per grammar (an expression, a block, a conditional), not 26 cases and not a new language.

Do not add a parser, a language, or a second interpreter to create the duality. If a shipped grammar has only one dispatch path, say so — that is a finding, not something to fix here.

## Files

- `Poly.Tests/Grammar/DualDispatchTests.cs` (new) — the harness and the cases.
- `Poly.Tests/Grammar/GrammarLanguageTests.cs` — only if a shared helper belongs there.
- `docs/plans/reference/customer-trust-proof-map.md` — the §3.1 row returns to Green with the pointer, or is reclassified with the reason.

## Shape matrix

| Case | Before | After | Test that proves it |
|---|---|---|---|
| same source, two dispatch paths | no in-tree instrument | identical AST | `SameSource_TwoDispatchPaths_ProducesTheSameAst` |
| executable program | not compared | identical VM result | `ExecutableProgram_TwoDispatchPaths_ProduceTheSameValue` |
| illegal program | not compared | same fail-closed message from both paths | `IllegalProgram_BothPaths_RejectWithTheSameMessage` |
| the §3.1 claim | Yellow, evidence withdrawn | Green with a shipped instrument, or retired with a recorded reason | the map row itself |

## Done when

- The §3.1 row "Grammar structure dispatch matches RD handlers" is **Green** with a pointer to a harness that uses only shipped code, or it is honestly reclassified and the card records who decided that.
- The harness has no dependency on a non-shipped language, no C99, and no deleted interpreter.
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: a shipped grammar's two dispatch paths agree over a small corpus, and the claim's row names the test that proves it.
NOT SHIP: the instrument is another experimental language; the comparison asserts that both paths *ran* rather than that they produced the same AST; the row is marked Green on a harness that tests something else.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DualDispatchTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/GrammarLanguageTests/*'
```
Then the full suite.

## Hand-edit

One claim, one instrument, built on the grammar the product ships.

## Needs Scot

Worth answering before the harness is written: **is this claim worth re-proving at all?** The C99 dual-run was preflight evidence for a cutover (`GI-7`) that has not happened. If the shipped grammar now has one dispatch path and the product has no consumer that depends on the duality, the claim is a candidate for retirement under `AGENTS.md` tenet 7 — guardrails only with real consumers — and the right outcome is a reclassified row, not a new harness. The card proceeds on the assumption that the claim is kept until Scot retires it.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
