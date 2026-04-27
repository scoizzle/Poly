namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class LengthConstraint(int? minLength = default, int? maxLength = default) : Constraint {
    public int? MinLength { get; set; } = minLength;
    public int? MaxLength { get; set; } = maxLength;

    /// <summary>
    /// Length constraints apply to Text and Collection types.
    /// </summary>
    public TypeCategory ApplicableCategories => TypeCategory.Text | TypeCategory.Collection | TypeCategory.Binary;

    public Node ToInterpretationNode(Node value) {
        var length = value.GetMember("Length");

        var minCheck = MinLength.HasValue
            ? new GreaterThanOrEqual(length, Wrap(MinLength.Value))
            : null;

        var maxCheck = MaxLength.HasValue
            ? new LessThanOrEqual(length, Wrap(MaxLength.Value))
            : null;

        var lengthCheck = (minCheck, maxCheck) switch {
            (Node min, Node max) => new And(min, max),
            (Node min, null) => min,
            (null, Node max) => max,
            _ => Wrap(true)
        };

        return lengthCheck;
    }
}