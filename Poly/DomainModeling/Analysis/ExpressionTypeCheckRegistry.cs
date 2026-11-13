using Poly.DomainModeling.Ontology;

namespace Poly.DomainModeling.Analysis;

/// <summary>
/// Registered semantic check for a pack-owned <see cref="DomainExpression"/> subtype,
/// consulted by <see cref="ExpressionTypeAnalyzer"/> during expression walking and default
/// validation. The pack registers a handler per type it owns so core analysis never names
/// pack IR. Handlers run with the analyzer's context and may report diagnostics. They do
/// not recurse — the analyzer's generic child walk continues after a handled check.
/// </summary>
public interface IExpressionTypeCheck {
    /// <summary>The concrete expression type this handler checks.</summary>
    Type ExpressionType { get; }

    /// <summary>
    /// Validates <paramref name="expression"/> (of <see cref="ExpressionType"/>).
    /// <paramref name="scope"/> carries the entity property map, optional action parameters,
    /// resolved enum types, and — when checking a <c>default(...)</c> — the target property's
    /// type name.
    /// </summary>
    void Check(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope);
}

/// <summary>
/// Where the analyzer reached a pack-owned expression, stamped on the node by the walk sites that know it (an assign
/// value, a create initializer, a comparison operand). <see cref="Default"/> is every other position, and a
/// <c>default(...)</c> (see <see cref="ExpressionTypeCheckScope.DefaultTargetTypeName"/>).
/// </summary>
public enum CheckSite { Default, Assign, Operand }

/// <summary>
/// Context bundle for a registered expression type check. Mirrors the surface the analyzer
/// walks; <see cref="DefaultTargetTypeName"/> is non-null when the check is a
/// <c>default(...)</c> compatibility validation against a target property. A stamped
/// <see cref="Site"/> names the position and <see cref="SiteTypeName"/> the type the value is assigned to or
/// compared with. <see cref="IsOptional"/> answers whether a property name is declared optional.
/// </summary>
public readonly record struct ExpressionTypeCheckScope(
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyDictionary<string, string>? Parameters,
    IReadOnlyDictionary<string, EnumType> EnumTypes,
    string? DefaultTargetTypeName = null,
    CheckSite Site = CheckSite.Default,
    string? SiteTypeName = null,
    Func<string, bool>? IsOptional = null);

/// <summary>
/// Session registry of pack-owned expression type checks.
/// </summary>
public sealed class ExpressionTypeCheckRegistry {
    private readonly List<IExpressionTypeCheck> _checks = [];

    public void Register(IExpressionTypeCheck check) {
        ArgumentNullException.ThrowIfNull(check);
        if (_checks.Any(c => c.ExpressionType == check.ExpressionType)) {
            throw new InvalidOperationException(
                $"Duplicate expression type check for '{check.ExpressionType.Name}'.");
        }
        _checks.Add(check);
    }

    /// <summary>
    /// Runs the registered check for <paramref name="expression"/>. Returns true when a
    /// handler claimed the type.
    /// </summary>
    public bool TryCheck(
        AnalysisContext context,
        DomainExpression expression,
        ExpressionTypeCheckScope scope) {
        foreach (var check in _checks) {
            if (check.ExpressionType.IsInstanceOfType(expression)) {
                check.Check(context, expression, scope);
                return true;
            }
        }
        return false;
    }
}