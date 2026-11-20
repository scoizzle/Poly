namespace Poly.Interpretation.Analysis.Semantics;

/// <summary>
/// The namespaces a scope imports with no <c>using</c> directive (#998 S2b, owner Q2): the analysis' question, which a
/// language answers through <see cref="ITypeSystem.Rule{TRule}"/>. C# answers the SDK's implicit global usings; a system
/// that does not answer imports nothing, so a bare name binds only through a unit's own directives.
/// </summary>
internal interface IImplicitUsings : ITypeSystemRule {
    /// <summary>The imported namespaces, in import order.</summary>
    IReadOnlyList<string> Namespaces { get; }
}