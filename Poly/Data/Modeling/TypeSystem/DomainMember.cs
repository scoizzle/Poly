namespace Poly.Data.Modeling.TypeSystem;

public abstract record DomainMember : DomainObject {
    protected DomainMember(string name) : base() {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public DomainMember(Domain domain, string name) : base(domain) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the name of the type.
    /// </summary>
    public string Name { get; protected set => field = Guard.ThrowIfNullOrEmpty(value); } = string.Empty;
}