# TASK C8-tc2 - Typed create in the compiled tree (2 of 3: StoreBind out, public navs, real unique, G12)
Status: planned. Branch slice/c8-tc2-storebind-out. Lane B. Review 2. Implement mill: Grok (review on OpenCode).
## Scope
Split of C8-tc (Scot 2026-10-10 19:58 CT). Findings K0 (rest), G9, G3+D1, G12. Depends on C8-tc1 (typed call sites already in the tree; nothing calls the bind methods any more); C8-vm (slice/c8-vm-plan) follows this slice.
Delete `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs` (317 lines on master `60089ef0`: always-Success `EnsureUnique` stub `:25-35`, dictionary job overloads `AddStoreJobOverloads` `:58-95`, `BindCreate` `:97`, `BindProbeCreate` `:124`, `BindCreateIn` `:160`, `BuildTargetCreateArgs` `:216` (C6b2's other copy), `ValueFromDictionary` `:279`) and the `AddStoreBindMethods` call (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs:878`; re-check the line after C8-tc1). The 48 goldens' 1296 `new Dictionary<string, object` constructions are these methods' printed bodies.
G9: `Create{Nav}` becomes public (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs:271` `AccessModifier: AccessModifier.Private` → `Public`), so hosts and C8a2 can call them. G3+D1: the unique stub becomes a real per-property check — emit `EnsureUnique{Prop}(value)` methods backed by a static `Dictionary<object, T>` index (`UniqueCheckMethodName`/`UniqueIndexFieldName` helpers; `WrapConstrainedAssign` `Poly/DomainModeling/Lowering/EffectLoweringPass.cs:310-313` and `BuildCreateUniqueChecks` `Notify.cs:482-495` call the typed name; interpreter routes the name suffix in `DomainEntityInstance.InvokeNamed.cs` to the existing `DomainInstanceStore.EnsureUnique` real check `Poly/DomainModeling/Runtime/DomainInstanceStore.cs:63`). G12: `DomainResult<T>.Value` becomes `T?` and `Failure` stores null, not `default!` (`Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:812` Value property, `:868` `new NullForgiving(new Default())`; golden `orders/Poly.Types.cs.golden:39-42`).
## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.StoreBind.cs` (delete)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.cs` (drop the `AddStoreBindMethods` call; add per-property unique-check method emission)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Notify.cs` (`Create{Nav}` public; `UniqueCheckMethodName`/`UniqueIndexFieldName`; typed unique call in `BuildCreateUniqueChecks`)
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (G12 `DomainResult<T>`)
- `Poly/DomainModeling/Lowering/EffectLoweringPass.cs` (`WrapConstrainedAssign` unique call name)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.InvokeNamed.cs`, `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (route `EnsureUnique{Prop}`)
- `Poly.Tests/DomainModeling/Lowering/StoreBindUniqueTests.cs`, `Poly.Tests/DomainModeling/Lowering/ConstraintAssignLoweringTests.cs`, `Poly.Tests/DomainModeling/Lowering/DomainToCSharpExporterTests.cs`, `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` (duplicate-unique row becomes agreement, not known-gap), `Poly.Tests/DomainModeling/Compile/EveryConceptHasTreeTests.cs` (`EnsureUnique` → `EnsureUnique{Prop}`), `Poly.Tests/DomainModeling/Compile/EmitGoldenTests.cs`
- Goldens: regenerate `Poly.Tests/DomainModeling/Compile/EmitGolden/**` (StoreBind blocks and dictionary constructions gone; `EnsureUnique{Prop}` names; `T? Value`; none hand-edited)
## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Any exported entity (K0 rest) | printed `Create`/`CreateIn`/`ProbeCreate` dictionary overloads; 1296 `new Dictionary<string, object` in 48 goldens | no StoreBind methods; zero dictionary constructions in goldens | `git grep -n 'new Dictionary<string, object' Poly.Tests/DomainModeling/Compile/EmitGolden` empty; `DomainToCSharpExporterTests` |
| `create in orders { … }` from a host (G9) | `private DomainResult<Order> CreateOrders(…)` (`orders/Customer.cs.golden:26`) | `public DomainResult<Order> CreateOrders(…)` | `DomainToCSharpExporterTests`; golden |
| Unique assign / unique on create (G3+D1) | `created.EnsureUnique("Plate", plate)` → `=> DomainResult.Success()` stub (`orders/Customer.cs.golden:110`) | `created.EnsureUniquePlate(plate)` real index check, same failure message both modes | `StoreBindUniqueTests`; `ParityTests.Create_WhenDuplicateUnique_FailsOnBothSides` (replaces the known-gap row) |
| Duplicate unique create | simulate throws / printed succeeds (known gap) | both fail with the unique message | `ParityTests` row above |
| `DomainResult<T>.Failure` (G12) | `Value` is `T`, `Failure` is `default!` (`orders/Poly.Types.cs.golden:39`) | `T? Value`, `Failure` passes null | `DomainToCSharpExporterTests`; golden |
| Unique constraint concept | `EveryConceptHasTreeTests` looks for method `EnsureUnique` | looks for `EnsureUnique{Prop}` | `EveryConceptHasTreeTests` |
## Done when
- `git grep -n 'new Dictionary<string, object' Poly.Tests/DomainModeling/Compile/EmitGolden` and `git grep -n 'BindCreate\|BindCreateIn\|BindProbeCreate\|\.EnsureUnique("' Poly.Tests/DomainModeling/Compile/EmitGolden` are empty; `DomainToCSharpExporter.StoreBind.cs` is gone and `AddStoreBindMethods` with it.
- `Create{Nav}` methods are public; the printed unique call is `EnsureUnique{Prop}` backed by a real check, and duplicate unique fails identically in simulate and print (the old known-gap parity row is replaced by an agreement row).
- G12: failed `DomainResult<T>.Value` is not `default!`; the golden shows `T? Value`. Full suite green.
## SHIP if / NOT SHIP if
SHIP if the greps above are empty, duplicate unique fails on both sides with the same message, `Create{Nav}` is public, the G12 golden shows `T?`, and the suite passes with regenerated goldens.

NOT SHIP if any StoreBind method or dictionary construction survives, if unique is still a Success stub or only fixed on one side, if `Create{Nav}` is still private, if failed `.Value` still returns `default!`, or if a golden was hand-edited.
## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/StoreBindUniqueTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ConstraintAssignLoweringTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DomainToCSharpExporterTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EveryConceptHasTreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```
## Hand-edit
One whole file deletes, one access modifier flips, the stub body becomes a five-line index check Scot can pattern-match, and `T?`/null is a two-token change in the `DomainResult<T>` printer.
## Needs Scot
none
## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-10 | planner | opencode/opencode-go/mimo-v2.6-flash | 60089ef0 | planned | K0(rest) G9 G3+D1 G12; split of C8-tc per Scot 19:58 CT |
