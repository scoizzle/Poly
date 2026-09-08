using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Session + analysis + lowered module for a <see cref="Domain"/> instance.
/// Authoring <see cref="DomainSession.Analyze"/> binds the session that loaded
/// the domain's <c>uses</c> (vendor maps included). Fallback reopen uses the
/// core catalog only when nothing has bound yet.
/// <see cref="GetOrLower"/> caches the operation module
/// (<see cref="DomainProgramProjection.ToSyntax"/>), populates OnEntry/OnExit
/// batches, and side-caches subscription effect trees so Domain-bound hot paths
/// bind module bodies instead of re-lowering at execute time.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; set; }
        public AnalysisResult? Analysis { get; set; }
        public IReadOnlyList<TypeDefinitionNode>? Module { get; set; }
        /// <summary>Plan entry → lowered subscription effect body (no execute-time LowerActionBody).</summary>
        public Dictionary<SubscriptionDispatchPlanEntry, Node>? SubscriptionBodies { get; set; }
        /// <summary>(entry, watched stage) → module When* handler method.</summary>
        public Dictionary<(SubscriptionDispatchPlanEntry Entry, string Stage), MethodDefinitionNode>? SubscriptionHandlers { get; set; }
        /// <summary>VM-shaped OnEntry/OnExit bodies for Domain-bound execute (export methods stay UseThis).</summary>
        public Dictionary<(string Entity, string Stage, string Kind), Node>? EntryExitBodies { get; set; }
        /// <summary>VM-shaped policy bodies for EvaluatePolicy (export bool methods stay UseThis).</summary>
        public Dictionary<(string Entity, string Policy), Node>? PolicyBodies { get; set; }
    }

    private static readonly ConditionalWeakTable<Domain, Holder> Cache = new();

    public static DomainSession Session(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return GetHolder(domain).Session;
    }

    public static void Bind(Domain domain, DomainSession session, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        var holder = Cache.GetValue(domain, _ => new Holder { Session = session });
        lock (holder) {
            var sessionChanged = !ReferenceEquals(holder.Session, session);
            var analysisChanged = analysis is not null && !ReferenceEquals(holder.Analysis, analysis);
            holder.Session = session;
            if (analysis is not null)
                holder.Analysis = analysis;
            if (sessionChanged || analysisChanged) {
                holder.Module = null;
                holder.SubscriptionBodies = null;
                holder.SubscriptionHandlers = null;
                holder.EntryExitBodies = null;
                holder.PolicyBodies = null;
            }
        }
    }

    public static AnalysisResult GetOrAnalyze(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.Analysis is not null)
            return holder.Analysis;

        lock (holder) {
            if (holder.Analysis is not null)
                return holder.Analysis;
            var analysis = holder.Session.AnalyzeWithoutBind(domain);
            DomainModelAnalyzer.RequireCatalog(analysis, domain);
            holder.Analysis = analysis;
            return analysis;
        }
    }

    public static IReadOnlyList<TypeDefinitionNode> GetOrLower(
        Domain domain, DomainSession session, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        Bind(domain, session, analysis);
        var holder = GetHolder(domain);
        if (holder.Module is not null)
            return holder.Module;
        lock (holder) {
            if (holder.Module is not null)
                return holder.Module;
            var module = DomainProgramProjection.ToSyntax(domain, analysis);
            var (module2, entryExit) = PopulateEntryExitMethods(domain, analysis, module);
            var (bodies, handlers) = BuildSubscriptionCaches(domain, analysis, module2);
            holder.Module = module2;
            holder.SubscriptionBodies = bodies;
            holder.SubscriptionHandlers = handlers;
            holder.EntryExitBodies = entryExit;
            holder.PolicyBodies = BuildPolicyBodies(domain, analysis);
            return holder.Module;
        }
    }

    internal static bool TryGetModuleMethod(
        Domain domain, string entityName, string methodName, out MethodDefinitionNode? method) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentException.ThrowIfNullOrEmpty(methodName);
        var holder = GetHolder(domain);
        var module = holder.Module;
        if (module is not null) {
            foreach (var type in module) {
                if (!string.Equals(type.Name, entityName, StringComparison.Ordinal))
                    continue;
                if (type.Methods is null)
                    break;
                foreach (var candidate in type.Methods) {
                    if (string.Equals(candidate.Name, methodName, StringComparison.Ordinal)) {
                        method = candidate;
                        return true;
                    }
                }
                break;
            }
        }
        method = null;
        return false;
    }

    internal static bool TryGetEntryMethod(
        Domain domain, string entityName, string stageName, out MethodDefinitionNode? method) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentException.ThrowIfNullOrEmpty(stageName);
        foreach (var name in EntryMethodNames(stageName)) {
            if (TryGetModuleMethod(domain, entityName, name, out method) && method is not null)
                return true;
        }
        method = null;
        return false;
    }

    internal static bool TryGetExitMethod(
        Domain domain, string entityName, string stageName, out MethodDefinitionNode? method) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentException.ThrowIfNullOrEmpty(stageName);
        foreach (var name in ExitMethodNames(stageName)) {
            if (TryGetModuleMethod(domain, entityName, name, out method) && method is not null)
                return true;
        }
        method = null;
        return false;
    }

    /// <summary>
    /// Effects-only body cached at <see cref="GetOrLower"/> for Domain-bound subscription dispatch.
    /// </summary>
    internal static bool TryGetSubscriptionBody(
        Domain domain, SubscriptionDispatchPlanEntry entry, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        var holder = GetHolder(domain);
        if (holder.SubscriptionBodies is not null
            && holder.SubscriptionBodies.TryGetValue(entry, out body)
            && body is not null)
            return true;
        body = null;
        return false;
    }

    internal static bool TryGetSubscriptionHandler(
        Domain domain, SubscriptionDispatchPlanEntry entry, string stageName,
        out MethodDefinitionNode? method) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(stageName);
        var holder = GetHolder(domain);
        if (holder.SubscriptionHandlers is not null
            && holder.SubscriptionHandlers.TryGetValue((entry, stageName), out method)
            && method is not null)
            return true;
        method = null;
        return false;
    }

    private static IEnumerable<string> EntryMethodNames(string stageName) {
        yield return $"OnEntry{stageName}";
        yield return $"{stageName}OnEntry";
        yield return "OnEntry";
    }

    private static IEnumerable<string> ExitMethodNames(string stageName) {
        yield return $"OnExit{stageName}";
        yield return $"{stageName}OnExit";
        yield return "OnExit";
    }

    /// <summary>
    /// Module ToSyntax inlines first-stage entry in the ctor only — populate
    /// OnEntry{Stage}/OnExit{Stage} export methods (UseThis) plus a VM-shaped
    /// side-cache for Domain-bound execute.
    /// </summary>
    private static (IReadOnlyList<TypeDefinitionNode> Module,
        Dictionary<(string, string, string), Node> EntryExit)
        PopulateEntryExitMethods(
            Domain domain, AnalysisResult analysis, IReadOnlyList<TypeDefinitionNode> module) {
        var types = module.ToList();
        var entryExit = new Dictionary<(string, string, string), Node>();
        for (var i = 0; i < types.Count; i++) {
            var td = types[i];
            var entity = domain.Types.OfType<Entity>()
                .FirstOrDefault(e => string.Equals(e.Name, td.Name, StringComparison.Ordinal));
            if (entity is null || entity.Stages.Count == 0)
                continue;

            var existingNames = new HashSet<string>(
                (td.Methods ?? []).Select(m => m.Name), StringComparer.Ordinal);
            var esm = analysis.GetStructure(entity);
            List<MethodDefinitionNode>? extras = null;

            foreach (var stage in entity.Stages) {
                // Exclude nested StageTransitionEffect — those still flush/recurse at
                // TransitionStage. ApplyInitialStageEntryEffects also filters them.
                var entryBatch = stage.OnEntryEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (entryBatch.Count > 0) {
                    var name = $"OnEntry{stage.Name}";
                    entryExit[(entity.Name, stage.Name, "entry")] =
                        LowerStageBatchVm(entity, domain, analysis, esm, entryBatch);
                    if (existingNames.Add(name)) {
                        extras ??= [];
                        extras.Add(BuildStageBatchMethodExport(
                            entity, domain, analysis, esm, name, entryBatch));
                    }
                }
                var exitBatch = stage.OnExitEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (exitBatch.Count > 0) {
                    var name = $"OnExit{stage.Name}";
                    entryExit[(entity.Name, stage.Name, "exit")] =
                        LowerStageBatchVm(entity, domain, analysis, esm, exitBatch);
                    if (existingNames.Add(name)) {
                        extras ??= [];
                        extras.Add(BuildStageBatchMethodExport(
                            entity, domain, analysis, esm, name, exitBatch));
                    }
                }
            }

            if (extras is null)
                continue;
            types[i] = td with { Methods = [.. td.Methods ?? [], .. extras] };
        }
        return (types, entryExit);
    }

    private static Node LowerStageBatchVm(
        Entity entity,
        Domain domain,
        AnalysisResult analysis,
        EntityStructureMetadata? esm,
        IReadOnlyList<Effect> effects) {
        var ctx = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: false,
            Domain: domain,
            EnumPropertyNames: esm?.EnumPropertyNames);
        return new EffectLoweringPass(entity, ctx).LowerActionBody(effects) ?? new Block([]);
    }

    private static MethodDefinitionNode BuildStageBatchMethodExport(
        Entity entity,
        Domain domain,
        AnalysisResult analysis,
        EntityStructureMetadata? esm,
        string methodName,
        IReadOnlyList<Effect> effects) {
        var ctx = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            UseThisReference: true,
            Domain: domain,
            EnumPropertyNames: esm?.EnumPropertyNames);
        var pass = new EffectLoweringPass(entity, ctx);
        var body = pass.LowerActionBody(effects) ?? new Block([]);
        return new MethodDefinitionNode(
            methodName,
            new TypeReference("void"),
            Body: body,
            AccessModifier: AccessModifier.Private);
    }

    internal static bool TryGetEntryExitBody(
        Domain domain, string entityName, string stageName, string kind, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.EntryExitBodies is not null
            && holder.EntryExitBodies.TryGetValue((entityName, stageName, kind), out body)
            && body is not null)
            return true;
        body = null;
        return false;
    }

    private static (
        Dictionary<SubscriptionDispatchPlanEntry, Node> Bodies,
        Dictionary<(SubscriptionDispatchPlanEntry, string), MethodDefinitionNode> Handlers)
        BuildSubscriptionCaches(
            Domain domain, AnalysisResult analysis, IReadOnlyList<TypeDefinitionNode> module) {
        var bodies = new Dictionary<SubscriptionDispatchPlanEntry, Node>(ReferenceEqualityComparer.Instance);
        var handlers = new Dictionary<(SubscriptionDispatchPlanEntry, string), MethodDefinitionNode>();

        var entities = domain.Types.OfType<Entity>().ToList();
        var entityLookup = entities.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var subscriptionsBySubscriber = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
            StringComparer.Ordinal);
        var subscriptionsByTarget = new Dictionary<string, List<DomainToCSharpExporter.SubscriptionInfo>>(
            StringComparer.Ordinal);

        foreach (var entity in entities) {
            var subList = new List<DomainToCSharpExporter.SubscriptionInfo>();
            var entityPlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(entity);
            if (entityPlan is not null)
                DomainToCSharpExporter.CollectSubscriptionInfo(
                    entityPlan, entity, null, entityLookup, subList, subscriptionsByTarget);

            foreach (var stage in entity.Stages) {
                var stagePlan = analysis.GetMetadata<SubscriptionDispatchPlanMetadata>(stage);
                if (stagePlan is not null)
                    DomainToCSharpExporter.CollectSubscriptionInfo(
                        stagePlan, entity, stage.Name, entityLookup, subList, subscriptionsByTarget);
            }

            if (subList.Count > 0)
                subscriptionsBySubscriber[entity.Name] = subList;
        }

        var handlerNames = DomainToCSharpExporter.BuildHandlerNames(subscriptionsBySubscriber);
        var typesByName = module
            .Where(t => t.Methods is { Count: > 0 })
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var (subscriberName, subList) in subscriptionsBySubscriber) {
            typesByName.TryGetValue(subscriberName, out var subscriberType);
            var entity = entityLookup[subscriberName];
            var esm = analysis.GetStructure(entity);

            foreach (var info in subList) {
                var entry = info.Subscription;
                if (handlerNames.TryGetValue(info, out var handlerName)
                    && subscriberType?.Methods is { } methods) {
                    var method = methods.FirstOrDefault(m =>
                        string.Equals(m.Name, handlerName, StringComparison.Ordinal));
                    if (method is not null)
                        handlers[(entry, info.StageName)] = method;
                }

                if (bodies.ContainsKey(entry))
                    continue;

                var effects = entry.Effects;
                if (effects.Count == 0) {
                    bodies[entry] = new Block([]);
                    continue;
                }

                IReadOnlyDictionary<string, Node>? peerParams = null;
                if (entry.PeerBinding is { Length: > 0 } peerBinding) {
                    peerParams = new Dictionary<string, Node>(StringComparer.Ordinal) {
                        [peerBinding] = new Parameter(
                            peerBinding, new NamedTypeReference(entry.TargetEntityName))
                    };
                }

                var ctx = new LoweringContext(
                    new Parameter("entity", new TypeReference(entity.Name)),
                    Parameters: peerParams,
                    Analysis: analysis,
                    UseThisReference: false,
                    Domain: domain,
                    EnumPropertyNames: esm?.EnumPropertyNames);
                var pass = new EffectLoweringPass(entity, ctx);
                bodies[entry] = pass.LowerActionBody(effects) ?? new Block([]);
            }
        }

        return (bodies, handlers);
    }

    private static Dictionary<(string, string), Node> BuildPolicyBodies(
        Domain domain, AnalysisResult analysis) {
        var map = new Dictionary<(string, string), Node>();
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var policy in entity.Policies) {
                var entityParam = new Parameter("entity", new TypeReference(entity.Name));
                var pass = new DomainExpressionLoweringPass(new LoweringContext(
                    entityParam,
                    Analysis: analysis,
                    Domain: domain,
                    UseThisReference: false,
                    PropertyTypeResolver: EffectLoweringPass.BuildPropertyTypeResolver(entity),
                    NavigationNameResolver: EffectLoweringPass.BuildNavigationNameResolver(entity, domain, analysis),
                    IsCollectionNavigation: EffectLoweringPass.BuildIsCollectionNavigation(entity, domain, analysis),
                    IsRelationshipNavigation: EffectLoweringPass.BuildIsRelationshipNavigation(entity, domain, analysis),
                    SourceEntityName: entity.Name));
                var lowered = pass.Lower(policy.Expression, entityParam);
                if (lowered is not null)
                    map[(entity.Name, policy.Name)] = lowered;
            }
        }
        return map;
    }

    internal static bool TryGetPolicyBody(
        Domain domain, string entityName, string policyName, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.PolicyBodies is not null
            && holder.PolicyBodies.TryGetValue((entityName, policyName), out body)
            && body is not null)
            return true;
        body = null;
        return false;
    }

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d => {
            var ids = d.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
            return new Holder { Session = DomainSession.ForExtensions(ids) };
        });
}
