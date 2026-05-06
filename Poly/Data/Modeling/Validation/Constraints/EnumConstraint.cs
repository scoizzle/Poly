namespace Poly.Data.Modeling.Validation.Constraints;

public sealed class EnumConstraint : Constraint {
    public sealed record EnumMember(string Name, object? CanonicalValue = null, string? Label = null) {
        public object EffectiveCanonicalValue => CanonicalValue ?? Name;
    }

    private readonly List<EnumMember> _members;

    public EnumConstraint(IEnumerable<EnumMember> members) {
        ArgumentNullException.ThrowIfNull(members);

        _members = members.ToList();

        if (_members.Count == 0) {
            throw new ArgumentException("Enum constraint requires at least one member.", nameof(members));
        }

        var duplicateNames = _members
            .GroupBy(static member => member.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (duplicateNames.Length > 0) {
            throw new ArgumentException($"Enum constraint contains duplicate member names: {string.Join(", ", duplicateNames)}.", nameof(members));
        }

        var seenCanonicalValues = new HashSet<object?>();
        foreach (var member in _members) {
            if (!seenCanonicalValues.Add(member.EffectiveCanonicalValue)) {
                throw new ArgumentException("Enum constraint contains duplicate canonical values.", nameof(members));
            }
        }
    }

    public EnumConstraint(params EnumMember[] members) : this((IEnumerable<EnumMember>)members) { }

    public IReadOnlyCollection<EnumMember> Members => _members.AsReadOnly();

    public TypeCategory ApplicableCategories => TypeCategory.None;
}