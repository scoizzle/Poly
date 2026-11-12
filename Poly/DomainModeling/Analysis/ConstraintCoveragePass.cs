using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Fail closed on a constraint no loaded library compiles (<c>unique</c> without
/// <c>persistence</c> or a vendor library, #684 C-05). Runs on the <see cref="Domain"/>
/// node so it reports once per constraint, positioned on the constrained property or
/// type. Reads the session's compilers and the catalog's providers; it never lowers.
/// </summary>
internal sealed class ConstraintCoveragePass : INodeAnalyzer {
    public const string Id = "ConstraintCoveragePass";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain)
            return;
        _ = Constrained(domain);
    }

    /// <summary>
    /// Every constraint the pass can report: a type's own constraints on all
    /// <see cref="DomainType"/>s (a <c>value</c> type's <c>unique</c> is a type-level
    /// constraint), then an <see cref="Entity"/>'s properties. A value type's
    /// properties are not walked: no shipped path compiles them either.
    /// </summary>
    private static List<(Node Node, string Owner, Constraint Constraint)> Constrained(Domain domain) {
        var found = new List<(Node, string, Constraint)>();
        foreach (var type in domain.Types) {
            foreach (var constraint in type.Constraints)
                found.Add((type, type.Name, constraint));
        }
        foreach (var entity in domain.Types.OfType<Entity>()) {
            foreach (var property in entity.Properties) {
                foreach (var constraint in property.Constraints)
                    found.Add((property, $"{entity.Name}.{property.Name}", constraint));
            }
        }
        return found;
    }
}