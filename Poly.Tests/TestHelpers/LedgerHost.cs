using Poly.Introspection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// A host with no CLR behind it: three types in the namespace <c>ledger</c>, each its own definition. Test-only; it is
/// the second platform the type-scope work (#998) is checked against, so a .NET assumption has something to fail on.
/// </summary>
sealed class LedgerHost : ITypeSource {
    public const string Namespace = "ledger";

    public LedgerHost() {
        Amount = new LedgerDefinition("amount", minorUnits: 2);
        Account = new LedgerDefinition("account");
        Entry = new LedgerDefinition("entry");
    }

    public ITypeDefinition Amount { get; }
    public ITypeDefinition Account { get; }
    public ITypeDefinition Entry { get; }

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        string.Equals(@namespace, Namespace, StringComparison.Ordinal) ? [Amount, Account, Entry] : [];

    /// <summary>A question only this host answers, on its own definitions: how many minor units the type counts money
    /// in, or null when it is not money. No other platform is asked it, so it is not on the neutral contract.</summary>
    public interface ILedgerType : ITypeDefinition {
        int? MinorUnits { get; }
    }

    private sealed class LedgerDefinition(string name, int? minorUnits = null) : ILedgerType {
        public int? MinorUnits => minorUnits;
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
        public ITypeDefinition? NullableOf => null;
        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
        bool ITypeDefinition.IsCollectionShape => false;
        bool ITypeDefinition.IsEnumeration => false;
        bool ITypeDefinition.HasValueSemantics => false;
        bool ITypeDefinition.IsInterface => false;
        bool ITypeDefinition.IsDelegate => false;
        bool ITypeDefinition.IsBoolean => false;
        bool ITypeDefinition.IsText => false;
        bool ITypeDefinition.IsCharacter => false;
        bool ITypeDefinition.IsNumber => minorUnits is not null;
        bool ITypeDefinition.IsIntegral => false;
        bool ITypeDefinition.IsSigned => minorUnits is not null;
        bool ITypeDefinition.IsDecimal => minorUnits is not null;
        bool ITypeDefinition.IsNativeSized => false;
        int ITypeDefinition.NumberBits => minorUnits is null ? 0 : 96;
        bool ITypeDefinition.IsTemporal => false;
        bool ITypeDefinition.HasDate => false;
        bool ITypeDefinition.HasTime => false;
        bool ITypeDefinition.HasOffset => false;
        bool ITypeDefinition.IsDuration => false;
        bool ITypeDefinition.IsUniqueIdentifier => false;
        bool ITypeDefinition.IsBinary => false;
        bool ITypeDefinition.IsTopType => false;
        ITypeDefinition? ITypeDefinition.SequenceElementType => null;
        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) => null;
        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => null;
    }
}