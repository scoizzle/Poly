# TASK C3b - Delete MaterializePeerInSyntax

Status: merged. Branch slice/c3b-plan. Lane B. Review 1. Implement mill: Grok (review on OpenCode).

## Scope
C3a (merged `019a7238`, PR #145) replaced the peer rewrite with a typed `SetArgs` slot, so
`MaterializePeerInSyntax` now has only its own definition and self-recursion
(`Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs:353-487`; the card's `HostAbi.cs:294-423`
is stale — the file was 895 lines when the card was written, 628 now).
The card's "twin tree-walker" — the effect-level `BindPeerInEffect` / `BindPeerInExpression` /
`PeerBindingRewrite` / `EvaluateExprOnPeer` / `RejectPeerAssignTarget` — was already deleted in C0
(`ab0772cd`); only a stale comment naming `BindPeerInEffect` remains at `HostAbi.cs:292`.
The live peer path is `ExecuteSubscriptionEffects` (`HostAbi.cs:305-318`): it adds a typed peer
`Parameter` and `setArgs.Add(peerInstance)`. Delete the dead method and the two stale references;
no behavior moves.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` — delete `MaterializePeerInSyntax`
  (doc comment `:353-356`, method `:357-487`); fix the comment at `:292` that names the deleted
  `BindPeerInEffect`.
- `Poly/DomainModeling/Lowering/LoweringContext.cs` — drop the now-false sentence at `:22`
  ("VM peer binding remains a separate pre-lower rewrite"); the rest of the `Parameters` doc stays.

## Shape matrix
| input kind | before | after | test that proves it |
|------------|--------|-------|---------------------|
| `git grep MaterializePeerInSyntax` | definition + ~30 self-recursive calls (`HostAbi.cs:357-487`), no external caller | empty | the grep in Done when |
| stale comments | `HostAbi.cs:292` names deleted `BindPeerInEffect`; `LoweringContext.cs:22` claims a pre-lower peer rewrite | both describe the typed `SetArgs` peer slot | `git grep -n BindPeerInEffect` and `git grep -n "pre-lower rewrite"` empty |
| `when Rel Stage as peer { … peer.Prop … }` (simulate), read the peer | C3a already passes `peerInstance` as a typed arg; the rewrite is dead code | unchanged behavior | `ParityTests.Invoke_WhenPeerHandlerReadsPeerProperty_Agrees`, `P4SubscriptionQuantifierDslTests` |
| `when Rel Stage` notify-only, no binder | no peer slot; method unused | unchanged | `P4SubscriptionQuantifierDslTests`, `SubscriptionAnalysisTests` |
| full suite on `019a7238` | green | unchanged | full `dotnet run --project Poly.Tests/Poly.Tests.csproj` |

## Done when
- `git grep -n MaterializePeerInSyntax` is empty (product, tests, docs, comments).
- The stale references are gone: `git grep -n BindPeerInEffect -- Poly` and
  `git grep -n "pre-lower rewrite" -- Poly` are empty; `LoweringContext.cs` no longer claims the
  peer binding is a rewrite.
- The full suite is unchanged (green, same count as master `019a7238`); no test edited.

## SHIP if / NOT SHIP if
SHIP if pure deletion: grep empty, no test changed, suite unchanged, and the live peer path
(`ExecuteSubscriptionEffects` typed `Parameter` + `setArgs.Add(peerInstance)`) is untouched.

NOT SHIP if any behavior code moved, if `ContainsPreviousStageParameter` or the typed peer
`Parameter` in `ExecuteSubscriptionEffects` changed, or if a test was edited to fit.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/P4SubscriptionQuantifierDslTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SubscriptionAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MultiHopPathPrefixPolicyTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainEntityInstanceTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
One method and two stale comment lines go away; the live peer path is the four adjacent lines in
`ExecuteSubscriptionEffects`, so Scot can read the whole slice cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode-go/deepseek-v4.1-flash | 019a7238 | planned | - |
| 2026-10-10 | implementer | grok/grok-4.6 | 181519d4 | pushed | tests 3501/3501; sweep: walker gone; leftover peer-bag comment listed |
| 2026-10-10 | planner | opencode-go/deepseek-v4.1-flash | c3b9d9d3 | merged | merged as #148 |
