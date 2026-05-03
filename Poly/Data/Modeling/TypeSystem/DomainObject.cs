namespace Poly.Data.Modeling.TypeSystem;

public abstract record DomainObject(Domain Domain) : Node {
    protected DomainObject() : this(default!) {
        if (this is Domain domain) {
            Domain = domain;
            return;
        }

        throw new InvalidOperationException("Only Domain can use the parameterless DomainObject constructor.");
    }

    public virtual IEnumerable<DomainObject> ChildObjects => [];
    public sealed override IEnumerable<Node?> Children => ChildObjects;

    public virtual bool Equals(DomainObject? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    protected override bool PrintMembers(System.Text.StringBuilder builder) {
        base.PrintMembers(builder);
        builder.Append($", Domain = {Domain?.Name ?? "(null)"}");
        return true;
    }
}