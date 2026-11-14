using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Runtime;

using AccessModifier = Poly.Introspection.AccessModifier;
using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Lowering;

// Actions: action method dispatch, bodies, and expression/effect body lowering.
public sealed partial class DomainToCSharpExporter {
    // ── Action method builder ───────────────────────────────────

    private static void AddActionMethods(Entity entity, List<MethodDefinitionNode> methods,
        string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        Domain domain, INodeMetadataProvider analysis,
        ContractSplicePlan? splice = null) {
        var variants = new List<(Action Action, string? SourceStage)>();
        foreach (var action in entity.Actions)
            variants.Add((action, null));
        foreach (var stage in entity.Stages)
            foreach (var action in stage.Actions)
                variants.Add((action, stage.Name));

        foreach (var group in variants.GroupBy(v => v.Action.Name, StringComparer.Ordinal)) {
            var items = group.ToList();
            if (items.Count == 1) {
                var (action, source) = items[0];
                AddActionMethod(entity, action, methods, stageEnumTypeName,
                    postTransitionNotifyStages, previousStageNotifyStages,
                    source, domain, analysis, splice);
                continue;
            }

            AddStageDispatchedActionMethod(entity, items, methods, stageEnumTypeName,
                postTransitionNotifyStages, previousStageNotifyStages, domain, analysis, splice);
        }
    }

    private static AccessModifier ActionGroupAccess(
        IReadOnlyList<(Action Action, string? SourceStage)> variants) =>
        variants.Any(v => DomainAccess.ForAction(v.Action) == AccessModifier.Public)
            ? AccessModifier.Public
            : AccessModifier.Internal;

    private static void AddActionMethod(Entity entity, Action action,
        List<MethodDefinitionNode> methods, string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        string? sourceStageName, Domain domain,
        INodeMetadataProvider analysis, ContractSplicePlan? splice = null,
        AccessModifier? access = null) {
        var isVoid = action.Result is not { Members.Count: > 0 };
        var body = BuildFullActionBody(entity, action, stageEnumTypeName,
            postTransitionNotifyStages, previousStageNotifyStages,
            loweringSourceStage: sourceStageName, guardSourceStage: sourceStageName,
            domain, analysis, isVoid, splice: splice);

        // All actions return DomainResult (void) or DomainResult<T> (typed).
        // This lets callers pattern-match on IsSuccess without exceptions.
        Node returnType;
        if (isVoid) {
            returnType = new NamedTypeReference("DomainResult");
        }
        else {
            var innerType = MapDomainTypeRef(action.Result.Members[0].Type, domain, analysis);
            returnType = new NamedTypeReference("DomainResult",
                TypeArguments: [innerType]);
        }

        AddScopedActionMethods(methods, action.Name, returnType,
            action.Parameters
                .Select(p => new Parameter(p.Name, MapDomainTypeRef(p.Type, domain, analysis)))
                .ToList(),
            body,
            DomainAccess.Demote(
                access ?? DomainAccess.ForAction(action), domain,
                [.. action.Parameters.Select(p => MapDomainTypeRef(p.Type, domain, analysis)), returnType]));
    }

    internal const string ActionBodyPrefix = "__BodyOf";

    private static Node ActionScopeCall(string member, params Node[] arguments) =>
        new Invoke(new Member(new ClrTypeReference(typeof(ActionScope)), member), arguments);

    /// <summary>
    /// <c>ActionScope.Touch(this)</c>, the first statement of every mutator (L-03c): the action wrapper, a subscription
    /// handler, <c>Attach*</c> and <c>Register*Subscriber</c>. A no-op outside a scope; inside one it snapshots the
    /// receiver once, so a rollback restores it.
    /// </summary>
    private static Node TouchThis() => ActionScopeCall("Touch", new ThisReference());

