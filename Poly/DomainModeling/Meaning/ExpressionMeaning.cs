using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Per-session meaning tables for library-owned expressions. Empty when the unit
/// did not load the owning library — pack IR then fails closed at rewrite/lower/check.
/// </summary>
public sealed class ExpressionMeaning {
    public static ExpressionMeaning Empty { get; } = new();

    public ExpressionDispatchRegistry<DomainExpression> Rewrite { get; } = new();

    public ExpressionLoweringRegistry Lowering { get; } = new();

    /// <summary>Catalog type name for a library expression (e.g. DateTime, Date, Duration).</summary>
    public ExpressionDispatchRegistry<string> Inference { get; } = new();

    public ExpressionTypeCheckRegistry Checks { get; } = new();

    public ExpressionDefaultResolverRegistry Defaults { get; } = new();

    private readonly List<IAssignConversionAdvisor> _assignConversions = [];

    public void RegisterAssignConversion(IAssignConversionAdvisor advisor) {
        ArgumentNullException.ThrowIfNull(advisor);
        _assignConversions.Add(advisor);
    }

    public bool TryAdviseAssignConversion(
        AnalysisContext context,
        AssignEffect assign,
        ExpressionTypeCheckScope scope) {
        foreach (var advisor in _assignConversions) {
            if (advisor.TryAdvise(context, assign, scope))
                return true;
        }
        return false;
    }

    public bool TryClaimAssign(
        AnalysisContext context,
        DomainExpression value,
        string targetTypeName,
        ExpressionTypeCheckScope scope) {
        foreach (var advisor in _assignConversions) {
            if (advisor.TryClaimAssign(context, value, targetTypeName, scope))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Library-owned assign conversion (e.g. Date onto DateTime). Core analysis
/// consults this instead of naming library IR.
/// </summary>
public interface IAssignConversionAdvisor {
    bool TryAdvise(AnalysisContext context, AssignEffect assign, ExpressionTypeCheckScope scope);

    /// <summary>
    /// True when this library owns assign-RHS compatibility for
    /// <paramref name="value"/> onto <paramref name="targetTypeName"/>
    /// (e.g. Now onto Date). Core Compatible is skipped.
    /// </summary>
    bool TryClaimAssign(
        AnalysisContext context,
        DomainExpression value,
        string targetTypeName,
        ExpressionTypeCheckScope scope) => false;
}