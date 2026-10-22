# TASK IS1 - one producer-capability table, one totality test

Status: planned. Branch `slice/IS1`. Lane A (`csharp`). Depends on IS0a, IS0b, IS0e.
Plan: [IS-README](../IS-README.md). Audit: [interpretation-simplification.md](../../../analysis/interpretation-simplification.md) §1.

## Why

Five independent type switches answer "what does this node mean", and none is exhaustive at compile time:

| Producer | Shape | Unknown kind |
|---|---|---|
| `AbiEmitter.CompileNodeInner` | switch expression | throws (`AbiEmitter.cs:296`) — correct |
| `LinqExpressionGenerator` | switch expression | throws, but unsound (deleted by IS2c) |
| `CSharpWriter` statements `:172-268`, expressions `:833-974` | two switch statements | printed the type name until IS0a |
| `CSharpReader` | string switch after table match | throws |
| `MermaidAstGenerator` `:236-417`, `:436-536` | two switch statements | empty / type name — silent |

The only cross-check is `LanguageSurfaceTests.Kinds` (`:25-100`); its test (`:114`) asserts each concrete `Node` has *a dictionary entry* and nothing about whether any producer honours it. A producer can lose an arm and the suite stays green.

## Scope

**The table.** New `Poly/Interpretation/NodeFacts.cs`:

```csharp
public sealed record NodeFact(string Label, Producers Producers, string? RejectReason = null);
[Flags] public enum Producers { None = 0, Vm = 1, Writer = 2, Reader = 4, Mermaid = 8 }
public static class NodeFacts { public static IReadOnlyDictionary<Type, NodeFact> All { get; } }
```

Keyed on `Type` for every concrete `Node`. `RejectReason` non-null means the VM rejects that kind deliberately and the text is the message the rejection must carry.

**Deviations from the audit's sketch, deliberate:**

- **No `Surface` enum.** It is derivable: `RejectReason != null` → compile-reject; `Vm` claimed → executable; no `Vm` claim → analysis-only. Deriving it deletes the three-value taxonomy instead of moving it.
- **No `Shape` field.** No consumer exists (tenet 7). The writer's shape lives in its grammar patterns and the VM's in its emitter; a third copy would be drift, not a source.
- **No `Children` delegate.** `Node.Children` already answers it, and `AggregateChildren` is direct-child by design (see the audit's research limits). Mermaid's defect is missing arms, not missing children; IS1b fixes it by reading `Node.Children`.
- **No `Linq` claim.** That producer is scheduled for deletion (IS2c) and is not a meaning authority. The table must not certify it.

**The totality test.** New `Poly.Tests/Interpretation/NodeFactsTotalityTests.cs`, evidence-driven — it does not hand-build 91 sample nodes:

1. **Inventory.** Every concrete `Node` type has an entry, and every entry names a real type (replaces `EveryConcreteNodeType_IsInventoried`).
2. **Corpus sweep.** Parse the existing corpus — the C# program strings in `Poly.Tests/Integration/**` and `Poly.Tests/Interpretation/**`, plus `Poly.Tests/DomainModeling/Compile/EmitGolden/**` — and collect every node type it produces. For each encountered kind:
   - `Vm` claimed → `Interpreter.Compile` does not reject it;
   - `Writer` claimed → `Write` succeeds and `Read(Write(x))` is the same kind;
   - `Reader` claimed → `Write(Read(x))` is the same kind;
   - `Mermaid` claimed → it renders and every child label appears;
   - `Producers == None` → the VM rejects it and the writer rejects it (this is what catches an arm that was added but never declared).
3. **Rejections.** Every kind with a `RejectReason` is exercised by a sample from the existing `CompileRejectKinds_FailLoud` list and throws with that message.
4. **Claims are reachable.** Every claimed kind appears in the corpus. A claim with no corpus entry is either a missing corpus case or a false claim; the test names it and the fix is one of those two, never a new abstraction.

Delete `LanguageSurfaceTests.Kinds`, `Surface`, and the three tests that read them; keep the two behavioural tests (`CommentAsExpression…`, `ClrTypeReference_AsValue…`) moved onto the table.

**Mermaid is not rewired here** — it keeps its switches until IS1b. The table gives IS1b its target.

## Files

- New: `Poly/Interpretation/NodeFacts.cs`
- New: `Poly.Tests/Interpretation/NodeFactsTotalityTests.cs`
- New: `Poly.Tests/Interpretation/NodeFactCorpus.cs` — the corpus loader (parses the program strings and goldens; no production code)
- `Poly.Tests/Interpretation/LanguageSurfaceTests.cs` — delete the dictionary and its three tests
- `Poly.Tests/TestHelpers/*` — only if a corpus source already lives there and must be exposed
- `Poly/Interpretation/README.md` — one paragraph naming the table

## Shape matrix

| Situation | Before | After | Test that proves it |
|---|---|---|---|
| a new concrete `Node` with no table entry | green | red, naming the type | `EveryConcreteNodeType_HasAFact` |
| a `Vm`-claimed kind whose emitter arm is deleted | per-kind test may not exist | red | `CorpusKind_ClaimedByVm_Compiles` |
| a writer arm added for a kind the table calls `None` | green | red | `UnclaimedKind_IsRejectedEverywhere` |
| a claimed kind absent from the corpus | green | red, naming the kind | `EveryClaim_HasCorpusEvidence` |
| a `RejectReason` whose message drifts from the emitter | green | red | `RejectReason_MatchesTheThrownMessage` |

## Done when

- `LanguageSurfaceTests.Kinds` is gone and nothing replaces it as a hand-kept list.
- The four tests above pass, and each fails when its situation is introduced deliberately (record one such red run in the Log).
- No production file outside `Poly/Interpretation/NodeFacts.cs` and the README changed.
- Full suite green; build 0 warnings.

## SHIP if / NOT SHIP if

SHIP: the table is data, the test is evidence-driven, and the suite goes red when an arm is deleted without a claim change.
NOT SHIP: a `NodeKind` enum appears; the table duplicates grammar or emitter shape; the test asserts only key existence; Mermaid was rewired (that is IS1b); `Linq` is claimed.

## Tests

```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/LanguageSurfaceTests/*'
```
Then the full suite.

## Hand-edit

One contiguous table lists every kind and which producers claim it; a missing arm is a failing test that names the kind, not a hunt through five switches.

## Needs Scot

Recommended and proceeding unless Scot objects: the table lives at `Poly/Interpretation/NodeFacts.cs`. `Poly/Ast` must not learn producer identities, and `Poly.Tests` cannot be a production source. If the corpus sweep proves impractical in implementation, the fallback is a per-kind sample registry in tests — but the cross-checks in rows 3–5 above stay, because they are what makes the table a check rather than a comment.

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
