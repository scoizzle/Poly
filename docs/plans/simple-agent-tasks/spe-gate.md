# SPE suite gate

**Status:** `[x]`  
**Prereq:** E3, L3, O3 complete  

## Checks

- [x] **G1** — `dotnet build` + full test suite green  
- [x] **G2** — Export peer-dependent `when … as name` does **not** throw; golden asserts handler param + notify `this`  
- [x] **G3** — Entity-level `when` fires under store notify without subscriber stage match  
- [x] **G4** — Owned/to-one path-prefix policy evaluates true/false with link; fail-closed without store documented or tested  
- [x] **G5** — Product guide §7 (when), export notes, owned/policy sections match code  
- [x] **G6** — No reintroduced dual semantic path without docs; follow-ups filed if residual  

## Verdict

`[x]` Suite complete — G1–G6 verified 2026-08-02.

### Evidence (2026-08-02)

| Check | Evidence |
|-------|----------|
| **G1** | Prior full run: `dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj` — 0 errors; `dotnet run --project Poly.Tests/Poly.Tests.csproj` — **1799/1799** succeeded. Static re-check of gate did **not** re-execute suite. |
| **G2** | `DomainToCSharpExporter` peer param + `ThisReference` notify; `DomainToCSharpExporterTests`: `Export_PeerDependentSubscription_HandlerHasPeerParameterAndNotifyPassesThis`, `…LowersPeerPathPrefixToParameterMember`, `…DslGolden_HandlerParamNotifyAndPeerMember`. |
| **G3** | `DomainInstanceStore.NotifyTransition` stage-then-entity; `EntityLevelSubscription_Fires_WhenSubscriberNotInStageWithWhen`; `EntityLevelAndStageSubscription_StageFirstThenEntityLevel`; `EntityLevelSubscription_PeerBinding_CopiesPeerProperty`; `SubscriptionAnalyzer` entity-level `ValidateSubscription`. |
| **G4** | `DomainEntityInstance` PreprocessQuantifiers `RelationshipNavigation` via `GetOutboundRelatedInstances` fail-closed; `McpSmokeTests.EvaluatePolicy_OwnedToOnePathPrefix_CreateLink_TrueAndFalse`; `EvaluatePolicy_ToOneRelationshipNav_*` true/false/`WithoutStore_Throws`/`Unlinked_Throws`. |
| **G5** | `Poly.Mcp/Docs/poly-dsl-guide.md` §7 placement + peer/export; owned/path-prefix dual-eval honesty + fail-closed + residual “not yet” list. |
| **G6** | Documented residual sibling path: bag-null `Rel exists` vs store-aware path-prefix/quantifiers (guide honesty). No undocumented dual product path reintroduced. Residuals listed in guide (store-aware exists, multi-hop owned, OwnedAccess IR-only). |

### Progress notes

- Product tasks E1–E3, L1–L3, O1–O3 all `[x]` before gate close.
- Gate close + suite docs: `spe-README`, parent `domain-surface-extensions-plan.md` Status Done, `docs/plans/README.md` SPE Complete.
- Re-verify (2026-08-02): implement success; verify **pass** (severity: **nit**). Static re-check G2–G6 against sources/tests/guide; G1 1799 green not re-executed in re-check.
