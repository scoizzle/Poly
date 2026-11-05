using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Introspection;

using AccessModifier = Poly.Introspection.AccessModifier;
using PrimType = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Session + analysis + lowered module for a <see cref="Domain"/> instance.
/// <see cref="DomainSession.Analyze"/> binds its session before the pipeline, so
/// passes see that compilation (vendor maps included). A domain nothing has bound
/// resolves every extension id through <see cref="ExtensionCatalog.Core"/>; an id
/// that catalog cannot load throws, the same as <see cref="DomainSession.ForExtensions"/>.
/// <see cref="GetOrLower"/> caches the operation module
/// (<see cref="DomainProgramProjection.ToSyntax"/>), including policy, subscription,
/// and OnEntry/OnExit bodies so Domain-bound hot paths bind those trees instead of
/// re-lowering at execute time.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; set; }
        public AnalysisResult? Analysis { get; set; }
        public IReadOnlyList<TypeDefinitionNode>? Module { get; set; }
        /// <summary>Plan entry → watched-stage → module handler body.</summary>
        public Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>? SubscriptionBodies { get; set; }
        /// <summary>OnEntry/OnExit bodies shared with module methods.</summary>
        public Dictionary<(string Entity, string Stage, string Kind), Node>? EntryExitBodies { get; set; }
        /// <summary>Policy expression trees from session.Lower (same Lower as print).</summary>
        public Dictionary<(string Entity, string Policy), Node>? PolicyBodies { get; set; }
        /// <summary>Catalog index after Index Lowers; dropped when Bind replaces session or analysis.</summary>
        public CatalogIndex? Index { get; set; }
    }

    private static readonly ConditionalWeakTable<Domain, Holder> Cache = new();

    public static DomainSession Session(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        return GetHolder(domain).Session;
    }

    internal static ExpressionMeaning MeaningFor(Domain? domain) =>
        domain is null ? ExpressionMeaning.Empty : Session(domain).Meaning;

    internal static ExpressionFormRegistry FormsFor(Domain? domain) =>
        domain is null ? new ExpressionFormRegistry() : Session(domain).ExpressionForms;

    internal static string ClrTypeName(Domain? domain, string domainType) =>
        domain is null
            ? DomainTypeMapping.ToClrTypeName(domainType)
            : Session(domain).TypeMaps.ToClrTypeName(domainType);

    public static void Bind(Domain domain, DomainSession session, AnalysisResult? analysis = null) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        var holder = Cache.GetValue(domain, _ => new Holder { Session = session });
        lock (holder) {
            var sessionChanged = !ReferenceEquals(holder.Session, session);
            holder.Session = session;
            // A new session without an analysis must not keep the old session's analysis.
            if (analysis is not null)
                holder.Analysis = analysis;
            else if (sessionChanged)
                holder.Analysis = null;
            // A second Analyze on the same session must not drop the module the
            // executor is running. Only a different compilation (session) invalidates it.
            if (sessionChanged) {
                holder.Module = null;
                holder.Index = null;
                holder.SubscriptionBodies = null;
                holder.EntryExitBodies = null;
                holder.PolicyBodies = null;
            }
        }
    }

    public static AnalysisResult GetOrAnalyze(Domain domain) =>
        EnsureAnalysis(domain);

    private static AnalysisResult EnsureAnalysis(Domain domain) {
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

    public static IReadOnlyList<TypeDefinitionNode> GetOrLower(Domain domain) {
        var analysis = EnsureAnalysis(domain);
        return GetOrLower(domain, Session(domain), analysis);
    }

    internal static AnalysisResult Analysis(Domain domain) => EnsureAnalysis(domain);

    /// <summary>
    /// Lowers so <see cref="DomainSession.ArtifactCatalog"/> is filled, then indexes it.
    /// The index is reused until Bind replaces the session or analysis.
    /// <see cref="GetOrLower(Domain)"/> does not fill the catalog.
    /// </summary>
    internal static CatalogIndex Index(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.Index is not null)
            return holder.Index;
        var analysis = EnsureAnalysis(domain);
        DomainModelAnalyzer.RequireCatalog(analysis, domain);
        lock (holder) {
            if (holder.Index is not null)
                return holder.Index;
            var session = holder.Session;
            session.Lower(domain, analysis);
            holder.Index = new CatalogIndex(session.ArtifactCatalog);
            return holder.Index;
        }
    }

    public static IReadOnlyList<TypeDefinitionNode> GetOrLower(
        Domain domain, DomainSession session, AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(analysis);
        DomainModelAnalyzer.ThrowIfHasErrors(analysis);
        Bind(domain, session, analysis);
        var holder = GetHolder(domain);
        if (holder.Module is not null)
            return holder.Module;
        lock (holder) {
            if (holder.Module is not null)
                return holder.Module;
            var policyBodies = new Dictionary<(string, string), Node>();
            var subscriptionBodies = new Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>(
                ReferenceEqualityComparer.Instance);
            var module = DomainProgramProjection.ToSyntax(
                domain, analysis, policyBodies, subscriptionBodies);
            var (module2, entryExit) = PopulateEntryExitMethods(domain, analysis, module);
            var contributed = ConstraintCompilation.Apply(module2, domain, analysis, session.ConstraintCompilers);
            module2 = contributed.Module;
            if (contributed.ReplacedBodies.Count > 0) {
                Retarget(entryExit, contributed.ReplacedBodies);
                Retarget(policyBodies, contributed.ReplacedBodies);
                foreach (var byStage in subscriptionBodies.Values)
                    Retarget(byStage, contributed.ReplacedBodies);
            }
            holder.Module = module2;
            holder.SubscriptionBodies = subscriptionBodies;
            holder.EntryExitBodies = entryExit;
            holder.PolicyBodies = CompletePolicyBodies(domain, analysis, policyBodies);
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
        return TryGetModuleMethod(domain, entityName, $"OnEntry{stageName}", out method) && method is not null;
    }

    internal static bool TryGetExitMethod(
        Domain domain, string entityName, string stageName, out MethodDefinitionNode? method) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(entityName);
        ArgumentException.ThrowIfNullOrEmpty(stageName);
        return TryGetModuleMethod(domain, entityName, $"OnExit{stageName}", out method) && method is not null;
    }

    /// <summary>
    /// Module handler body cached at <see cref="GetOrLower"/> for Domain-bound
    /// subscription dispatch. <paramref name="targetStageName"/> selects the
    /// watched-stage handler (one info per stage on the entry).
    /// </summary>
    internal static bool TryGetSubscriptionBody(
        Domain domain, SubscriptionDispatchPlanEntry entry, string targetStageName, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(targetStageName);
        body = null;
        var holder = GetHolder(domain);
        if (holder.SubscriptionBodies is null)
            return false;
        if (!holder.SubscriptionBodies.TryGetValue(entry, out var byStage) || byStage.Count == 0) {
            // A second Analyze on the same session publishes new entry instances.
            // The cache stays keyed by the entries from the lower that built the
            // module. Match that one plan by signature so the handler is not
            // replaced with a missing-body throw.
            byStage = null;
            Dictionary<string, Node>? only = null;
            var matches = 0;
            foreach (var pair in holder.SubscriptionBodies) {
                if (!SameSubscription(pair.Key, entry))
                    continue;
                matches++;
                only = pair.Value;
            }
            if (matches != 1 || only is null || only.Count == 0)
                return false;
            byStage = only;
        }
        return byStage.TryGetValue(targetStageName, out body) && body is not null;
    }

    private static bool SameSubscription(
        SubscriptionDispatchPlanEntry cached, SubscriptionDispatchPlanEntry lookup) =>
        string.Equals(cached.RelationshipName, lookup.RelationshipName, StringComparison.Ordinal)
        && string.Equals(cached.SourceEntityName, lookup.SourceEntityName, StringComparison.Ordinal)
        && string.Equals(cached.TargetEntityName, lookup.TargetEntityName, StringComparison.Ordinal)
        && cached.Quantifier == lookup.Quantifier
        && string.Equals(cached.PeerBinding, lookup.PeerBinding, StringComparison.Ordinal)
        && SameStages(cached.StageNames, lookup.StageNames);

    private static bool SameStages(IReadOnlySet<string> cached, IReadOnlySet<string> lookup) {
        if (cached.Count != lookup.Count)
            return false;
        foreach (var name in cached) {
            if (!lookup.Contains(name))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Module ToSyntax inlines first-stage entry in the ctor only — populate
    /// OnEntry{Stage}/OnExit{Stage} methods once. EntryExitBodies holds the same
    /// bodies Domain-bound execute compiles.
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

            var esm = analysis.GetStructure(entity);
            foreach (var stage in entity.Stages) {
                // Whole-batch EntryExitBodies exclude nested StageTransitionEffect (no-nested
                // TransitionStage path). They are lowered here, at Lower, through
                // EffectLoweringPass; no execute path calls LowerActionBody. The bodies stay in
                // the cache for the tests that assert their shape; the printed type no longer
                // carries the dead OnEntry*/OnExit* members (D10).
                var entryBatch = stage.OnEntryEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (entryBatch.Count > 0) {
                    var entryMethod = BuildStageBatchMethodExport(
                        entity, domain, analysis, esm, $"OnEntry{stage.Name}", entryBatch);
                    entryExit[(entity.Name, stage.Name, "entry")] = entryMethod.Body!;
                }

                var exitBatch = stage.OnExitEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (exitBatch.Count > 0) {
                    var exitMethod = BuildStageBatchMethodExport(
                        entity, domain, analysis, esm, $"OnExit{stage.Name}", exitBatch);
                    entryExit[(entity.Name, stage.Name, "exit")] = exitMethod.Body!;
                }
            }
        }
        return (types, entryExit);
    }

    private static void Retarget<TKey>(
        Dictionary<TKey, Node> map,
        IReadOnlyList<(Node From, Node To)> replaced) where TKey : notnull {
        if (map.Count == 0)
            return;
        foreach (var key in map.Keys) {
            var body = map[key];
            foreach (var (from, to) in replaced) {
                if (!ReferenceEquals(body, from))
                    continue;
                map[key] = to;
                break;
            }
        }
    }

    private static MethodDefinitionNode BuildStageBatchMethodExport(
        Entity entity,
        Domain domain,
        AnalysisResult analysis,
        EntityStructureMetadata? esm,
        string methodName,
        IReadOnlyList<Effect> effects) {
        var ctx = new LoweringContext(
            new ThisReference(),
            Analysis: analysis,
            Domain: domain,
            EnumPropertyNames: esm?.EnumPropertyNames,
            LifecyclePropertyName: esm?.LifecyclePropertyName,
            LifecycleEnumTypeName: esm?.LifecycleEnumTypeName);
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

    /// <summary>
    /// Policy methods are emitted in ToSyntax, one per name. This cache only
    /// fills a name that the module did not already define, for
    /// <c>EvaluatePolicy</c>. A second definition of the same name (entity,
    /// stage or action-local) is an analysis error and ToSyntax throws on it,
    /// so the first body cached for a name is the only one.
    /// </summary>
    private static Dictionary<(string, string), Node> CompletePolicyBodies(
        Domain domain, AnalysisResult analysis,
        Dictionary<(string, string), Node> map) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            void Cache(Policy policy) {
                var key = (entity.Name, policy.Name);
                if (map.ContainsKey(key))
                    return;
                var lowered = DomainToCSharpExporter.LowerExpressionToMethodBody(
                    policy.Expression, entity, domain, analysis);
                if (lowered is not null)
                    map[key] = lowered;
            }

            foreach (var action in entity.Actions)
                foreach (var policy in action.Policies)
                    Cache(policy);
            foreach (var stage in entity.Stages) {
                foreach (var policy in stage.Policies)
                    Cache(policy);
                foreach (var action in stage.Actions)
                    foreach (var policy in action.Policies)
                        Cache(policy);
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


    /// <summary>Test hook: replace a GetOrLower-cached policy body (cache-identity oracles).</summary>
    internal static void ReplacePolicyBody(Domain domain, string entity, string policy, Node body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(body);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.PolicyBodies ??= new Dictionary<(string, string), Node>();
            holder.PolicyBodies[(entity, policy)] = body;
        }
    }

    /// <summary>Test hook: remove a cached policy body so Domain-bound EvaluatePolicy must fail closed.</summary>
    internal static void ClearPolicyBody(Domain domain, string entity, string policy) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.PolicyBodies?.Remove((entity, policy));
        }
    }

    /// <summary>Test hook: replace a GetOrLower-cached OnEntry/OnExit body.</summary>
    internal static void ReplaceEntryExitBody(
        Domain domain, string entity, string stage, string kind, Node body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(body);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.EntryExitBodies ??= new Dictionary<(string, string, string), Node>();
            holder.EntryExitBodies[(entity, stage, kind)] = body;
        }
    }

    /// <summary>Test hook: clear an OnEntry/OnExit body (fail-closed Domain-bound).</summary>
    internal static void ClearEntryExitBody(Domain domain, string entity, string stage, string kind) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.EntryExitBodies?.Remove((entity, stage, kind));
        }
    }

    /// <summary>Test hook: replace a GetOrLower-cached subscription effect body.</summary>
    internal static void ReplaceSubscriptionBody(
        Domain domain, SubscriptionDispatchPlanEntry entry, Node body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(body);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.SubscriptionBodies ??= new Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>(
                ReferenceEqualityComparer.Instance);
            if (!holder.SubscriptionBodies.TryGetValue(entry, out var byStage)
                || byStage.Count == 0)
                throw new InvalidOperationException(
                    "Subscription body is not cached; Lower the domain before replacing a handler.");
            foreach (var stage in byStage.Keys.ToList())
                byStage[stage] = body;
        }
    }

    /// <summary>Test hook: clear a subscription body (fail-closed Domain-bound).</summary>
    internal static void ClearSubscriptionBody(Domain domain, SubscriptionDispatchPlanEntry entry) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.SubscriptionBodies?.Remove(entry);
        }
    }

    /// <summary>
    /// Test hook: replace the GetOrLower-cached lowered module. Throws when no module
    /// is cached, so a test cannot overwrite a module a Lower/Emit never produced.
    /// Used by <c>EmitRefusesVmErrorsTests</c> to make Emit fail closed.
    /// </summary>
    internal static void ReplaceModule(Domain domain, IReadOnlyList<TypeDefinitionNode> module) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(module);
        var holder = GetHolder(domain);
        lock (holder) {
            if (holder.Module is null)
                throw new InvalidOperationException(
                    "Module is not cached; Lower the domain before replacing the module.");
            holder.Module = module;
        }
    }

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d =>
            new Holder { Session = DomainSession.ForExtensions(d.Extensions) });
}