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
| 2026-10-22 | agent | Copilot/VS Code | — | RED: the checks cannot pass before the table exists | `dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/NodeFactsTotalityTests/*'` with `NodeFacts.All` emptied → 1 failed: `every concrete Node kind needs one fact: Add; And; …; WhileLoop` (85 kinds), 7 passed vacuously. Restored. |
| 2026-10-22 | agent | Copilot/VS Code | — | RED: an emitter arm deleted without a claim change | Deleted `PopCount pc => SpillToRing(EmitPopCountValue(pc, ctx), ctx),` from `AbiEmitter.CompileNodeInner` → 1 failed: `a Vm-claimed kind needs a corpus program the VM accepts: PopCount`. Reverted; suite green again. |
| 2026-10-22 | agent | Copilot/VS Code | 6584fa07 | GREEN: table + totality test shipped | 85 concrete kinds, 85 rows (`Vm` 62, `Writer` 74, `Reader` 75, `Mermaid` 29, 6 with a `RejectReason`). `NodeFactsTotalityTests` 8/8; `LanguageSurfaceTests` 2/2; `PrintedSubsetVmTests` 6/6; full suite 4319 passed, 0 failed; `Poly.Benchmarks` build 0 warnings. |
| 2026-10-22 | agent | Copilot/VS Code | 6584fa07 | Findings and deviations | (a) `PrintedSubsetVmTests` also read `Kinds`/`Surface`, so the blanket delete could not be green; it now reads `NodeFacts` and is otherwise unchanged — the card did not name this the way the user's brief did. (b) The corpus sweep proves totality, reader evidence (75 kinds) and the writer round-trip (84 writer-subject units), but not a `Vm` claim: a node lifted out of a golden reaches analysis without its surrounding locals, so it rejects for a reason that has nothing to do with its kind, and `Interpreter.Compile(CompilationUnitNode)` has no emitter arm at all (0/257 units compile that way). `Vm` claims therefore use the card's §Needs Scot fallback: a 33-program declared-sample registry, plus 4 added statement-level corpus cases (`BitwiseNot`, `DoWhileLoop`, `LabelDeclaration`, `MapTypeReference`) and writer subjects for kinds no golden exercises. The cross-checks in shape-matrix rows 2–5 all stand. (c) Pre-existing writer defect, not fixed (out of this slice's scope): a `ForLoop` increment prints as a declaration — `for (var i = 0; i < 3; i = i + 1)` → `for (var i = 0; i < 3; var i = i + 1) { }` — which Roslyn rejects and the reader cannot read back; `ForLoop` is therefore not `Writer`-claimed. (d) `AttributedNode`, `ClrTypeReference`, `TypeDefinitionReference`, `Comment` and `SuspendNode` print but do not re-read as themselves, so `Writer` means a faithful round-trip and these are `None` or `Vm`-only. (e) `AGENTS.md`'s lockstep clause named the deleted dictionary; updated to `NodeFacts` so the next agent is not sent to a deleted file. |
