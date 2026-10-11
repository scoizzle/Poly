# TASK C6c - Unlink cannot drop below a required minimum
Status: R1/R2 fixed after review 2 (NOT SHIP). Branch slice/c6c-plan (rebased on master). Lane B. Implement mill: Grok (review on OpenCode; R1/R2 fix on OpenCode).
## Scope
C6b1 (merged #151) enforces the link maximum in two places: the shared lowered `create in` tree (`Poly/DomainModeling/Lowering/EffectLoweringPass.cs:909-931`) and `DomainInstanceStore.Link` (`Poly/DomainModeling/Runtime/DomainInstanceStore.cs:355-380`; contract lookup `:364-376`), message `Relationship.LinkViolationMessage` (`Poly/DomainModeling/Ontology/Relationship.cs:34-35`).
`RelationshipCardinality` is max-only (no minimum; `Relationship.cs:6-11`) and navigations cannot be `required` (`Poly.Mcp/Docs/poly-dsl-guide.md:94-100`). The one modelled minimum is ownership: an `owned` target (`SourceOwnsTarget`, `Relationship.cs:26`; `RelationshipContract.SourceOwnsTarget` `DomainModelMetadata.cs:88`) must keep its owner. Delete semantics are out of scope (V9), so unlink is where that minimum is enforced.
Unlink is store/tool-only: the only unlink is `DomainInstanceStore.Unlink` (`:385-397`), reached by MCP `unlink_instances` (`Poly.Mcp/Tools/RuntimeTool.cs:378`, calls `:457`). There is no Link/Unlink Effect IR (`poly-dsl-guide.md:984`; `first-principles.md:366`), so C6c adds the mirror of C6b1 to `Unlink` in the store, not a lowered action. Plan against C8-tc's typed create (no `StoreBind.cs`); the store and `Relationship` are untouched by C8-tc.
## Files
- `Poly/DomainModeling/Ontology/Relationship.cs` (`UnlinkViolationMessage`, after `LinkViolationMessage` `:34-35`; no new type)
- `Poly/DomainModeling/Runtime/DomainInstanceStore.cs` (`Unlink`: one shared `ResolveRelationshipContract` (R2) feeds `Link` and `Unlink`; `Unlink` throws `Relationship.UnlinkViolationMessage` before any mutation when `contract.SourceOwnsTarget` and the target's inbound count is 1, then calls the new private `Detach`. `Remove` calls `Detach` directly, so delete and create-in rollback are exempt from the minimum (R1))
- `Poly.Tests/DomainModeling/Analysis/DomainInstanceStoreFailClosedTests.cs` (new `Unlink_OwnedTarget_LastOwner_Throws`, `Unlink_OwnedCollection_ChildKeepsOwner`, `Unlink_NonOwned_Singular_Succeeds`, `Unlink_NoDomain_Allows`, `Unlink_UnknownRelationship_Allows`, plus R1 regression `Remove_OwnedTarget_LastOwner_DropsInstanceAndLink`, `Remove_OwnedSource_LastOwner_DropsChildLink`; mirror the `Link_*` helpers `:242-335`)
- `Poly.Tests/Mcp/McpSmokeTests.cs` (new owned-rel unlink row; existing `UnlinkInstances_LinkThenUnlink_Succeeds` `:3494-3518` and `SurfaceExtensionCompositionTests.Unlink_StopsPeerWhen_AndExistsBecomesFalse` `:74-125` use non-owned `Tracks`/`bs` and stay green)
- No golden change. No exporter/lowering change: unlink is not a lowered effect, so there is nothing for `EmitGoldenTests` to see.
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| `store.Unlink` last owner of an `owned` target | edge removed; target orphaned | throws `InvalidOperationException(Relationship.UnlinkViolationMessage(rel))`; edge stays | new `DomainInstanceStoreFailClosedTests.Unlink_OwnedTarget_LastOwner_Throws` (red on master: orphan) |
| MCP `unlink_instances` on an `owned` relationship | success | Failure with the same message; edge stays | new `McpSmokeTests` row |
| `store.Unlink` on a non-owned OneToOne (`Tracks: Order`) | removed | unchanged (min 0) | existing `SurfaceExtensionCompositionTests.Unlink_StopsPeerWhen_AndExistsBecomesFalse` `:74-125` |
| `store.Unlink` on a non-owned collection (`A.bs: many B`) | removed | unchanged | existing `McpSmokeTests.UnlinkInstances_LinkThenUnlink_Succeeds` `:3494-3518` |
| Unlink when not linked, `Domain` null, or no matching contract | silent no-op / removed | unchanged | existing `UnlinkInstances_NoExistingLink_Fails` `:3521-3538`; new `Unlink_NoDomain_Allows`, `Unlink_UnknownRelationship_Allows` |
| `store.Remove` of an `owned` target or its owner (delete / create-in rollback) | edge removed | unchanged: `Remove` detaches without the minimum check | new `Remove_OwnedTarget_LastOwner_DropsInstanceAndLink`, `Remove_OwnedSource_LastOwner_DropsChildLink` (both red with R1 reintroduced) |
## Done when
- `Relationship.UnlinkViolationMessage` is the single message source; `DomainInstanceStore.Unlink` throws it before mutating the subscriber registry or `_links` when the resolved contract has `SourceOwnsTarget` and the target has exactly one inbound link on that relationship.
- `Unlink` does not throw for non-owned relationships, when the link is absent, when `source.Domain` is null, or when no `RelationshipContract` matches.
- MCP `unlink_instances` surfaces the same message as Failure (it already catches exceptions, `RuntimeTool.cs:461-467`) and the edge is not removed.
- The owned-target store row is red on master (master orphans the target) and green after; full suite green; no golden changed.
- `Remove` (delete / create-in rollback) is exempt: removing an owned target, or its owner, drops the link and does not throw (R1).
## SHIP if / NOT SHIP if
SHIP if below-minimum unlink of an owned target fails from the store (the path MCP uses) with the same message, the edge is not removed, non-owned relationships are unchanged, and the existing missing-link fail-closed behavior is untouched.

NOT SHIP if the check lives only in MCP `unlink_instances`, if the guard runs after the subscriber registry or `_links` mutate, if non-owned relationships gain a minimum, if a golden was hand-edited, or if `Remove` (delete / create-in rollback) throws on an owned target's last link (R1).
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainInstanceStoreFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/McpSmokeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/SurfaceExtensionCompositionTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One static message helper, one shared `ResolveRelationshipContract`, and one guard branch in `Unlink`; `Remove` routes through the private `Detach`. Scot can open `Relationship.UnlinkViolationMessage`, `DomainInstanceStore.Unlink` and `DomainInstanceStore.Detach` cold.
## Needs Scot
Answered as Decision 21 (Scot 2026-10-10 18:03 CT): an owned target must keep its owner (minimum 1); non-owned links stay minimum 0.
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/deepseek-v4.1-flash | 60089ef0 | planned | - |
| 2026-10-10 | implementer | grok/grok-4.6 | d19b6c9e | pushed | tests 3531/3531; sweep: owned min-1 in Unlink before mutate |
| 2026-10-10 | reviewer | (different model) | 5cd51cd4 | NOT SHIP | R1 major: `Remove` calls `Unlink`, so create-in rollback throws and leaves the owned child linked; R2 nit: `Link`/`Unlink` duplicate the contract lookup |
| 2026-10-10 | fixer | opencode/opencode-go/deepseek-v4.1-flash | 537fb70b | pushed | R1: `Remove` uses private `Detach`; R2: one `ResolveRelationshipContract`; 2 new `Remove_*` tests (red with R1 reintroduced); rebased on master; full suite 3537/3537 |
