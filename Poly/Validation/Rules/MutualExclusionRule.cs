namespace Poly.Validation.Rules;

public sealed class MutualExclusionRule : Rule {
    public IEnumerable<string> PropertyNames { get; set; }
    public int MaxAllowed { get; set; }

    public MutualExclusionRule(IEnumerable<string> propertyNames, int maxAllowed = 1) {
        PropertyNames = propertyNames;
        MaxAllowed = maxAllowed;
    }

    public override Node BuildInterpretationTree(RuleBuildingContext context) {
        var properties = PropertyNames.ToList();

        if (properties.Count <= MaxAllowed) {
            return True;
        }

        // General mutual exclusion: at most MaxAllowed can be non-null.
        // Generate combinations of (MaxAllowed + 1) properties — each combination
        // being all non-null simultaneously would violate the constraint.
        var nonNullChecks = properties
            .Select(name => new Member(context.Value, name))
            .Select(member => new NotEqual(member, Null))
            .ToList();

        if (MaxAllowed >= nonNullChecks.Count)
            return True;

        // Generate all combinations of size (MaxAllowed + 1)
        var exclusions = new List<Node>();
        var indices = new int[MaxAllowed + 1];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        while (true) {
            // NOT (all checks in this combination are non-null simultaneously)
            var conjunction = (Node)nonNullChecks[indices[0]];
            for (int i = 1; i < indices.Length; i++)
                conjunction = new And(conjunction, nonNullChecks[indices[i]]);
            exclusions.Add(new Not(conjunction));

            // Advance to next combination
            int p = indices.Length - 1;
            while (p >= 0 && indices[p] == nonNullChecks.Count - (indices.Length - p))
                p--;
            if (p < 0) break;
            indices[p]++;
            for (int i = p + 1; i < indices.Length; i++)
                indices[i] = indices[i - 1] + 1;
        }

        return exclusions.Aggregate((current, next) => new And(current, next));
    }

    public override string ToString() {
        return $"At most {MaxAllowed} of [{string.Join(", ", PropertyNames)}] can have values";
    }
}