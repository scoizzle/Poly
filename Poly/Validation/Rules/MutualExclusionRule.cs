using Poly.Interpretation;
using Poly.Interpretation.Operators;
using Poly.Interpretation.Operators.Boolean;
using Poly.Interpretation.Operators.Equality;

namespace Poly.Validation.Rules;

public sealed class MutualExclusionRule : Rule {
    public IEnumerable<string> PropertyNames { get; set; }
    public int MaxAllowed { get; set; }

    public MutualExclusionRule(IEnumerable<string> propertyNames, int maxAllowed = 1) {
        PropertyNames = propertyNames;
        MaxAllowed = maxAllowed;
    }

    public override Value BuildInterpretationTree(RuleBuildingContext context) {
        var properties = PropertyNames.ToList();
        
        if (properties.Count <= MaxAllowed) {
            return Literal.True;
        }
        
        // For now, implement simple mutual exclusion (only one can have value)
        // More complex counting logic would require arithmetic operators
        if (MaxAllowed == 1) {
            // At most one property can be non-null
            var nonNullChecks = properties
                .Select(name => new MemberAccess(context.Value, name))
                .Select(member => new NotEqual(member, Value.Null))
                .ToList();
            
            // Create pairwise exclusions: for each pair, at least one must be null
            var exclusions = new List<Value>();
            for (int i = 0; i < nonNullChecks.Count; i++) {
                for (int j = i + 1; j < nonNullChecks.Count; j++) {
                    // !(prop_i != null AND prop_j != null)
                    exclusions.Add(new Not(new And(nonNullChecks[i], nonNullChecks[j])));
                }
            }
            
            var exclusionResult = exclusions.Aggregate((current, next) => new And(current, next));
            return exclusionResult;
        }
        
        // For maxAllowed > 1, would need count aggregation
        throw new NotImplementedException("MutualExclusionRule with MaxAllowed > 1 not yet implemented");
    }

    public override string ToString() {
        return $"At most {MaxAllowed} of [{string.Join(", ", PropertyNames)}] can have values";
    }
}