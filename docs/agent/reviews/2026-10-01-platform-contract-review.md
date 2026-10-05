# Platform contract review — 2026-10-01

- **Target**: whole tree at `9db8868` (`master`, clean, even with `origin/master`)
- **Mode**: standard
- **Issue counts**: 4 bugs, 3 suggestions, 0 nits
- **Verdict**: not closed — do not treat the frozen contract as done until F1–F4 are closed
- **Process notes**: the same invariant is implemented twice and the siblings disagree (extension ids; policy identity; dirty analysis). Comments and `docs/plans/p3b-followups-2026-09-27.md` still describe execute-time lower sites that are dead or literal-only. Tests were not re-run; findings are from current source.

## Summary

Session compile on the product doors (DSL compiler, MCP `apply_dsl`, `DomainEvolution` when it builds the session itself) does fail closed on unknown `uses` and on analysis errors. The lowered module is not the whole domain meaning: stage policies are enforced only by the `DomainEntityInstance` invoke prelude, and policy bodies are cached by entity plus name so a later policy with the same name never gets its own tree. Two other doors accept or delete extension ids that `DomainSession.ForExtensions` would reject. `PIPELINE-STATUS.md` still presents lowering holism as future work while the September session-compile slices are already on `master` with those residuals open.

## Issues

### Issue 1 -- Severity: bug
- File: Poly/DomainModeling/Lowering/DomainToCSharpExporter.Actions.cs:238
- Description: Stage policies are legal domain facts (`Stage.Policies`, `EvolutionBuilder.AddPolicyToStage`) and `InvokeActionInternal` evaluates every current-stage policy before effects (`DomainEntityInstance.cs:558`). The operation module does not. `BuildActionBodyWithGuards` emits require gates only for `action.Policies`, and bool methods are created only for `entity.Policies` (`DomainToCSharpExporter.cs:379`). Interpreter execution of the module method and the printed C# method both omit the stage gate. The scratch invoke path adds it outside the module. A domain with a stage policy that does not hold therefore blocks in `InvokeAction` and proceeds in the artifact that is supposed to be the domain meaning. `docs/plans/p3b-followups-2026-09-27.md` already records that these policies are not module methods; the behavior is still wrong on a valid domain.
- Suggestion: Lower each stage policy into the module under a scope-qualified method, and emit the same guard in every action body that runs in that stage. Delete the prelude loop so simulate and print share that tree. Add a test that prints the action method and runs it on the Interpreter with the stage policy false, and asserts both fail the same way.
- Status: open

### Issue 2 -- Severity: bug
- File: Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:371
- Description: `CompletePolicyBodies` keys bodies by `(entity.Name, policy.Name)` and returns when the key exists. Entity policies are inserted first (`DomainToCSharpExporter.cs:382`). `StructuralDomainAnalyzer` rejects duplicate names only inside one list (`StructuralDomainAnalyzer.cs:76`, `:101`, `:110`) and rejects action/policy collisions only for entity policies (`:90`). An entity policy and a stage or action policy may share a name. `EvaluatePolicy` then runs the entity body for the stage policy (`DomainEntityInstance.cs:382`). The stage expression is dropped. On a valid domain this is a wrong guard result, not a miss.
- Suggestion: Key the cache by scope (entity / stage / action) plus name, or reject cross-scope duplicate policy names in `StructuralDomainAnalyzer` before lower. Test both the analyze rejection and, if scoped keys remain, that `EvaluatePolicy` on the stage policy executes the stage expression.
- Status: open

### Issue 3 -- Severity: bug
- File: Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:488
- Description: `GetHolder` keeps only ids `ExtensionCatalog.Core` contains (`temporal`, `storage`, `persistence`) and opens that session. `sqlite`, `http`, `sqlserver`, and any other id are dropped with no error. `DomainSession.ForExtensions` throws on an id the catalog cannot resolve (`DomainSession.cs:101`). `DomainModelAnalyzer.Analyze` and `GetOrAnalyze` both enter through `GetHolder`, and the first call `Bind`s the stripped session for the life of that `Domain` instance. A domain whose `Extensions` include `sqlite` then analyzes without `SqliteDefaults.ApplyTypeMaps` (SQL types such as `Number` → `INTEGER`, `Boolean` → `INTEGER`, `DateTime` → `TEXT`). A domain that lists `http` never runs `HttpSurfacePass`. `Extensions` still lists the id. The authoring compiler path does not hit this when it `Analyze`s first; any first touch through the cache or `DomainModelAnalyzer` does, including a `Domain` value that was never passed through `DslCompiler`.
- Suggestion: If any extension id is not loaded by the session about to bind, throw the same error `ForExtensions` throws. Do not filter. Add a test that builds a domain with `Extensions` containing `sqlite`, calls `GetOrAnalyze` with nothing bound, and asserts the throw (or asserts the sqlite column type when a caller deliberately loaded `SqliteLibrary`).
- Status: open

