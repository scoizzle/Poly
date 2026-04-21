using Poly.Interpretation.AbstractSyntaxTree.Boolean;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Data.Modeling.Validation;

public enum ConstraintAggregationStrategy {
    /// <summary>
    /// All constraints must be satisfied (logical AND).
    /// </summary>
    All,

    /// <summary>
    /// At least one constraint must be satisfied (logical OR).
    /// </summary>
    Any
}

public sealed class ConstraintSet : Constraint {
    public ConstraintSet(ConstraintAggregationStrategy aggregationStrategy, IEnumerable<Constraint> constraints) {
        ArgumentNullException.ThrowIfNull(constraints);

        var materializedConstraints = constraints.ToArray();

        AggregationStrategy = aggregationStrategy;
        Constraints = materializedConstraints;
        ApplicableCategories = DetermineApplicableCategories(materializedConstraints);
    }

    public ConstraintSet(params Constraint[] constraints) : this(ConstraintAggregationStrategy.All, constraints) { }

    public TypeCategory ApplicableCategories { get; }

    public ConstraintAggregationStrategy AggregationStrategy { get; }

    public IReadOnlyList<Constraint> Constraints { get; }

    private static TypeCategory DetermineApplicableCategories(IReadOnlyList<Constraint> constraints) {
        TypeCategory? sharedCategories = null;

        foreach (var constraint in constraints) {
            var applicableCategories = constraint.ApplicableCategories;

            if (applicableCategories == TypeCategory.None) {
                continue;
            }

            sharedCategories = sharedCategories is null
                ? applicableCategories
                : sharedCategories.Value & applicableCategories;

            if (sharedCategories == TypeCategory.None) {
                throw new ArgumentException(
                    "All constraints in a constraint set must share at least one applicable type category.",
                    nameof(constraints));
            }
        }

        return sharedCategories ?? TypeCategory.None;
    }

    public Node ToInterpretationNode(Node value) {
        var nodes = Constraints.Select(c => c.ToInterpretationNode(value));

        return AggregationStrategy switch {
            ConstraintAggregationStrategy.All => nodes.Aggregate((Node)True, (acc, node) => new And(acc, node)),
            ConstraintAggregationStrategy.Any => nodes.Aggregate((Node)False, (acc, node) => new Or(acc, node)),
            _ => throw new InvalidOperationException("Unknown aggregation strategy.")
        };
    }
}