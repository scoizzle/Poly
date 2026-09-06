using Poly.Ast.Nodes;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Session + analysis + lowered module for a <see cref="Domain"/> instance.
/// Authoring <see cref="DomainSession.Analyze"/> binds the session that loaded
/// the domain's <c>uses</c> (vendor maps included). Fallback reopen uses the
/// core catalog only when nothing has bound yet.
/// <see cref="GetOrLower"/> caches the operation module
/// (<see cref="DomainProgramProjection.ToSyntax"/>). Named actions live as
/// <see cref="MethodDefinitionNode.Body"/> on the entity
/// <see cref="TypeDefinitionNode"/>.
/// </summary>
internal static class RuntimeAnalysisCache {
    private sealed class Holder {
        public required DomainSession Session { get; set; }
        public AnalysisResult? Analysis { get; set; }
        public IReadOnlyList<TypeDefinitionNode>? Module { get; set; }
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
            if (sessionChanged || analysisChanged)
                holder.Module = null;
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
            holder.Module ??= DomainProgramProjection.ToSyntax(domain, analysis);
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

    private static IEnumerable<string> EntryMethodNames(string stageName) {
        yield return $"OnEntry{stageName}";
        yield return $"{stageName}OnEntry";
        yield return "OnEntry";
    }

    private static Holder GetHolder(Domain domain) =>
        Cache.GetValue(domain, static d => {
            var ids = d.Extensions.Where(ExtensionCatalog.Core.Contains).ToList();
            return new Holder { Session = DomainSession.ForExtensions(ids) };
        });
}
