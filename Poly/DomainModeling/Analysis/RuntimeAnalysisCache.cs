using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Session + analysis + lowered module for a <see cref="Domain"/> instance.
/// Authoring <see cref="DomainSession.Analyze"/> binds the session that loaded
/// the domain's <c>uses</c> (vendor maps included). Fallback reopen uses the
/// core catalog only when nothing has bound yet.
/// <see cref="GetOrLower"/> also caches runtime-shaped named action / OnEntry
/// trees; invoke looks them up instead of re-lowering Effect IR.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; set; }
        public AnalysisResult? Analysis { get; set; }
        public IReadOnlyList<TypeDefinitionNode>? Module { get; set; }
        public Dictionary<string, Node?>? Operations { get; set; }
        public bool RuntimeOperationsReady { get; set; }
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
                holder.Operations = null;
                holder.RuntimeOperationsReady = false;
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
        if (holder.Module is not null && holder.RuntimeOperationsReady)
            return holder.Module;
        lock (holder) {
            holder.Module ??= DomainProgramProjection.ToSyntax(domain, analysis);
            EnsureRuntimeOperations(holder, domain, analysis);
            return holder.Module;
        }
    }

    internal static string ActionKey(string entity, string action, string? stage) =>
        $"{entity}\0action\0{action}\0{stage}";

    internal static string EntryKey(string entity, string stage) =>
        $"{entity}\0entry\0{stage}";

    internal static void ReplaceOperation(Domain domain, string key, Node tree) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(tree);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.Operations ??= new Dictionary<string, Node?>(StringComparer.Ordinal);
            holder.Operations[key] = tree;
        }
    }

    internal static bool TryGetOperation(Domain domain, string key, out Node? tree) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(key);
        var holder = GetHolder(domain);
        lock (holder) {
            if (holder.Operations is not null && holder.Operations.TryGetValue(key, out tree))
                return true;
        }
        tree = null;
        return false;
    }

    public static Node? GetOrLowerOperation(Domain domain, string key, Func<Node?> lower) {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(lower);
        var holder = GetHolder(domain);
        lock (holder) {
            holder.Operations ??= new Dictionary<string, Node?>(StringComparer.Ordinal);
            if (holder.Operations.TryGetValue(key, out var cached))
                return cached;
            var tree = lower();
            holder.Operations[key] = tree;
            return tree;
        }
    }

    private static void EnsureRuntimeOperations(Holder holder, Domain domain, AnalysisResult analysis) {
        if (holder.RuntimeOperationsReady)
            return;
        holder.Operations ??= new Dictionary<string, Node?>(StringComparer.Ordinal);
        foreach (var entity in domain.Types.OfType<Entity>()) {
            var actionNames = entity.Actions.Select(a => a.Name)
                .Concat(entity.Stages.SelectMany(s => s.Actions.Select(a => a.Name)))
                .Distinct(StringComparer.Ordinal);
            var stages = entity.Stages.Select(s => (string?)s.Name).Append(null);
            foreach (var stage in stages) {
                foreach (var name in actionNames) {
                    if (!analysis.TryResolveAction(domain, entity, stage, name, out var action)
                        || action is null
                        || action.Effects.Count == 0)
                        continue;
                    AddMissing(holder, ActionKey(entity.Name, action.Name, stage),
                        () => LowerAction(entity, action, stage, analysis, domain));
                }
            }

            foreach (var stage in entity.Stages) {
                var entryEffects = stage.OnEntryEffects
                    .Where(e => e is not StageTransitionEffect)
                    .ToList();
                if (entryEffects.Count == 0)
                    continue;
                AddMissing(holder, EntryKey(entity.Name, stage.Name),
                    () => LowerEntry(entity, entryEffects, analysis, domain));
            }
        }
        holder.RuntimeOperationsReady = true;
    }

    private static void AddMissing(Holder holder, string key, Func<Node?> lower) {
        if (holder.Operations!.ContainsKey(key))
            return;
        holder.Operations[key] = lower();
    }

    private static Node? LowerAction(
        Entity entity, Action action, string? stage, AnalysisResult analysis, Domain domain) {
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            Domain: domain,
            SourceStageName: stage,
            ActionParameterNames: action.Parameters.Count > 0
                ? action.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                : null);
        return new EffectLoweringPass(entity, context).LowerActionBody(action.Effects);
    }

    private static Node? LowerEntry(
        Entity entity, IReadOnlyList<Effect> entryEffects, AnalysisResult analysis, Domain domain) {
        var context = new LoweringContext(
            new Parameter("entity", new TypeReference(entity.Name)),
            Analysis: analysis,
            Domain: domain);
        return new EffectLoweringPass(entity, context).LowerActionBody(entryEffects);
    }

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d => {
            var ids = d.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
            return new Holder { Session = DomainSession.ForExtensions(ids) };
        });
}
