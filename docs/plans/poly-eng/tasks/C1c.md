# TASK C1c - Unbound adapter result shape; last `BindForSimulate` arm
Status: planned. Branch slice/c1c-unbound-adapter-result. Lane B. Review 1. Implement mill: Grok (review on OpenCode).

## Scope
V7 = a (decided): an unbound contract endpoint returns the same `DomainResult.Failure` on simulate and print. C1b (#127 `20fd11e0`) already deleted the throw arm and `AsVoidResultBody`. The last `BindForSimulate` rewrite is the adapter arm (`Poly/DomainModeling/Runtime/DomainEntityInstance.cs:966-971`): an `Invoke` of `{Contract}Adapters.{Endpoint}` becomes `return DomainResult.Failure("Contract endpoint '{contract}.{endpoint}' has no in-process adapter on simulate.")`. Print still throws (`BuildContractAdapterTypeDef` `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:448-482`, throw at `:459-464`). The card's `~949-953` is the method comment; the arm is `:966-971`. Method starts at `:952`. Call sites: `:778`, `:785`, `:791`, `BindModuleMethodBody` `:936`, `DomainEntityInstance.HostAbi.cs:309`.

Put the Failure in the printed adapter (return type `DomainResult`, body `return DomainResult.Failure(...)`). `PrependAdapterInvocation` (`Actions.cs:421-438`) today prepends a discarded `StripeAdapters.Charge(request)` (`:433-435`); after a Failure-returning adapter that would continue the action. Emit the existing Create pattern (`StoreBind.cs:154-161`, `Notify.cs:208-218`): assign the call, `if (!adapterResult.IsSuccess) return` it (void `DomainResult`) or `DomainResult<T>.Failure(ErrorMessage)` (typed). One message, owned by the adapter: `Contract endpoint '{name}.{endpoint}' has no in-process adapter.` Then delete `BindForSimulate` and `AdapterTypeName` / `ContractNameFromAdapter` / `TypeNameOf` (`:1021-1036`). `BindModuleMethodBody` (`:924-937`) still swaps name-only body `Parameter` nodes for the method's typed `Parameter`s (C1a, `EffectLoweringPass.cs:81`); keep that as a local recurse, not under the name `BindForSimulate`. Entry/exit/subscription call sites (`:778/:785/:791`, HostAbi `:309`) pass the body through.

## Files
- `Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs` (`BuildContractAdapterTypeDef`, `PrependAdapterInvocation`)
- `Poly/DomainModeling/Lowering/DomainProgramProjection.cs` (comment `:122-125`: adapter returns Failure)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.cs` (delete `BindForSimulate` and helpers; keep typed-Parameter swap in `BindModuleMethodBody`)
- `Poly/DomainModeling/Runtime/DomainEntityInstance.HostAbi.cs` (drop `BindForSimulate` at `:309`)
- `Poly.Tests/DomainModeling/Lowering/ContractAdapterFailClosedTests.cs` (print side: Failure, not NIE)
- `Poly.Tests/DomainModeling/Lowering/ParityTests.cs` + `Poly.Tests/TestHelpers/ParitySides.cs` / `ParityScenario.cs` (row; `Invoke` with args)
- `Poly.Tests/DomainModeling/Lowering/ContractBindingExportTests.cs` (`NotImplementedException` / `public static void Charge(`)
- `Poly.Tests/DomainModeling/Lowering/DslCompilerCompileOracleTests.cs` (`:210` NIE)
- `Poly.Tests/DomainModeling/Compile/PipelineTransformationTests.cs` (message still contains `no in-process adapter`)
- `Poly.Tests/DomainModeling/CrmDogfoodTests.cs` (Capture still fails; comment `:217`)
- `Poly.Tests/DomainModeling/Compile/C1aRootProgramParamsTests.cs` (comment `:18` still says BindForSimulate)
- `Poly.Tests/DomainModeling/Compile/EmitGolden/crm/Poly.Types.cs.golden` (`:52-59`); `crm/Opportunity.cs.golden` (`:456`)

## Shape matrix
| Input kind | Before | After | Test that proves it |
|---|---|---|---|
| Bound action, no in-process adapter (`Pay` → `Stripe.Charge`) | Simulate rewrites the adapter `Invoke` to `return Failure` (`:966-971`). Print throws `NotImplementedException` (`Actions.cs:459-464`). Messages differ. | Same tree. Adapter method returns `DomainResult.Failure` with one message. Action body returns that Failure when `!IsSuccess`. Simulate runs the printed call (no rewrite). | `ContractAdapterFailClosedTests.UnboundContractAdapter_FailsClosed_OnSimulateAndPrintedCsharp`; `ParityTests.Invoke_WhenUnboundContractEndpoint_FailsTheSameWay` |
| Bound action export (CRM `Capture`, Shop `Pay`) | `BillingAdapters.Charge(request);` then effects; adapter is `public static void` + throw | `adapterResult = BillingAdapters.Charge(request); if (!adapterResult.IsSuccess) return …`; adapter is `public static DomainResult` + `return Failure` | `ContractBindingExportTests.BoundAction_Export_InvokesAdapterThroughBinding`; golden `Opportunity.cs.golden` / `Poly.Types.cs.golden` |
| Two bindings, one endpoint | one `public static void Charge(` | one `public static DomainResult Charge(` | `ContractBindingExportTests.BoundAction_Export_TwoBindingsSameEndpoint_EmitsSingleAdapterMethod` |
| CRM Capture | simulate Failure mentioning `Billing.Charge`; export throws | both Failure, same message | `CrmDogfoodTests` Capture (`:210-219`) |
| Action params / `when all` (C1a) | `BindForSimulate` still walks to swap typed `Parameter`s | swap stays in `BindModuleMethodBody`; name `BindForSimulate` gone | `C1aRootProgramParamsTests` |
| Void constraint throw (C1b) | `ConstraintFailureException`; host catches only that | unchanged | `C1bConstraintFailureTests`; `OnEntryConstraintAgreeTests` |
| `BindForSimulate` | present | `git grep BindForSimulate -- Poly Poly.Tests` empty | grep; C1a comment updated |

## Done when
- Unbound endpoint: simulate and print return the same `DomainResult.Failure` (same message). Printed adapter no longer throws `NotImplementedException`.
- Bound action body does not continue after adapter Failure (the prepended call is checked, not discarded).
- `git grep BindForSimulate -- Poly Poly.Tests` is empty. Typed-Parameter swap remains in `BindModuleMethodBody` without that name.
- Goldens for `crm/Poly.Types.cs` and `crm/Opportunity.cs` match. Full suite green.
- C1a `when all` / action-params rows and C1b fail-loud IOE still pass.

## SHIP if / NOT SHIP if
SHIP if the unbound-endpoint row agrees on Failure (message and `success=false`) and `git grep BindForSimulate -- Poly Poly.Tests` is empty.

NOT SHIP if simulate and print still differ, if the adapter still throws `NotImplementedException`, if the action continues after a discarded adapter Failure, if `BindForSimulate` remains, or if C1a's typed-Parameter swap is dropped and `C1aRootProgramParamsTests` go red.

## Tests
```
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ContractAdapterFailClosedTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ParityTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/ContractBindingExportTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/DslCompilerCompileOracleTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/PipelineTransformationTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/CrmDogfoodTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/C1aRootProgramParamsTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/C1bConstraintFailureTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/OnEntryConstraintAgreeTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/EmitGoldenTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj -- --treenode-filter '/*/*/MinimalApiGeneratorTests/*'
dotnet run --project Poly.Tests/Poly.Tests.csproj
```

## Hand-edit
The adapter stub becomes a `return DomainResult.Failure`, the bound action gets the same `if (!IsSuccess) return` already used for Create, and `BindForSimulate` is deleted; Scot can open those three spots cold.

## Needs Scot
none

## Log
| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-09 | planner | grok/grok-4.6 | `20fd11e0` | planned | - |
