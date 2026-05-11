namespace Poly.Data.Modeling.TypeSystem;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;

public abstract record DomainObject(Domain Domain) : Node {
    private readonly List<string> _comments = new();
    public IReadOnlyList<string> Comments => _comments.AsReadOnly();

    /// <summary>
    /// Appends a new comment/description to this domain object. Comments are append-only and preserve authoring history.
    /// </summary>
    internal void AddComment(string comment) {
        if (!string.IsNullOrWhiteSpace(comment))
            _comments.Add(comment);
    }

    internal void RemoveCommentAt(int index) => _comments.RemoveAt(index);

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

    protected override bool PrintMembers(StringBuilder builder) {
        base.PrintMembers(builder);
        builder.Append($", Domain = {Domain?.Name ?? "(null)"}");
        if (_comments.Count > 0)
            builder.Append($", Comments = [{string.Join("; ", _comments)}]");
        return true;
    }
}