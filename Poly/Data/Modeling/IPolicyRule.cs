using Poly.Syntax;

namespace Poly.Data.Modeling;

/// <summary>
/// Represents a single evaluatable clause within a policy.
/// </summary>
public interface IPolicyRule {
    /// <summary>
    /// Builds an interpretation tree for this policy clause.
    /// </summary>
    /// <param name="subject">The policy subject node (typically an entity instance).</param>
    /// <returns>A boolean interpretation node for this clause.</returns>
    public Node ToInterpretationNode(Node subject);
}