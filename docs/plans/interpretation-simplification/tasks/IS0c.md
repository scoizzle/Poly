# TASK IS0c - `AnyChild` means descendants, with stated boundaries

Status: planned. Branch `slice/IS0c`. Lane B (`analysis`). Issue #258.
Plan: [IS-README](../IS-README.md).

## Why

`AnyChild` documents a descendant search and implements a one-level search (`Poly/Analysis/INodeAnalyzer.cs:57-69`). `ControlFlowAnalysisPass` carries a private copy with the same bug (`:500-509`). Two current consequences:

1. **Live:** `ContainsThrow` (`:494-497`) is used at `:452` to decide catch-clause elision. `try { if (c) throw ...; } catch { ... }` is reported as "no throw statement in try block" (CF0010) and the catch body is marked elidable. `MarkElidable` reports *information*, so analysis does not fail closed, and `CanElide` is read by the writer (`CSharpWriter.cs:310,443,484,488`) — so a reachable catch body is omitted from printed C#.
2. **Latent:** purity of loop conditions (`:655-656`) uses the same one-level walk.

`AggregateChildren` is direct-child aggregation by design and is not the defect. The defect is one helper whose name, doc, and implementation disagree — and a shadow that duplicates it.

## Scope

- Make the shared helper true to its name: descend until the predicate holds.
- State the traversal boundary in the doc comment and enforce it: `Lambda` bodies and type-declaration bodies are **barriers**. A `throw` inside a lambda is not a throw at the declaration site, so it must not make an enclosing `catch` reachable. A type declaration's body is a separate scope for the same reason.
- Keep the name `AnyChild`; it is the name both consumers read. Do not add an `AnyDirectChild` — nothing needs one, and `AggregateChildren` already covers direct-child aggregation.
- Delete the private shadow in `ControlFlowAnalysisPass` and call the shared helper. If the fused-walk comment at `:655` records a performance decision, keep the fused walk but implement it on top of the shared traversal rule (or record why the local one stays and make it descendant-true too).
- Add a recursion/cycle guard only if the tree can contain one; `Node` is a DAG of records, so a visited set is unnecessary today — say so in the comment instead of adding one.

## Files

- `Poly/Analysis/INodeAnalyzer.cs` — the helper and its doc.
- `Poly/Interpretation/Analysis/ControlFlow/ControlFlowAnalysisPass.cs` — `ContainsThrow`, the shadow, the loop-condition call site.
- `Poly.Tests/Interpretation/ControlFlowAnalysisTests.cs` (or the class that already owns CF0010 facts) — the tests below.
- `Poly.Tests/Interpretation/AnyChildTests.cs` — new, for the helper's contract and boundaries.

## Shape matrix

| Input | Before | After | Test that proves it |
|---|---|---|---|
| `try { if (c) throw ...; } catch { ... }` | CF0010; catch body elidable | no CF0010; catch body kept | `NestedThrow_InsideIf_KeepsCatchReachable` |
| `try { { { throw ...; } } } catch {}` | throw not found | found | `DeeperNestedThrow_IsFound` |
| `try { var f = () => throw ...; } catch {}` | found at one level? no | **not** found (lambda is a barrier) | `ThrowInsideLambda_IsNotATryScopeThrow` |
| loop condition that mutates inside a nested `if` | purity reads the wrong answer | correct | `LoopConditionPurity_SeesNestedMutation` |

## Done when

- One descendant helper, no private shadow.
- CF0010 does not fire for a throw nested under control flow inside the `try`.
- Printed C# keeps the catch body.
- Lambda and type-declaration boundaries are asserted by tests, not just described.
- Full suite green.

## SHIP if / NOT SHIP if

SHIP: the four shape rows pass and the helper's doc matches its walk.
NOT SHIP: the boundary is undocumented; the private shadow remains; a test asserts the old one-level behaviour; the change makes `AnyChild` walk into a lambda to keep an existing test green.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/AnyChildTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ControlFlowAnalysisTests/*'
```
Then the full suite.

## Hand-edit

The helper is one recursive walk with its boundary named as two types, and the shadow is gone — one place to read.

## Needs Scot

Recommended and proceeding unless Scot objects: lambdas and type declarations are barriers. The alternative — walk everything — would make a `throw` that only runs when a lambda is invoked mark the enclosing `catch` reachable, which reports a catch as reachable when it is not.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