### Issue 4 -- Severity: bug
- File: Poly/DomainModeling/Evolution/DomainChange.cs:684
- Description: `AddDomainExtensionChange` stores any non-empty id. Only a duplicate is an error. Ids outside `ExtensionCatalog.Core` return before primitive seeding and are never resolved (`:685`). `DomainEvolution.Apply` analyzes with the caller-supplied session when one is passed (`DomainEvolution.cs:45`, `ResolveSession` at `:93`). That session is not rebuilt from the proposed extension list. `Apply([new AddDomainExtensionChange("http")], session: temporalSession)` therefore succeeds, `Domain.Extensions` contains `http`, and `HttpSurfacePass` never ran. The null-session sibling calls `ForExtensions` and throws. The comment on the change says duplicate ids fail closed and does not mention this hole.
- Suggestion: Resolve the id against the session catalog inside `ApplyTo` or in `Apply` before accepting the root. An id the session cannot load is an error, same as `ForExtensions`. Test the passed-session sibling, not only `AddDomainExtensionChange("temporal")` on a core catalog.
- Status: open

### Issue 5 -- Severity: suggestion
- File: Poly/DomainModeling/Compile/DomainSession.cs:150
- Description: Frozen compile is dirty analysis then stop, then `Lower`. `Analyze` returns the result and binds it even when `HasErrors` is true (`:128`). `Lower` and `Emit` never read `HasErrors`. `DomainEvolution` and `DslCompiler.CompileCore` do stop, so the compiler and MCP apply path are safe. Any caller of the public session door can lower and emit a rejected domain. `GetOrAnalyze` also caches an error analysis because `RequireCatalog` returns early on `HasErrors` (`DomainModelAnalyzer.cs:54`). Runtime invoke never checks `HasErrors` (no matches under `DomainModeling/Runtime`).
- Suggestion: `Lower` and `Emit` throw when `analysis.HasErrors`, listing the diagnostics. Keep the evolution rollback. Test `session.Lower` on a domain whose analysis reports a structural error.
- Status: open

### Issue 6 -- Severity: suggestion
- File: Poly/DomainModeling/Analysis/RuntimeAnalysisCache.cs:195
- Description: `EntryMethodNames` / `ExitMethodNames` fall through to a method named `OnEntry` or `OnExit` with no stage. `ExecuteEffectList` uses that lookup when the cached entry/exit body is missing (`DomainEntityInstance.cs:758`). An action method with that name then runs as the stage hook instead of the miss throw. On the normal `GetOrLower` path a non-empty entry batch is cached and this fallback does not run. It is an unconstrained second path, reachable after a body miss or if a stage has no `OnEntry{Stage}` method.
- Suggestion: Delete the bare `OnEntry` / `OnExit` candidates. A miss stays the existing `InvalidOperationException`.
- Status: open

### Issue 7 -- Severity: suggestion
- File: docs/plans/simple-agent-tasks/PIPELINE-STATUS.md:15
- Description: The agent-pick line still lists lowering-module holism as `THEN`, and the header says updated 2026-09-04. `master` is `9db8868` (2026-09-28), session-compile slices through #81 are in the notes, and `docs/plans/p3b-followups-2026-09-27.md` is the residual list. That follow-up anchors `EvaluateParameterBindings` at `DomainEntityInstance.cs:646`. The method is now at `:654` and has no callers. `BindPeerInEffect` / `EvaluateExprOnPeer` (`DomainEntityInstance.HostAbi.cs:430`, `:495`) are also unreachable from outside the dead rewrite. The live `CreateChildInstance` / `PrevalidateCreateInitializers` lowers run on literal bindings built from values the VM already evaluated, not on the original DSL expressions. The invariant comment on `ExecuteEffectList` (`DomainEntityInstance.cs:690`, "execute never lowers") is false for those literal re-lowers and misleading for the dead sites. An agent that trusts the pick line or the follow-up line numbers will restart shipped work or patch the wrong method.
- Suggestion: Point the pick line at the open residuals (F1–F4 here) instead of a blanket holism item. Rebuild the p3b table from a grep of `new DomainExpressionLoweringPass` under `DomainModeling/Runtime`, and drop sites with no caller. Narrow the `ExecuteEffectList` comment to the paths that actually bind module bodies.
- Status: open

## Checklist

- [x] Diff collected; scope is the whole tree (no local delta)
- [x] Stance: adversarial; standard mode (one pass)
- [x] Producer/consumer keys traced for policy bodies and extension ids
- [x] Null / partial / not-found / missing-contract outcomes distinct where checked
- [x] Sibling-path check done for extension resolve, policy scope, and dirty stop
- [x] Fail-loud changes were not the subject; missing fail-loud was
- [x] Invariant comments on `ExecuteEffectList` and `AddDomainExtensionChange` checked
- [x] Counts and sites recomputed from current files
- [x] Same-shape-different-meaning considered for stage policies (prelude vs module)
- [x] Oracles not treated as proof that the sibling path is tested
- [x] Plan status compared with `git log` and p3b
- [x] Review file written under `docs/`
- [x] Follow-up tasks written under `docs/`
- [x] Prior p3b items dispositioned from current source
- [ ] Pass B (not requested)
- [x] User given paths and top issues
