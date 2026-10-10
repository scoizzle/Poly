# TASK C3a - Peer passed as a real argument

Status: merged #145 `019a7238` 2026-10-10. Branch slice/c3a-peer-argument. Lane B. Review 2. Implement mill: Grok (review on OpenCode).

## Scope
The printed handler already takes the peer as a typed parameter, peer first then
`previousStage` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:526-536`; the card's
`481-489` is the Register/Notify methods, not the handler). The VM can now pass a second
argument (C1a merged `b1fddf7e`). Today `ExecuteSubscriptionEffects` rewrites peer reads to
Constants via `MaterializePeerInSyntax` (`DomainEntityInstance.HostAbi.cs:305-306`, `356-486`)
because the cached subscription body's peer parameter is untyped
(`DomainToCSharpExporter.cs:550`, `new Parameter(peerBinding)`); without a type, the runtime
analyzer cannot resolve `peer.Prop` and `EmitMember` throws "not resolved"
(`DirectVmAbiEmitter.Expressions.cs:356-402`). C3a types that parameter at the lowering site and
passes the peer in `SetArgs`, so both simulate and print share the operation AST. Keep the C3a
HostAbi hunk to `ExecuteSubscriptionEffects`/`ExecuteCachedSubscriptionTree` only: C6a (PR 144,
open, lane B) also edits `DomainEntityInstance.HostAbi.cs`.

## Files
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` - drop the `MaterializePeerInSyntax` call; build typed root parameters and args (`this`, `previousStage`, peer).
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` - line 550: type the peer body parameter `new Parameter(peerBinding, new NamedTypeReference(info.TargetEntity.Name))` (one line; the handler signature is unchanged).
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` - new peer-handler parity row.

## Shape matrix
| input kind | before | after | test that proves it |
|------------|--------|-------|---------------------|
| `when Rel Stage as peer { assign X to peer.Prop }` (simulate) | `MaterializePeerInSyntax` swallows `peer.Prop` into a `Constant`; VM never receives the peer | `peer` is a typed root parameter; `SetArgs(this, previousStage, peer)`; `peer.Prop` is a live member read | new `ParityTests.Invoke_WhenPeerHandlerReadsPeerProperty_Agrees` (simulate == print) |
| `when Rel Stage as peer { assign X to peer.Prop }` (runtime single-path) | peer value injected by tree rewrite | peer value is the transitioned instance passed as an argument | `P4SubscriptionQuantifierDslTests.WhenNoKeyword_Each_FiresPerTransitionWithPeer` |
| `when Rel Stage` (no binder, notify-only) | no peer arg; handler gets `this` only | unchanged | `ParityTests.Invoke_WhenTrackedPeerTransitions_SubscriberTransitionsToo` |
| handler body with `previousStage` (`when all`) | `previousStage` at root slot 1; any peer materialized | `previousStage` and peer are adjacent root args, in that order | `P4SubscriptionQuantifierDslTests.WhenAll_FiresOnlyWhenEveryLinkedLoanIsOverdue` |
| printed handler | typed peer param `(peer, previousStage)`, body param untyped | same signature; body param now typed at lowering | `DomainToCSharpExporterTests.Export_PeerDependentSubscription_DslGolden_HandlerParamNotifyAndPeerMember` |

## Done when
- `git grep -n MaterializePeerInSyntax` shows only its own definition and self-recursion: no caller outside the method.
- A peer handler that reads a peer property agrees in both simulate and print (the new ParityTests row).
- `P4SubscriptionQuantifierDslTests`, `SubscriptionAnalysisTests`, `MultiHopPathPrefixPolicyTests` pass.
- The printed handler parameter list is unchanged (peer first, then `previousStage`).

## SHIP if / NOT SHIP if
SHIP if `MaterializePeerInSyntax` has no callers outside itself and the peer parity row agrees.
NOT SHIP if the printed handler signature changed, or a peer property read is still replaced by a `Constant`.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/P4SubscriptionQuantifierDslTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SubscriptionAnalysisTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MultiHopPathPrefixPolicyTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
The peer parameter is typed where the body is lowered and the call site builds its args in one readable list, so Scot can follow a `peer.Prop` read from the exported handler through to `SetArgs` by hand.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | opencode-go/deepseek-v4.1-flash | 91012793 | planned | - |
| 2026-10-09 | implementer | grok/grok-4.6 | 8d3cd909 | pushed | tests 3487/3487; sweep: no MaterializePeerInSyntax callers; leftover rewrite comments listed |
| 2026-10-10 | planner | opencode-go/deepseek-v4.1-flash | `019a7238` | merged #145 | squash-merged to master; leftover rewrite comments carry to C3b |
