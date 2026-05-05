namespace Poly.Data.Modeling.Recipes;

/// <summary>
/// Fluent recipe to scaffold a relationship between two entities with specified cardinality and ownership.
/// </summary>
public sealed class RelationshipScaffoldRecipe : IScaffoldRecipe {
    private readonly string _relationshipName;
    private Entity? _source;
    private Entity? _target;
    private RelationshipCardinality _cardinality = RelationshipCardinality.ManyToOne;
    private bool _sourceOwnsTarget;

    public string Name => $"Relationship[{_relationshipName}]";

    public RelationshipScaffoldRecipe(string relationshipName) {
        ArgumentNullException.ThrowIfNull(relationshipName);
        _relationshipName = relationshipName;
    }

    /// <summary>Sets the source entity.</summary>
    public RelationshipScaffoldRecipe WithSource(Entity source, bool ownsTarget = false) {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _sourceOwnsTarget = ownsTarget;
        return this;
    }

    /// <summary>Sets the target entity.</summary>
    public RelationshipScaffoldRecipe WithTarget(Entity target) {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        return this;
    }

    /// <summary>Sets the relationship cardinality.</summary>
    public RelationshipScaffoldRecipe WithCardinality(RelationshipCardinality cardinality) {
        _cardinality = cardinality;
        return this;
    }

    /// <summary>Builds the relationship into the domain via transactional mutation.</summary>
    public void BuildInto(Domain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        if (_source is null) {
            throw new InvalidOperationException($"Relationship recipe '{_relationshipName}' missing source entity. Call WithSource().");
        }
        if (_target is null) {
            throw new InvalidOperationException($"Relationship recipe '{_relationshipName}' missing target entity. Call WithTarget().");
        }

        var mutation = domain.CreateMutation();

        // Create the relationship
        var relationship = new Relationship(
            domain,
            _relationshipName,
            _source,
            _target,
            _cardinality,
            _sourceOwnsTarget
        );

        // Add relationship to domain (NOT via AddType, just AddRelationship)
        mutation.AddRelationship(relationship);

        // Register the relationship with source entity
        mutation.AddEntityRelationship(_source, relationship);

        // Apply and check for errors
        var result = mutation.Apply();
        if (result.HasErrors) {
            var errorMsg = string.Join("; ", result.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"Failed to build relationship '{_relationshipName}': {errorMsg}");
        }
    }
}