    /// <summary>
    /// An action is atomic: the action's method (at the action's own access) is a wrapper that opens an <see cref="ActionScope"/>,
    /// touches <c>this</c> (L-03c: every mutator touches its receiver at entry, so the scope snapshots it before the
    /// body writes it), calls the private body method that holds the lowered action, and commits on success.
    /// A failed result, an exception, or a throwing commit leaves <c>__done</c> false, so the
    /// <c>finally</c> rolls the still-open frame back. The wrapper's locals begin with <c>__</c>,
    /// which the DSL reserves, and the body is a separate method, so no authored name can collide.
    /// The token is cast to its own type: the VM analysis does not resolve the static call's return
    /// type, and an unresolved first write makes the analysis of the same body reject itself on re-entry.
    /// </summary>
    private static void AddScopedActionMethods(List<MethodDefinitionNode> methods, string name,
        Node returnType, List<Parameter> parameters, Block body, AccessModifier access,
        Node? staticReceiver = null) {
        var bodyName = ActionBodyPrefix + name;
        var scope = new Variable("__scope");
        var done = new Variable("__done");
        var result = new Variable("__result");
        var isStatic = staticReceiver is not null;
        var call = new Invoke(new Member(staticReceiver ?? new ThisReference(), bodyName), [.. parameters]);
        var wrapper = new Block([
            new Assignment(scope, new TypeCast(
                ActionScopeCall("Begin", new Constant(null)),
                new ClrTypeReference(typeof(ActionScopeToken)))),
            new Assignment(done, new Constant(false)),
            new TryCatchFinally(
                new Block([
                    .. isStatic ? Array.Empty<Node>() : [TouchThis()],
                    new Assignment(result, call),
                    new IfStatement(
                        new Poly.Ast.Nodes.Not(new Member(result, "IsSuccess")),
                        new Block([new Return(result)])),
                    ActionScopeCall("Commit", scope),
                    new Assignment(done, new Constant(true)),
                    new Return(result)
                ], [result]),
                FinallyBlock: new Block([
                    new IfStatement(new Poly.Ast.Nodes.Not(done), ActionScopeCall("Rollback", scope))
                ]))
        ], [scope, done]);
        methods.Add(new MethodDefinitionNode(
            name,
            returnType,
            Parameters: parameters,
            Body: wrapper,
            IsStatic: isStatic,
            AccessModifier: access));

        methods.Add(new MethodDefinitionNode(
            bodyName,
            returnType,
            Parameters: parameters
                .Select(p => new Parameter(p.Name, p.TypeReference))
                .ToList(),
            Body: body,
            IsStatic: isStatic,
            AccessModifier: AccessModifier.Private));
    }

    private static Block BuildFullActionBody(Entity entity, Action action,
        string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        string? loweringSourceStage, string? guardSourceStage, Domain domain,
        INodeMetadataProvider analysis, bool isVoid, LocalNames? names = null,
        ContractSplicePlan? splice = null, Node? methodResultType = null) {
        var paramNames = new HashSet<string>(
            action.Parameters.Select(p => p.Name), StringComparer.Ordinal);
        var effectsBody = LowerActionToMethodBody(entity, action, paramNames, stageEnumTypeName,
            postTransitionNotifyStages, previousStageNotifyStages,
            loweringSourceStage, domain, analysis, isVoid, names, methodResultType);
        effectsBody = SpliceContractBody(domain, entity, action, effectsBody, isVoid, analysis, splice, names);
        return BuildActionBodyWithGuards(action, entity, effectsBody, domain,
            guardSourceStage, stageEnumTypeName, isVoid, analysis, loweringSourceStage, methodResultType);
    }

