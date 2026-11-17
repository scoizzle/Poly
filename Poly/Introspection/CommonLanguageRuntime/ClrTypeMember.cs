namespace Poly.Introspection.CommonLanguageRuntime;

internal abstract class ClrTypeMember : ITypeMember {
    public abstract ClrTypeDefinition MemberTypeDefinition { get; }
    public abstract ClrTypeDefinition DeclaringTypeDefinition { get; }
    public abstract IEnumerable<ClrParameter> Parameters { get; }
    public abstract string Name { get; }
    public abstract MemberKind Kind { get; }
    public abstract AccessModifier AccessModifier { get; }
    public abstract LifetimeModifier LifetimeModifier { get; }
    public bool IsStatic => LifetimeModifier == LifetimeModifier.Static;

    public virtual Mutability Mutability => Mutability.Mutable;

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberTypeDefinition;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringTypeDefinition;
    IEnumerable<IParameter> ITypeMember.Parameters => Parameters;

    /// <summary>
    /// Returns <c>true</c> when this member is readable. A CLR field is readable;
    /// a property answers from its getter (<see cref="ClrTypeProperty.CanRead"/>).
    /// </summary>
    public virtual bool CanRead => Kind == MemberKind.Field;

    /// <summary>
    /// Returns <c>true</c> when this member can be written after construction.
    /// Returns <c>false</c> for readonly/init-only members and compile-time constants.
    /// </summary>
    public bool CanWrite => (Mutability & Mutability.ReadOnlyAfterInit) != Mutability.ReadOnlyAfterInit;

    /// <summary>
    /// Returns <c>true</c> when the member can be written during initialization
    /// (readonly fields, init-only setters, etc.).
    /// </summary>
    public bool CanInitialize => (Mutability & Mutability.ReadOnlyAfterInit) == Mutability.ReadOnlyAfterInit;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}";
}