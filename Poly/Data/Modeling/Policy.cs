using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;

namespace Poly.Data.Modeling;

public enum PolicyAggregationStrategy {
    All,
    Any
}

/// <summary>
/// Represents a composable policy that can aggregate multiple policy rules.
/// </summary>
public sealed partial record Policy : DomainMember {
    internal readonly List<Rule> _rules = [];

    public Policy(Domain domain, string name) : base(domain, name) {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public PolicyAggregationStrategy AggregationStrategy { get; init; } = PolicyAggregationStrategy.All;

    public IReadOnlyCollection<Rule> Rules => _rules.AsReadOnly();
    public sealed override IEnumerable<DomainMember> ChildObjects => [.. _rules];

    public Node ToInterpretationNode(Node subject) {
        ArgumentNullException.ThrowIfNull(subject);

        var nodes = Rules.Select(rule => rule switch {
            PredicateRule predicateRule => predicateRule.ToInterpretationNode(subject),
            _ => rule.Constraints.ToInterpretationNode(subject)
        });

        return AggregationStrategy switch {
            PolicyAggregationStrategy.All => nodes.Aggregate((Node)True, (acc, node) => new And(acc, node)),
            PolicyAggregationStrategy.Any => nodes.Aggregate((Node)False, (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException("Unknown aggregation strategy.")
        };
    }
}