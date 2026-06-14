# Anti-Pattern 001: Duplicate Tree Walks

**Problem:** `TypeResolver` and `MemberResolver` both walk the entire AST independently calling the same `MethodInvocationSemanticResolver`/`ConstructorInvocationSemanticResolver`. The first pass resolves the type and discards the resolved member. The second pass re-resolves the same nodes to store member metadata. ~O(2n) work for ~O(n) results.

## Plan

1. **Add `SetResolvedMember` to `TypeResolver`.** When `TypeResolver.ResolveMethodInvocationType` calls `MethodInvocationSemanticResolver.ResolveMethod`, the returned `ITypeMethod` is already available. Store it via `context.SetResolvedMember(invoke, method)` at the same time as storing the resolved type. Same for constructor invocations.

2. **Remove the `MemberResolver` pass registration from all pipelines.** The `MemberResolver.Analyze` method walks the entire tree, but its work is now done inline in `TypeResolver`. Remove `UseMemberResolver()` from every pipeline.

3. **Remove `MemberResolutionPass.cs`.** The file and its extension methods become dead and can be deleted.

**Lines saved:** ~159 (the entire `MemberResolutionPass.cs`), minus ~15 lines added to `TypeResolver` = ~144 net.

**Risk:** Low. Both passes call the same resolvers. The only difference is TypeResolver currently drops the result after extracting the return type. The MemberResolutionMetadata type and its query methods (`GetResolvedMember`) stay unchanged — they just get populated by a different pass.

**Timeline:** 1-2 hours.

## Why This Wasn't Done Before

The original audit flagged this but the recommendation was "keep separate — distinct semantic concerns." That was the right call for code clarity in isolation. But the performance cost (a second full tree walk) is real and the merge is mechanically simple. The passes call the same resolvers for the same nodes. There's no semantic coupling beyond what already exists.
