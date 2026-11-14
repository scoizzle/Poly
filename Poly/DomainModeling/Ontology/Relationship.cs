namespace Poly.DomainModeling.Ontology;

/// <summary>
/// Defines the cardinality options for relationships between entities.
/// </summary>
public enum RelationshipCardinality {
    OneToOne,
    OneToMany,
    ManyToOne,
    ManyToMany
}

/// <summary>
/// Represents a named relationship between two entity types.
/// 
/// Relationships are first-class members of a <see cref="Domain"/> and can carry their own properties.
/// They are distinct from owned structures (which are typically modeled via owned value types or composition).
/// </summary>
public sealed record Relationship(
    string Name,
    DomainTypeReference Source,
    DomainTypeReference Target,
    RelationshipCardinality Cardinality,
    IReadOnlyList<Property> Properties
) : DomainMember(Name) {
    public bool SourceOwnsTarget { get; init; }

    /// <summary>
    /// The inverse navigation name declared with <c>via</c>, or null when the navigation
    /// declares none. Syntax and IR only: pairing validity is analysis (S-11).
    /// </summary>
    public string? Inverse { get; init; }

    /// <summary>
    /// True when the navigation is declared <c>optional</c>. A bare to-one navigation is required at
    /// create (L-15a); an owned single part must be declared optional, and a collection cannot be.
    /// This is the declaration's fact, not the null-tolerant read analysis gives a navigation inside
    /// a link's condition.
    /// </summary>
    public bool IsOptional { get; init; }
    public IReadOnlyList<Stage> Stages { get; init; } = [];
    public IReadOnlyList<Policy> Policies { get; init; } = [];
    public sealed override IEnumerable<Node?> Children => [Source, Target, .. Properties, .. Stages, .. Policies];

    /// <summary>
    /// Message when a OneToOne or ManyToOne source already has an outbound link.
    /// </summary>
    public static string LinkViolationMessage(string relationshipName) =>
        $"Cannot add a second link on '{relationshipName}': the source already has a target.";

    /// <summary>
    /// Message when Unlink would drop the last owner of an owned target.
    /// </summary>
    public static string UnlinkViolationMessage(string relationshipName) =>
        $"Cannot unlink '{relationshipName}': the owned target must keep its owner.";

    /// <summary>
    /// Message when Unlink would clear a required to-one navigation. A required link is permanent in
    /// this release: delete the instance and create a new one to move it.
    /// </summary>
    public static string RequiredUnlinkViolationMessage(string relationshipName) =>
        $"Cannot unlink '{relationshipName}': the navigation is required; delete the instance and create a new one to move it.";
}