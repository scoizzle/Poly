# Follow-ups — PR 39 MCP export and policy guards — 2026-08-30

Review: [`2026-08-30-pr39-mcp-export-and-policy-guards.md`](./2026-08-30-pr39-mcp-export-and-policy-guards.md)

Owning stream: DomainModeling export/runtime create paths, MCP policy evaluate, named entity policies as predicates.

## Prior open items (disposition)

- No prior review file for `dogfood/mcp-export-and-policy-guards`.
- 2026-08-09 csharp-export “no test compiles the export” — **narrowed** by `Crm_Export_Compiles` / `University_Export_Compiles` (not TinyCompiler). Not closed for Contact-create-in-Account (F3).

## Merge blockers (bugs)

- [x] **F1 (bug)** — Align export create-in probes with runtime prevalidate on `ConditionalEffect`. `EffectLoweringPass.cs:688` walks then/else; `HostAbi.cs:305` does not. Test if/else create-in with an illegal then-branch initializer: runtime must succeed when the branch is untaken; export must not fail the whole action. Do: collect probes only from the unconditional set, or lower probes inside each branch.

- [x] **F2 (bug)** — Plain `create Type` must not throw after prior assigns while create-in returns `DomainResult.Failure`. `EffectLoweringPass.cs:561` still `ThrowStatement(InvalidOperationException)`; runtime prevalidates `CreateEntityInstance` at `HostAbi.cs:328`. Give `create Type` the same probe + Failure shape as create-in. Test assign + invalid `create Stay`.

- [x] **F3 (bug)** — Copy the `entity.Name == targetEntity.Name` rule from `Notify.cs:129-134` into `LowerCreateInProbe` (`EffectLoweringPass.cs:722`). Cross-type create-in with a self-rel slot currently passes `this` in the probe and `null` in the factory (CS1503). Test Contact-or-equivalent `create in account` export compile.

- [x] **F4 (bug)** — `FindAction` (`DomainAnalysis.cs:26`) is entity-first then first matching stage; `TryResolveAction` (`DomainSemanticLookupExtensions.cs:161`) is current-stage first. Constraint/effect analysis using `FindAction` does not match runtime dispatch for same-named stage actions. Resolve the same way, or fail closed when bodies differ and current stage is unknown. Test two stage Cancels plus entity-level `invoke Cancel`.

## Suggestions

- [x] **F5 (suggestion)** — Update `docs/CORE.md:157` and `DomainResult.cs:8-10`. Self/cross-entity wrap `IsSuccess` (`EffectLoweringPass.cs:362-378`); `ExecuteEffect` returns failed `DomainResult` (`DomainEntityInstance.cs:591-592`); `InvokeNamed` returns Failure (`DomainEntityInstance.InvokeNamed.cs:38`).

- [x] **F6 (suggestion)** — `docs/domainmodeling-capability-inventory.md:188` still marks bag-mode `evaluate_policy(age|properties=)` shipped. Bag mode is gone. Replace or mark removed.

- [x] **F7 (suggestion)** — `ComposeStagePolicies` concatenates entity policies (`DomainEffectiveSurface.cs:32-34`) while the type comment says stage-local only. Only caller passes `Array.Empty<Policy>()` (`CapabilityAnalyzer.cs:140`). Return `stage.Policies` only, or delete the helper.

- [ ] **F8 (suggestion)** — Unique is not in `ValidateConstraints` (`DomainEntityInstance.cs:147-184`) or export `Create`. Parking mutation-then-fail still holds for unique. Prevalidate unique against the store, or document it out of the Parking invariant. Test assign + duplicate unique create-in.

- [ ] **F9 (suggestion)** — `BindEntityTypedActionArgs` (`RuntimeTool.cs:62`) leaves a missing/wrong instance id as `string`. Fail closed at the tool. Resolve the action via `TryResolveAction`, not FirstOrDefault scan (`:41-49`).

- [ ] **F10 (suggestion)** — `EvolutionBuilder.AddEffectToAction` (`DomainEvolution.cs:289`) still constructs changes with no `StageName`. Thread optional stageName; fail closed on ambiguous same-name. Sibling of the DSL parser fix.

- [ ] **F11 (suggestion)** — `WrapInvokeResult` (`EffectLoweringPass.cs:371`) returns nested `DomainResult`; typed `-> Entity` actions need `ActionResultType.Failure(...)` (CS0029). Mirror create-in Failure construction.

- [ ] **F12 (suggestion)** — Default-expression `Today` uses `DateTime.Today` (`EffectLoweringPass.cs:779-783`); policy/VM `Today` uses `DateTime.UtcNow` (`DomainExpressionLoweringPass.cs:305-308`). One clock. Test `default(Today)` vs policy `Today`.

## Nits

- [ ] **F13 (nit)** — Move the extra `<summary>` at `HostAbi.cs:493` onto `TryLinkInverseCollection`.
- [ ] **F14 (nit)** — Rename `GetEffectivePolicies_EntityAndStagePolicies_Combined` (`DomainSemanticLookupFailClosedTests.cs:241`); body asserts `Adult` is not in the set.
- [ ] **F15 (nit)** — Add trailing newline to `DomainToCSharpExporter.Actions.cs`.

## Process follow-ups (fix the loop)

- [ ] **P1** — Protocol sibling-path check (§3.2a) must name **export create-in probe vs runtime PrevalidateUnconditionalCreates vs plain `create Type` throw** as a dual-path class. This PR grew that split.
- [ ] **P2** — Invariant-stating comments (`FindAction` “same dispatch as invoke”, CORE “do not wrap IsSuccess”, `ComposeStagePolicies` “stage-local only”) need a gate: comment vs every sibling, or the comment is a bug.
