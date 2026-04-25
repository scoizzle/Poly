using Poly.Data.Modeling.TypeSystem;
using Poly.Interpretation.AbstractSyntaxTree.Boolean;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Data.Modeling;

public enum PolicyAggregationStrategy {
    All,
    Any
}

/// <summary>
/// Represents a composable policy that can aggregate multiple policy rules.
/// </summary>
public sealed class Policy : IDomainObject {
    private readonly List<IPolicyRule> _rules = [];

    public required Domain Domain { get; init; }
    public required string Name { get; set; }

    public PolicyAggregationStrategy AggregationStrategy { get; init; } = PolicyAggregationStrategy.All;

    public IReadOnlyCollection<IPolicyRule> Rules => _rules.AsReadOnly();

    public void AddRule(IPolicyRule rule) {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
    }

    public Node ToInterpretationNode(Node subject) {
        ArgumentNullException.ThrowIfNull(subject);

        var nodes = _rules.Select(rule => rule.ToInterpretationNode(subject));

        return AggregationStrategy switch {
            PolicyAggregationStrategy.All => nodes.Aggregate((Node)True, (acc, node) => new And(acc, node)),
            PolicyAggregationStrategy.Any => nodes.Aggregate((Node)False, (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException("Unknown aggregation strategy.")
        };
    }
}

/// <summary>
/// A policy rule backed by an expression factory, useful for cross-property predicates.
/// </summary>
public sealed class PredicateRule : IPolicyRule {
    public required Func<Node, Node> Predicate { get; init; }

    public Node ToInterpretationNode(Node subject) {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(Predicate);

        return Predicate(subject);
    }
}