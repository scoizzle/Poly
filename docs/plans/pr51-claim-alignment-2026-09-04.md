# PR 51 claim alignment fact-check

**Date:** 2026-09-05  
**Status:** Proposal (not CURRENT)  
**SHA:** `0b6fcab93b833ed1ee77b55b0fb01bb3f961921c`  
**PR:** https://github.com/scoizzle/Poly/pull/51  
**Plan:** [`docs/plans/pipeline-transformation-2026-09-04.md`](pipeline-transformation-2026-09-04.md)

---

## Claim table

| # | Claim (PR body) | Verdict | Evidence (file:line) |
|---|-----------------|---------|----------------------|
| P1 | **One lower.** Create / create-in / unique always lower to Store jobs. C# `Stay.Create` / `CreateNav` are the host bind. Operation tree stays flattened name/value pairs. | **MATCH** | `EffectLoweringPass.cs:644-663,811-834,279-294` — create/create-in/unique lower to `Create`/`CreateIn`/`ProbeCreate`/`EnsureUnique` Store jobs. `DomainToCSharpExporter.StoreBind.cs:20-48,97-191` — C# bind via `BindCreate`/`BindCreateIn`. `DomainEntityInstance.HostAbi.cs:372-480` — runtime dispatch to `Store.Create`/`Store.CreateIn`. `EffectLoweringPass.cs:848-882` — flattened pair args. `LowerStageTransitions` zero hits in `*.cs`. |
| P2 | **Compile once.** `GetOrLower` populates named action / OnEntry trees. `ExecuteEffectList` looks them up — `LowerActionBody` not on the named-action invoke hot path. Subscriptions/transition batches still lower at execute time. EvaluatePolicy still lowers the guard per call. | **MATCH** | `RuntimeAnalysisCache.cs:67-186` — `GetOrLower` → `EnsureRuntimeOperations` → populates `Operations` dict via `LowerActionBody`. `DomainEntityInstance.cs:637-671` — `ExecuteEffectList` uses `TryGetOperation` with `cacheKey`; named-action invoke always hits cache (`InvokeAction` line 561). `HostAbi.cs:207,280` — subscriptions/transition batches call `ExecuteEffectList` without `cacheKey` → fall through to `LowerActionBody`. `DomainEntityInstance.cs:375-407` — `EvaluatePolicy` constructs fresh `DomainExpressionLoweringPass` per call. |
| P3 | **session.Lower.** Cached `DomainProgramProjection.ToSyntax`. `session.Emit` prints that module. | **MATCH** | `DomainSession.cs:137-141` — `Lower` delegates to `RuntimeAnalysisCache.GetOrLower`. `RuntimeAnalysisCache.cs:77` — `holder.Module ??= DomainProgramProjection.ToSyntax(domain, analysis)` (cached). `DomainSession.cs:147-178` — `Emit` calls `Lower`, feeds result to `CSharpGenerator`. `DomainProgramProjection.cs:21-25` — `ToSyntax` public API. |
| P4 | **Host artifacts.** `uses http` fail-closed if a `BehaviorAction` is missing from the module. | **MATCH** | `DomainEntityInstance.cs:471-476` — `InvokeActionInternal` throws `InvalidOperationException` if `DomainCatalogMetadata` action map is null. `DomainEntityInstance.cs:484-485` — unresolved action → `ReportUnresolvedAction`. `RuntimeTool.cs:599-730` — MCP tool surfaces the exception as `DomainToolResponse(Success: false)`. Note: the fail-closed check is in the runtime layer, not in MCP tool code specifically, but the functional guarantee is the same. |
| P5 | **One analysis door.** `DomainSession.Analyze` binds `RuntimeAnalysisCache`. | **MATCH** | `DomainSession.cs:116-121` — `Analyze` calls `RuntimeAnalysisCache.Bind(domain, this, analysis)`. `RuntimeAnalysisCache.cs:33-48` — `Bind` stores session in `Holder.Session` via `ConditionalWeakTable<Domain, Holder>`. `RuntimeAnalysisCache.cs:18-24` — `Holder` stores session, analysis, module, operations in one entry. |
| P6 | **Clocks in the tree.** `PreprocessRuntimeKeyword` is gone. Clocks lower to BCL members the VM executes. | **MATCH** | `grep PreprocessRuntimeKeyword *.cs` — zero hits. `DomainExpressionLoweringPass.cs:371-377` — `Now` → `Member("DateTime","UtcNow")`, `Today` → `Invoke(Member("DateOnly","FromDateTime"),...)`. `EffectLoweringPass.cs:932-965` — `LowerDefaultExpression` maps `Now`/`Today`/`Guid` to BCL members. `ClockLoweringTests.cs` — 5 tests prove lowering and runtime execution. |
| C | **Create defaults on probe.** Store `Create` / `ProbeCreate` fill `default(...)` before unique/required validation. | **MATCH** | `DomainInstanceStore.cs:175` — `FillCreateDefaults` before `ValidateCreateConstraints`. `DomainInstanceStore.cs:150-152` — `ProbeCreate` same ordering. `DomainEntityInstance.cs:138-159` — `FillCreateDefaults` fills missing slots from `DefaultValueConstraint`. `StoreBindCreateTests.cs:174-212` — two tests prove defaults fill before unique/required validation. |

---

## Gaps

**None found for the claimed scope.** All seven claims (P1–P6 + create-defaults-on-probe) are verified against shipped code at SHA `0b6fcab9`.

Known open items (not gaps in the PR's claims, but noted in the final-pass review):
- `EvaluatePolicy` guard lowering is not cached (review followup F3: `docs/agent/reviews/2026-09-04-pr51-pipeline-final-pass-followups.md`).
- Subscriptions and transition batches still lower at execute time (acknowledged in P2 claim and plan).
- `Conditional-create` still goes through `ExecuteStructured` (documented guard in `DomainEntityInstance.cs`).

These are out-of-scope for the P1–P6 claim set and documented as future work.

---

## Recommendation

**Merge-ready for the claimed scope.** The PR body's P1–P6 claims and create-defaults-on-probe claim are all accurate and backed by code, tests, and docs at SHA `0b6fcab9`. No overclaims, no missing pieces within the declared scope.
