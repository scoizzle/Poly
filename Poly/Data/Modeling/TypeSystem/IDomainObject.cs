namespace Poly.Data.Modeling.TypeSystem;

public abstract record DomainObject : Node, IDomainObject {
    protected DomainObject(Domain domain) {
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
    }

    protected DomainObject() {
        if (this is Domain domain) {
            Domain = domain;
            return;
        }

        throw new InvalidOperationException("Only Domain can use the parameterless DomainObject constructor.");
    }

    public Domain Domain { get; }
    public string Name { get; protected set => field = Guard.ThrowIfNullOrEmpty(value); } = string.Empty;
    public virtual IEnumerable<DomainObject> ChildObjects => [];
    public sealed override IEnumerable<Node?> Children => ChildObjects;

    public virtual bool Equals(DomainObject? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Represents a domain, which is a logical grouping of related types and objects.
/// </summary>
public interface IDomainObject {
    /// <summary>
    /// Gets the domain to which this object belongs.
    /// </summary>
    public Domain Domain { get; }
}