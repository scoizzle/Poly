using Poly.Introspection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// A host with no CLR behind it: three types in the namespace <c>ledger</c>, each its own definition. Test-only; it is
/// the second platform the type-scope work (#998) is checked against, so a .NET assumption has something to fail on.
/// </summary>
sealed class LedgerHost : ITypeSource {
    public const string Namespace = "ledger";

    public LedgerHost() {
        Amount = new LedgerDefinition("amount", new Money(MinorUnits: 2));
        Account = new LedgerDefinition("account");
        Entry = new LedgerDefinition("entry");
    }

    public ITypeDefinition Amount { get; }
    public ITypeDefinition Account { get; }
    public ITypeDefinition Entry { get; }

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        string.Equals(@namespace, Namespace, StringComparison.Ordinal) ? [Amount, Account, Entry] : [];

    /// <summary>A fact only this host states: the type counts money in minor units.</summary>
    public sealed record Money(int MinorUnits) : TypeFact;

    private sealed class LedgerDefinition(string name, Money? money = null) : ITypeDefinition {
        public TFact? GetFact<TFact>() where TFact : TypeFact => money as TFact;
        public string Name => name;
        public string? Namespace => LedgerHost.Namespace;
        public AccessModifier AccessModifier => AccessModifier.Public;
        public ITypeDefinition? BaseType => null;
        public IEnumerable<ITypeDefinition> Interfaces => [];
        public IEnumerable<IParameter> GenericParameters => [];
        public IEnumerable<ITypeMember> Members => [];
        public IEnumerable<ITypeField> Fields => [];
        public IEnumerable<ITypeProperty> Properties => [];
        public IEnumerable<ITypeMethod> Methods => [];
        public IEnumerable<ITypeConstructor> Constructors => [];
        public IEnumerable<ITypeOperator> Operators => [];
        public PrimitiveType? PrimitiveType => null;
        public ITypeDefinition? NullableOf => null;
        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}