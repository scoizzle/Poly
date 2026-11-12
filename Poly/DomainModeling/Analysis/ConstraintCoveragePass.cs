using Poly.Analysis;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Fail closed on a constraint no loaded library compiles (<c>unique</c> without
/// <c>persistence</c> or a vendor library, #684 C-05). Runs on the <see cref="Domain"/>
/// node so it reports once per constraint, positioned on the constrained property or
/// type. Reads the session's compilers and the catalog's providers; it never lowers.
/// <para>
/// The verdict never reads <see cref="ExtensionCatalog.ProvidersOf"/>: that answers the
/// message hint. A catalog whose probe throws is a hintless finding, never a crash.
/// </para>
/// </summary>
internal sealed class ConstraintCoveragePass : INodeAnalyzer {
    public const string Id = "ConstraintCoveragePass";
    public string PassName => Id;

    public void Analyze(AnalysisContext context, Node node) {
        if (node is not Domain domain)
            return;
        var constrained = Constrained(domain);
        if (constrained.Count == 0)
            return;
        var session = RuntimeAnalysisCache.Session(domain);
        foreach (var (subject, owner, constraint) in constrained) {
            if (!ConstraintCoverage.Uncovered(constraint, session.ConstraintCompilers))
                continue;
            context.ReportError(
                subject,
                ConstraintCoverage.Message(owner, constraint, session.Options.Catalog),
                DomainModelDiagnosticCodes.ConstraintNoCompiler);
        }
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

/// <summary>
/// The one rule for "is this constraint compiled": a writer kind or a documented marker
/// needs no library, and anything else needs a loaded <see cref="IConstraintCompiler"/>
/// whose <see cref="IConstraintCompiler.Applies"/> is true. Analysis reports the miss and
/// lowering refuses it, so both read the same answer.
/// </summary>
internal static class ConstraintCoverage {
    /// <summary>True when no loaded compiler claims <paramref name="constraint"/> and no row marks it.</summary>
    public static bool Uncovered(Constraint constraint, IReadOnlyList<IConstraintCompiler> compilers) {
        ArgumentNullException.ThrowIfNull(constraint);
        ArgumentNullException.ThrowIfNull(compilers);
        if (Claimed(constraint))
            return false;
        foreach (var compiler in compilers) {
            if (compiler.Applies(constraint))
                return false;
        }
        return true;
    }

    /// <summary>True when the writer table already answers for this record: a kind, or a marker.</summary>
    public static bool Claimed(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        return ConstraintCheckWriter.CoverageByConstraint.TryGetValue(constraint.GetType(), out var coverage)
            && (coverage.Kinds.Count > 0 || coverage.Marker);
    }

    /// <summary>
    /// The finding's message: the constraint, its owner, and the libraries in
    /// <paramref name="catalog"/> that could compile it. The catalog answers a hint only, so a
    /// throwing probe degrades to no hint instead of failing the analysis.
    /// </summary>
    public static string Message(string owner, Constraint constraint, ExtensionCatalog catalog) {
        ArgumentNullException.ThrowIfNull(catalog);
        var head = $"Constraint '{ConstraintValidation.Describe(constraint)}' on {owner} has no compiler";
        IReadOnlyList<string> providers;
        try {
            providers = catalog.ProvidersOf(constraint);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            return head + ".";
        }
        if (providers.Count == 0)
            return head + ": no library in this session's catalog compiles it.";
        var hints = providers.Select(id => $"`uses {id}`").ToList();
        var list = hints.Count == 1
            ? hints[0]
            : string.Join(", ", hints.Take(hints.Count - 1)) + " or " + hints[hints.Count - 1];
        return head + $": add {list}.";
    }
}