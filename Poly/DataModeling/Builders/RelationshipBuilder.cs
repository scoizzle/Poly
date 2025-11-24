namespace Poly.DataModeling.Builders;

using Poly.Validation;

public sealed class RelationshipBuilder {
    internal enum SourceCardinality {
        One,
        Many
    }

    private readonly string _sourceTypeName;
    private readonly string? _sourcePropertyName;
    private readonly SourceCardinality _sourceCardinality;
    private readonly List<Constraint> _sourceConstraints;
    private readonly List<Constraint> _targetConstraints;
    
    private string? _targetTypeName;
    private string? _targetPropertyName;
    private bool? _targetIsMany;

    internal RelationshipBuilder(string sourceTypeName, string? sourcePropertyName, SourceCardinality sourceCardinality) {
        ArgumentNullException.ThrowIfNull(sourceTypeName);
        _sourceTypeName = sourceTypeName;
        _sourcePropertyName = sourcePropertyName;
        _sourceCardinality = sourceCardinality;
        _sourceConstraints = [];
        _targetConstraints = [];
    }

    public RelationshipBuilder OfType(string targetTypeName) {
        ArgumentNullException.ThrowIfNull(targetTypeName);
        _targetTypeName = targetTypeName;
        return this;
    }

    public RelationshipBuilder WithOne(string? targetPropertyName = null) {
        if (_targetTypeName == null) {
            throw new InvalidOperationException("Must call OfType before WithOne.");
        }
        _targetPropertyName = targetPropertyName;
        _targetIsMany = false;
        return this;
    }

    public RelationshipBuilder WithMany(string? targetPropertyName = null) {
        if (_targetTypeName == null) {
            throw new InvalidOperationException("Must call OfType before WithMany.");
        }
        _targetPropertyName = targetPropertyName;
        _targetIsMany = true;
        return this;
    }

    public RelationshipBuilder WithSourceConstraint(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        _sourceConstraints.Add(constraint);
        return this;
    }

    public RelationshipBuilder WithSourceConstraints(params Constraint[] constraints) {
        ArgumentNullException.ThrowIfNull(constraints);
        _sourceConstraints.AddRange(constraints);
        return this;
    }

    public RelationshipBuilder WithTargetConstraint(Constraint constraint) {
        ArgumentNullException.ThrowIfNull(constraint);
        _targetConstraints.Add(constraint);
        return this;
    }

    public RelationshipBuilder WithTargetConstraints(params Constraint[] constraints) {
        ArgumentNullException.ThrowIfNull(constraints);
        _targetConstraints.AddRange(constraints);
        return this;
    }

    internal Relationship Build() {
        if (_targetTypeName == null) {
            throw new InvalidOperationException("Target type name must be set. Call OfType.");
        }
        if (_targetIsMany == null) {
            throw new InvalidOperationException("Target cardinality must be set. Call WithOne or WithMany.");
        }

        var sourceEnd = new RelationshipEnd(
            _sourceTypeName,
            _sourcePropertyName,
            _sourceConstraints);

        var targetEnd = new RelationshipEnd(
            _targetTypeName,
            _targetPropertyName,
            _targetConstraints);

        var relationshipName = _sourcePropertyName ?? _targetPropertyName ?? $"{_sourceTypeName}_{_targetTypeName}";

        return (_sourceCardinality, _targetIsMany.Value) switch {
            (SourceCardinality.One, false) => new OneToOneRelationship(relationshipName, sourceEnd, targetEnd),
            (SourceCardinality.One, true) => new OneToManyRelationship(relationshipName, sourceEnd, targetEnd),
            (SourceCardinality.Many, false) => new ManyToOneRelationship(relationshipName, sourceEnd, targetEnd),
            (SourceCardinality.Many, true) => new ManyToManyRelationship(relationshipName, sourceEnd, targetEnd),
            _ => throw new InvalidOperationException($"Unknown cardinality combination: {_sourceCardinality}, {_targetIsMany}")
        };
    }
}
