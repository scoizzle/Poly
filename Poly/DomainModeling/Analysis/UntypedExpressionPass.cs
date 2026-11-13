using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// One place where a consumer met a value the expression analyzer could not type (T-05b, lock #679 6845700954, A1):
/// <see cref="Expression"/> is the untyped value (null when the untyped thing is the consumer's declared target type),
/// <see cref="Consumer"/> the node that needed its type, <see cref="TypeName"/> a declared type name the type model
/// cannot resolve (A2), and <see cref="Reason"/> why the analyzer has no type.
/// </summary>
internal sealed record UntypedSite(DomainExpression? Expression, Node Consumer, string? TypeName, string Reason);

/// <summary>The untyped sites <see cref="ExpressionTypeAnalyzer"/> stamped in this run, on the <see cref="Domain"/> node.</summary>
internal sealed class UntypedSitesMetadata : IAnalysisMetadata {
    private readonly HashSet<(Node?, Node)> _seen = [];

    public List<UntypedSite> Sites { get; } = [];

    public void Add(UntypedSite site) {
        if (_seen.Add((site.Expression, site.Consumer)))
            Sites.Add(site);
    }
}

/// <summary>
/// The closing pass for the strict gate (T-05b, A1): an expression the analyzer could not type is never compatible.
/// <see cref="ExpressionTypeAnalyzer"/> only stamps each untyped consuming site; this pass runs after
/// <see cref="SubscriptionAnalyzer"/>, when the passes that report unknown names, contract calls and effect shapes
/// have run, and reports DMSEM006 for a site only when no error lies on the expression, anywhere in its subtree, on
/// the consumer or on a node between them; for an untyped declared type, only when no error lies on a declaration of
/// that type name (A2). Sites are taken smallest subtree first, so an inner report silences an outer one.
/// </summary>
internal sealed class UntypedExpressionPass : INodeAnalyzer {
    public const string Id = "UntypedExpression";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain) return;
        if (context.GetMetadata<UntypedSitesMetadata>(domain) is not { Sites.Count: > 0 } stamped) return;

        var errorNodes = new HashSet<Node>(context.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Node), ReferenceEqualityComparer.Instance);
        var reported = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var site in stamped.Sites.OrderBy(s => Size(s.Expression))) {
            if (site.Expression is { } expression) {
                if (reported.Contains(expression) || Explained(errorNodes, site.Consumer, expression)) continue;
                if (site.TypeName is { } typeName && DeclarationHasError(domain, errorNodes, typeName)) continue;
            }
            else if (errorNodes.Contains(site.Consumer)
                || site.TypeName is null
                || DeclarationHasError(domain, errorNodes, site.TypeName)) {
                continue;
            }
            Node at = site.Expression ?? site.Consumer;
            context.ReportError(at, site.Reason, DomainModelDiagnosticCodes.SemanticUntypedExpression);
            errorNodes.Add(at);
            reported.Add(at);
        }
    }

    private static int Size(Node? node) => node is null ? int.MaxValue : 1 + node.Children.Sum(c => c is null ? 0 : Size(c));

    /// <summary>An error on the expression or its subtree, on the consumer, or on a node between them.</summary>
    private static bool Explained(HashSet<Node> errorNodes, Node consumer, DomainExpression expression) {
        if (SubtreeHasError(errorNodes, expression)) return true;
        if (errorNodes.Contains(consumer)) return true;
        var path = new List<Node>();
        return PathTo(consumer, expression, path) && path.Any(errorNodes.Contains);
    }

    private static bool SubtreeHasError(HashSet<Node> errorNodes, Node node) =>
        errorNodes.Contains(node) || node.Children.Any(c => c is not null && SubtreeHasError(errorNodes, c));

    /// <summary>The nodes strictly between <paramref name="from"/> and <paramref name="target"/>.</summary>
    private static bool PathTo(Node from, Node target, List<Node> path) {
        foreach (var child in from.Children) {
            if (child is null) continue;
            if (ReferenceEquals(child, target)) return true;
            path.Add(child);
            if (PathTo(child, target, path)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }

    /// <summary>A2: an error already sits on a property, parameter or function that declares <paramref name="typeName"/>.</summary>
    private static bool DeclarationHasError(Domain domain, HashSet<Node> errorNodes, string typeName) {
        bool Declares(Property p) => string.Equals(p.Type.TypeName, typeName, StringComparison.Ordinal);
        bool Has(Node n) => SubtreeHasError(errorNodes, n);
        foreach (var type in domain.Types) {
            IEnumerable<Property> properties = type switch {
                Entity e => e.Properties
                    .Concat(e.Actions.SelectMany(a => a.Parameters))
                    .Concat(e.Stages.SelectMany(s => s.Actions).SelectMany(a => a.Parameters)),
                Ontology.ValueType v => v.Properties,
                _ => [],
            };
            if (properties.Any(p => Declares(p) && Has(p))) return true;
        }
        return domain.Functions.Any(f =>
            (string.Equals(f.ReturnType.TypeName, typeName, StringComparison.Ordinal) || f.Parameters.Any(Declares))
            && errorNodes.Contains(f));
    }
}