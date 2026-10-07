# TASK C1b — Dedicated failure exception; delete `AsVoidResultBody`

Status: branch `cursor/c1b-constraint-exception-4ad1`, stacked on C1a (`cursor/c1a-root-program-params-4ad1`). Lane B. Review 2. Not V11 (printed output changes; Scot hand-merges).

## Scope
Void printed trees (constructor, stage entry/exit, subscription handler) throw `DomainFailureException` when a domain rule fails, because they cannot return a `DomainResult`. The simulator maps the name to the CLR `Poly.DomainModeling.Runtime.DomainFailureException` and lets it propagate exactly as printed C# does. The `throw InvalidOperationException` → `return Failure` arm of `BindForSimulate`, `AsVoidResultBody` and `ThrowIfEffectListFailed` are deleted. Host fail-loud throws stay plain `InvalidOperationException`.

## Files
`Lowering/EffectLoweringPass.cs` (`ReturnCallerFailure` throws in a void caller; `AssignConstraintFailure` and the create fail-closed twin fold into it), `Lowering/DomainToCSharpExporter.Actions.cs` + `DomainProgramProjection.cs` (scaffolding type), `Runtime/DomainFailureException.cs` (new), `Runtime/DomainResultTypeProvider.cs`, `Runtime/DomainEntityInstance.cs` / `.HostAbi.cs`, `Analysis/StructuralDomainAnalyzer.cs` (N4 reserved name), goldens, tests.

## Done when
Parity rows: constraint failure in a void body throws the same type and message on both sides; inside an action it is the same Failure on both sides; host fail-loud errors still escape as `InvalidOperationException`. `git grep AsVoidResultBody` is empty.

## SHIP / NOT SHIP
SHIP if a host fail-loud throw is still not swallowed and `git grep AsVoidResultBody` is empty. NOT SHIP if Execute catches `InvalidOperationException` broadly.

## Shape
| Context | Printed before | Printed after | Simulate before | Simulate after |
|---|---|---|---|---|
| Ctor / first-stage entry constraint | `throw InvalidOperationException` | `throw DomainFailureException` | rewrite to `return Failure`, rethrown as IOE | throws `DomainFailureException` |
| Void unique / invoke / create-in failure | `return DomainResult.Failure` in a void method (does not compile) | `throw DomainFailureException` | `return Failure`, rethrown as IOE | throws `DomainFailureException` |
| Action body | `return Failure` | unchanged | unchanged | unchanged |
| Unbound adapter, void body | `NotImplementedException` | unchanged (C1c) | `return Failure` + `AsVoidResultBody` | throws `DomainFailureException` |
| Unbound adapter, action body | `NotImplementedException` | unchanged (C1c) | `return Failure` | unchanged |
| Host fail-loud (loop guard) | `System.InvalidOperationException` | unchanged | IOE | unchanged |

## Log

| Date | Who | Mill | SHA | Verdict / event | Findings |
|------|-----|------|-----|-----------------|----------|
| 2026-10-07 | implementer | Cursor agent | (this push) | Implemented on top of C1a | full suite 3418/3418; goldens: scaffolding gains `DomainFailureException`, 7 constraint throws change type; `catalog.golden` unchanged |
