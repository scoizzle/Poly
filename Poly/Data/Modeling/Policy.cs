using Poly.Data.Modeling.TypeSystem;

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
}