    private static void AddStageDispatchedActionMethod(Entity entity,
        List<(Action Action, string? SourceStage)> variants,
        List<MethodDefinitionNode> methods, string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        Domain domain, INodeMetadataProvider analysis,
        ContractSplicePlan? splice = null) {
        var representative = variants[0].Action;
        var paramSignature = DomainAnalysis.ParameterSignature(representative);
        foreach (var (action, _) in variants) {
            if (!string.Equals(
                    DomainAnalysis.ParameterSignature(action), paramSignature, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Action '{action.Name}' on entity '{entity.Name}' has incompatible parameter lists across stages.");
        }

        // Stage copies of one name are distinct actions and may declare different results.
        // The merged method returns the typed slot when any copy yields a value; a void
        // copy then succeeds with no value. Typed copies must agree on the type.
        static bool IsVoidAction(Action a) => a.Result is not { Members.Count: > 0 };
        var typedVariants = variants.Where(v => !IsVoidAction(v.Action)).ToList();
        var typedRepresentative = typedVariants.Count > 0 ? typedVariants[0].Action : null;
        if (typedRepresentative is not null) {
            var typeName = typedRepresentative.Result!.Members[0].Type.TypeName;
            foreach (var (action, _) in typedVariants) {
                if (!string.Equals(action.Result!.Members[0].Type.TypeName, typeName, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Action '{action.Name}' on entity '{entity.Name}' returns different types across stages.");
            }
        }
        var isVoid = typedRepresentative is null;
        Node actionResultType = isVoid
            ? new NamedTypeReference("DomainResult")
            : new NamedTypeReference("DomainResult",
                TypeArguments: [MapDomainTypeRef(typedRepresentative!.Result!.Members[0].Type, domain, analysis)]);
        // Null when the branch's own result already matches the method's.
        Node? ResultOverride(Action a) => !isVoid && IsVoidAction(a) ? actionResultType : null;

        // One method holds every stage branch and the entity-level tail, so their
        // locals come from one name source and cannot collide.
        var names = new LocalNames();
        var nodes = new List<Node>();
        var locals = new List<Node>();
        Action? entityLevel = null;
        foreach (var (action, sourceStage) in variants) {
            if (sourceStage is null)
                entityLevel = action;
        }

        // An implicit single stage has no enum and no guard. Mirror the multi-stage
        // empty-copy rule: an empty stage copy falls back to the entity-level body,
        // but a non-empty stage copy wins.
        if (stageEnumTypeName is null) {
            var stageAction = variants.FirstOrDefault(v => v.SourceStage is not null).Action;
            var chosen = stageAction is not null
                && (stageAction.Effects.Count > 0 || stageAction.Policies.Count > 0)
                ? stageAction
                : entityLevel ?? stageAction ?? variants[0].Action;
            AddActionMethod(entity, chosen, methods,
                stageEnumTypeName, postTransitionNotifyStages, previousStageNotifyStages,
                sourceStageName: null, domain, analysis, splice,
                access: ActionGroupAccess(variants));
            return;
        }

        // G10: group stage copies by identical DSL fragment. Apply the empty-copy
        // substitution first (an empty/policy-free stage copy uses the entity body),
        // then key on the fragment: effects + require + source-stage policies +
        // OnExit effects + parameter names. Identical fragments share one body
        // guarded by a stage-set equality; a singleton keeps a single-stage if.
        var printer = new DomainDslPrinter(RuntimeAnalysisCache.Session(domain));
        var groups = new List<(string Key, Action Action, List<string> Stages)>();
        var groupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (action, sourceStage) in variants) {
            if (sourceStage is null)
                continue;

            if (stageEnumTypeName is null)
                throw new InvalidOperationException(
                    $"Action '{action.Name}' is stage-scoped on '{entity.Name}' but no stage enum was emitted.");

            // SA empty stage-copy (no effects/policies) → entity body. Same as TryResolveAction.
            var branchAction = action.Effects.Count == 0
                && action.Policies.Count == 0
                && entityLevel is not null
                ? entityLevel
                : action;
            var source = entity.Stages.FirstOrDefault(s =>
                string.Equals(s.Name, sourceStage, StringComparison.Ordinal));
            var key = printer.PrintActionFragmentKey(branchAction, source);
            if (groupIndex.TryGetValue(key, out var idx)) {
                groups[idx].Stages.Add(sourceStage);
            }
            else {
                groupIndex[key] = groups.Count;
                groups.Add((key, branchAction, [sourceStage]));
            }
        }

        foreach (var group in groups) {
            var branchBody = BuildFullActionBody(entity, group.Action, stageEnumTypeName,
                postTransitionNotifyStages, previousStageNotifyStages,
                loweringSourceStage: group.Stages[0], guardSourceStage: null,
                domain, analysis, IsVoidAction(group.Action), names, splice, ResultOverride(group.Action));
            nodes.Add(new IfStatement(
                InWatchedStages(
                    new Member(new ThisReference(), "CurrentStage"),
                    stageEnumTypeName!,
                    group.Stages),
                branchBody));
        }

        if (entityLevel is not null) {
            var tail = BuildFullActionBody(entity, entityLevel, stageEnumTypeName,
                postTransitionNotifyStages, previousStageNotifyStages,
                loweringSourceStage: null, guardSourceStage: null,
                domain, analysis, IsVoidAction(entityLevel), names, splice, ResultOverride(entityLevel));
            nodes.AddRange(tail.Nodes);
            locals.AddRange(tail.Variables);
        }
        else {
            nodes.Add(FailureLowering.Return(actionResultType, new FailureLowering.Spec(
                DomainFailureKind.Stage,
                new Constant($"'{representative.Name}' is not valid for the current stage on entity '{entity.Name}'."),
                $"{entity.Name}.{representative.Name}")));
        }

        Node returnType = isVoid
            ? new NamedTypeReference("DomainResult")
            : actionResultType;

        AddScopedActionMethods(methods, representative.Name, returnType,
            representative.Parameters
                .Select(p => new Parameter(p.Name, MapDomainTypeRef(p.Type, domain, analysis)))
                .ToList(),
            new Block(nodes, locals),
            DomainAccess.Demote(
                ActionGroupAccess(variants), domain,
                [.. representative.Parameters.Select(p => MapDomainTypeRef(p.Type, domain, analysis)), returnType]));
    }

    // ── Lowering helpers ────────────────────────────────────────

    internal static Node? LowerActionToMethodBody(
        Entity entity, Action action,
        HashSet<string>? paramNames, string? stageEnumTypeName,
        IReadOnlySet<string>? postTransitionNotifyStages,
        IReadOnlySet<string>? previousStageNotifyStages,
        string? sourceStageName, Domain domain,
        INodeMetadataProvider analysis, bool isVoid = true, LocalNames? names = null,
        Node? methodResultType = null) {
        if (action.Effects.Count == 0) return null;
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        var esm = analysis.GetStructure(entity);
        Node actionResultType = methodResultType ?? (isVoid
            ? new NamedTypeReference("DomainResult")
            : new NamedTypeReference("DomainResult",
                TypeArguments: [MapDomainTypeRef(action.Result.Members[0].Type, domain, analysis)]));
        var context = new LoweringContext(
            new ThisReference(),
            analysis,
            domain,
            ActionParameterNames: paramNames,
            StageEnumTypeName: stageEnumTypeName,
            PostTransitionNotifyStages: postTransitionNotifyStages,
            PreviousStageNotifyStages: previousStageNotifyStages,
            SourceStageName: sourceStageName,
            EnumPropertyNames: enumProps,
            LifecyclePropertyName: esm?.LifecyclePropertyName,
            LifecycleEnumTypeName: esm?.LifecycleEnumTypeName,
            ActionName: action.Name,
            ActionResultType: actionResultType,
            Names: names);
        var effectPass = new EffectLoweringPass(entity, context);
        return effectPass.LowerActionBody(action.Effects);
    }

    /// <summary>
    /// Policy method body: quantifier loops (if any) sit immediately before <c>return</c> of the value.
    /// </summary>
    internal static Node? LowerExpressionToMethodBody(
        DomainExpression expr, Entity entity, Domain domain,
        INodeMetadataProvider analysis) {
        var enumProps = GetEnumPropertyNames(entity, domain, analysis);
        var context = new LoweringContext(
            new ThisReference(),
            analysis,
            domain,
            EnumPropertyNames: enumProps,
            NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(entity, domain, analysis),
            IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(entity, domain, analysis),
            PropertyTypeResolver: EffectLoweringPass.BuildPropertyTypeResolver(entity),
            SourceEntityName: entity.Name);
        var pass = new DomainExpressionLoweringPass(context);
        var lowered = pass.LowerCondition(expr, new ThisReference());
        var body = lowered.Before(new Return(lowered.Value));
        return body is Block ? body : new Block([body]);
    }
}