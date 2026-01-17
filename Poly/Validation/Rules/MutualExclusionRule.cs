using Poly.Interpretation;
using Poly.Interpretation.Expressions;

namespace Poly.Validation.Rules;

public sealed class MutualExclusionRule : Rule {
    public IEnumerable<string> PropertyNames { get; set; }
    public int MaxAllowed { get; set; }

    public MutualExclusionRule(IEnumerable<string> propertyNames, int maxAllowed = 1)
    {
        PropertyNames = propertyNames;
        MaxAllowed = maxAllowed;
    }

    public override Interpretable BuildInterpretationTree(RuleBuildingContext context)
    {
        var properties = PropertyNames.ToList();

        if (properties.Count <= MaxAllowed) {
            return new Constant(true);
        }

        // For now, implement simple mutual exclusion (only one can have value)
        // More complex counting logic would require arithmetic operators
        if (MaxAllowed == 1) {
            // At most one property can be non-null
            var nonNullChecks = properties
                .Select(name => {
                    var property = context.TypeDefinition.Properties.FirstOrDefault(p => p.Name == name)
                        ?? throw new InvalidOperationException($"Property '{name}' not found on type '{context.TypeDefinition.Name}'.");
                    return new MemberAccess(context.Value, property.Name);
                })
                .Select(member => new BinaryOperation(BinaryOperationKind.NotEqual, member, new Constant(null)))
                .ToList();

            // Create pairwise exclusions: for each pair, at least one must be null
            var exclusions = new List<Interpretable>();
            for (int i = 0; i < nonNullChecks.Count; i++) {
                for (int j = i + 1; j < nonNullChecks.Count; j++) {
                    // !(prop_i != null AND prop_j != null)
                    exclusions.Add(
                        new UnaryOperation(
                            UnaryOperationKind.Not,
                            new BinaryOperation(BinaryOperationKind.And, nonNullChecks[i], nonNullChecks[j])
                        )
                    );
                }
            }

            var exclusionResult = exclusions.Aggregate((current, next) => new BinaryOperation(BinaryOperationKind.And, current, next));
            return exclusionResult;
        }

        // For maxAllowed > 1, would need count aggregation
        throw new NotImplementedException("MutualExclusionRule with MaxAllowed > 1 not yet implemented");
    }

    public override string ToString()
    {
        return $"At most {MaxAllowed} of [{string.Join(", ", PropertyNames)}] can have values";
    }
}