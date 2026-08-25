namespace Poly.Introspection.CommonLanguageRuntime;

internal abstract class ClrTypeMember : ITypeMember {
    public abstract ClrTypeDefinition MemberTypeDefinition { get; }
    public abstract ClrTypeDefinition DeclaringTypeDefinition { get; }
    public abstract IEnumerable<ClrParameter> Parameters { get; }
    public abstract string Name { get; }
    public abstract AccessModifier AccessModifier { get; }
    public abstract LifetimeModifier LifetimeModifier { get; }
    public bool IsStatic => LifetimeModifier == LifetimeModifier.Static;

    public virtual Mutability Mutability => Mutability.Mutable;

    ITypeDefinition ITypeMember.MemberTypeDefinition => MemberTypeDefinition;
    ITypeDefinition ITypeMember.DeclaringTypeDefinition => DeclaringTypeDefinition;
    IEnumerable<IParameter> ITypeMember.Parameters => Parameters;

    /// <summary>
    /// Returns <c>true</c> when this member is readable. A member is considered
    /// readable when <see cref="EmitRead"/> returns a non-null expression for a
    /// non-null instance (for instance members) or null instance (for static members).
    /// </summary>
    public bool CanRead => ((ITypeMember)this).EmitRead(
        System.Linq.Expressions.Expression.Default(typeof(object))) is not null;

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

    /// <summary>
    /// Reads this member's value from <paramref name="instance"/> as an expression tree,
    /// or returns <c>null</c> if not readable. Override in derived types.
    /// </summary>
    public virtual Expression? EmitRead(Expression? instance) => null;

    /// <summary>
    /// Writes <paramref name="value"/> to this member on <paramref name="instance"/>
    /// as an expression tree, or returns <c>null</c> if not writable. Override in derived types.
    /// </summary>
    public virtual Expression? EmitWrite(Expression? instance, Expression value) => null;

    public override string ToString() => $"{MemberTypeDefinition} {DeclaringTypeDefinition}.{Name}";
}