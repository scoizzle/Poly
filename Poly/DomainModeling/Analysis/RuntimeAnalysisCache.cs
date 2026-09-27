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
/// Authoring <see cref="DomainSession.Analyze"/> binds the session that loaded
/// the domain's <c>uses</c> (vendor maps included). Fallback reopen uses the
/// core catalog only when nothing has bound yet.
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
        /// <summary>OnEntry/OnExit bodies shared with module methods; execute BindThis.</summary>
        public Dictionary<(string Entity, string Stage, string Kind), Node>? EntryExitBodies { get; set; }
        /// <summary>
        /// Contiguous non-StageTransition segments of OnEntry/OnExit, lowered at GetOrLower.
        /// Mixed lists flush/recurse at execute by binding these — never LowerActionBody.
        /// </summary>
        public Dictionary<(string Entity, string Stage, string Kind, int Segment), Node>? EntryExitSegmentBodies { get; set; }
        /// <summary>Policy expression trees from session.Lower (same Lower as print).</summary>
        public Dictionary<(string Entity, string Policy), Node>? PolicyBodies { get; set; }
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
            var analysisChanged = analysis is not null && !ReferenceEquals(holder.Analysis, analysis);
            holder.Session = session;
            if (analysis is not null)
                holder.Analysis = analysis;
            if (sessionChanged || analysisChanged) {
                holder.Module = null;
                holder.SubscriptionBodies = null;
                holder.EntryExitBodies = null;
                holder.EntryExitSegmentBodies = null;
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
            var policyBodies = new Dictionary<(string, string), Node>();
            var subscriptionBodies = new Dictionary<SubscriptionDispatchPlanEntry, Dictionary<string, Node>>(
                ReferenceEqualityComparer.Instance);
            var module = DomainProgramProjection.ToSyntax(
                domain, analysis, policyBodies, subscriptionBodies);
            var (module2, entryExit, entryExitSegments) = PopulateEntryExitMethods(domain, analysis, module);
            holder.Module = module2;
            holder.SubscriptionBodies = subscriptionBodies;
            holder.EntryExitBodies = entryExit;
            holder.EntryExitSegmentBodies = entryExitSegments;
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
        Domain domain, SubscriptionDispatchPlanEntry entry, out Node? body) =>
        TryGetSubscriptionBody(domain, entry, targetStageName: null, out body);

    internal static bool TryGetSubscriptionBody(
        Domain domain, SubscriptionDispatchPlanEntry entry, string? targetStageName, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(entry);
        body = null;
        var holder = GetHolder(domain);
        if (holder.SubscriptionBodies is null
            || !holder.SubscriptionBodies.TryGetValue(entry, out var byStage)
            || byStage.Count == 0)
            return false;
        if (targetStageName is { Length: > 0 })
            return byStage.TryGetValue(targetStageName, out body) && body is not null;
        body = byStage.Values.FirstOrDefault();
        return body is not null;
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
    /// OnEntry{Stage}/OnExit{Stage} methods once. EntryExitBodies holds the same
    /// bodies; Domain-bound execute BindThis.
    /// </summary>
    private static (IReadOnlyList<TypeDefinitionNode> Module,
        Dictionary<(string, string, string), Node> EntryExit,
        Dictionary<(string, string, string, int), Node> EntryExitSegments)
        PopulateEntryExitMethods(
            Domain domain, AnalysisResult analysis, IReadOnlyList<TypeDefinitionNode> module) {
        var types = module.ToList();
        var entryExit = new Dictionary<(string, string, string), Node>();
        var entryExitSegments = new Dictionary<(string, string, string, int), Node>();
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
                // Whole-batch EntryExitBodies exclude nested StageTransitionEffect (no-nested
                // TransitionStage path). Segments cache contiguous non-ST fragments for the
                // mixed flush/recurse path — execute binds those, never LowerActionBody.
                var entryBatch = stage.OnEntryEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (entryBatch.Count > 0) {
                    var name = $"OnEntry{stage.Name}";
                    var entryMethod = BuildStageBatchMethodExport(
                        entity, domain, analysis, esm, name, entryBatch);
                    entryExit[(entity.Name, stage.Name, "entry")] = entryMethod.Body!;
                    if (existingNames.Add(name)) {
                        extras ??= [];
                        extras.Add(entryMethod);
                    }
                }
                CacheEntryExitSegments(
                    entryExitSegments, entity, domain, analysis, esm,
                    stage.Name, "entry", stage.OnEntryEffects);

                var exitBatch = stage.OnExitEffects
                    .Where(e => e is not StageTransitionEffect).ToList();
                if (exitBatch.Count > 0) {
                    var name = $"OnExit{stage.Name}";
                    var exitMethod = BuildStageBatchMethodExport(
                        entity, domain, analysis, esm, name, exitBatch);
                    entryExit[(entity.Name, stage.Name, "exit")] = exitMethod.Body!;
                    if (existingNames.Add(name)) {
                        extras ??= [];
                        extras.Add(exitMethod);
                    }
                }
                CacheEntryExitSegments(
                    entryExitSegments, entity, domain, analysis, esm,
                    stage.Name, "exit", stage.OnExitEffects);
            }

            if (extras is null)
                continue;
            types[i] = td with { Methods = [.. td.Methods ?? [], .. extras] };
        }
        return (types, entryExit, entryExitSegments);
    }

    /// <summary>
    /// Lower nonempty contiguous non-<see cref="StageTransitionEffect"/> segments of an
    /// OnEntry/OnExit list. Mixed lists flush these at execute via
    /// <see cref="TryGetEntryExitSegmentBody"/>. Lists with no StageTransitionEffect
    /// skip segment cache — execute uses EntryExitBodies / module methods already.
    /// </summary>
    private static void CacheEntryExitSegments(
        Dictionary<(string, string, string, int), Node> map,
        Entity entity,
        Domain domain,
        AnalysisResult analysis,
        EntityStructureMetadata? esm,
        string stageName,
        string kind,
        IReadOnlyList<Effect> effects) {
        // No ST → no mixed flush path; avoid unused seg0 twin of EntryExitBodies.
        if (effects.All(e => e is not StageTransitionEffect))
            return;
        var segmentIndex = 0;
        var batch = new List<Effect>();
        void Flush() {
            if (batch.Count == 0) return;
            var method = BuildStageBatchMethodExport(
                entity, domain, analysis, esm,
                $"On{(kind == "entry" ? "Entry" : "Exit")}{stageName}_Seg{segmentIndex}",
                batch);
            map[(entity.Name, stageName, kind, segmentIndex)] = method.Body!;
            segmentIndex++;
            batch.Clear();
        }
        foreach (var effect in effects) {
            if (effect is StageTransitionEffect) {
                Flush();
            }
            else {
                batch.Add(effect);
            }
        }
        Flush();
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

    internal static bool TryGetEntryExitSegmentBody(
        Domain domain, string entityName, string stageName, string kind, int segmentIndex, out Node? body) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        if (holder.EntryExitSegmentBodies is not null
            && holder.EntryExitSegmentBodies.TryGetValue(
                (entityName, stageName, kind, segmentIndex), out body)
            && body is not null)
            return true;
        body = null;
        return false;
    }

    /// <summary>
    /// Entity-level policies are collected during ToSyntax (one Lower). Action- and
    /// stage-scoped policies are not module methods — lower them with the same
    /// <see cref="DomainToCSharpExporter.LowerExpressionToMethodBody"/> the exporter uses.
    /// </summary>
    private static Dictionary<(string, string), Node> CompletePolicyBodies(
        Domain domain, AnalysisResult analysis,
        Dictionary<(string, string), Node> map) {
        foreach (var entity in domain.Types.OfType<Entity>()) {
            void Cache(Policy policy) {
                if (map.ContainsKey((entity.Name, policy.Name)))
                    return;
                var lowered = DomainToCSharpExporter.LowerExpressionToMethodBody(
                    policy.Expression, entity, domain, analysis);
                if (lowered is not null)
                    map[(entity.Name, policy.Name)] = lowered;
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

    /// <summary>Test hook: clear a mixed-list OnEntry/OnExit segment body.</summary>
    internal static void ClearEntryExitSegmentBody(
        Domain domain, string entity, string stage, string kind, int segmentIndex) {
        ArgumentNullException.ThrowIfNull(domain);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.EntryExitSegmentBodies?.Remove((entity, stage, kind, segmentIndex));
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
                || byStage.Count == 0) {
                holder.SubscriptionBodies[entry] = new Dictionary<string, Node>(StringComparer.Ordinal) {
                    [""] = body
                };
                return;
            }
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

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d => {
            var ids = d.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
            return new Holder { Session = DomainSession.ForExtensions(ids) };
        });
}