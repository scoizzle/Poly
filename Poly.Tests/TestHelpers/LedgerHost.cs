using Poly.Introspection;

namespace Poly.Tests.TestHelpers;

/// <summary>
/// A type system with no CLR behind it (#998 T2): three types in the namespace <c>ledger</c>, each its own definition,
/// no words of its own, one operator of its own (<c>amount + amount</c>), and one rule of its own
/// (<see cref="ILedgerRules"/>). Test-only; it is the second type system the type-system work is checked against, so a
/// .NET or C# assumption has something to fail on. It is a base system: it is layered on nothing.
/// </summary>
sealed class LedgerHost : ITypeSource, ITypeSystem, LedgerHost.ILedgerRules {
    public const string Namespace = "ledger";

    public LedgerHost() {
        Amount = new LedgerDefinition(this, "amount", minorUnits: 2);
        Account = new LedgerDefinition(this, "account");
        Entry = new LedgerDefinition(this, "entry");
    }

    public ITypeDefinition Amount { get; }
    public ITypeDefinition Account { get; }
    public ITypeDefinition Entry { get; }

    public IEnumerable<ITypeDefinition> TypesIn(string? @namespace) =>
        string.Equals(@namespace, Namespace, StringComparison.Ordinal) ? [Amount, Account, Entry] : [];

    /// <summary>A question only the ledger answers, declared by its owner and asked of the system: how many minor units
    /// a type counts money in, or null when it is not money.</summary>
    public interface ILedgerRules : ITypeSystemRule {
        int? MinorUnits(ITypeDefinition definition);
    }

    // ── The type system ──

    public ITypeSystem? Target => null;
    public IReadOnlyList<ITypeSource> Sources => [this];

    public ITypeDefinition? Resolve(string qualifiedName) =>
        TypesIn(Namespace).FirstOrDefault(type => string.Equals(type.FullName, qualifiedName, StringComparison.Ordinal));

    public ITypeDefinition? Bind(string word) => null;
    public string? Spell(ITypeDefinition definition) => null;
    public bool IsSame(ITypeDefinition a, ITypeDefinition b) => ReferenceEquals(a, b);
    public ITypeDefinition? NullableOf(ITypeDefinition definition) => null;
    public ITypeDefinition? ElementType(ITypeDefinition definition) => null;

    public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) =>
        IsSame(destination, source);

    public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) => null;

    /// <summary>The ledger's one operator: two amounts add to an amount. Anything else, a foreign operand included, has
    /// no ledger operator.</summary>
    public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) =>
        kind == OperatorKind.Add && ReferenceEquals(left, Amount) && right is not null && ReferenceEquals(right, Amount)
            ? new AmountAddition(Amount)
            : null;

    public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => this as TRule;

    public int? MinorUnits(ITypeDefinition definition) =>
        definition is LedgerDefinition own && TypesIn(Namespace).Contains(definition) ? own.MinorUnitsCounted : null;

    private sealed class AmountAddition(ITypeDefinition amount) : ITypeOperator {
        public OperatorKind Kind => OperatorKind.Add;
        public ITypeDefinition DeclaringTypeDefinition => amount;
        public IEnumerable<IParameter> Parameters => [new Operand(0, "left", amount), new Operand(1, "right", amount)];
        public ITypeDefinition ResultTypeDefinition => amount;
        public ITypeMethod? Method => null;
    }

    private sealed record Operand(int Position, string Name, ITypeDefinition ParameterTypeDefinition) : IParameter {
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }

    private sealed class LedgerDefinition(LedgerHost host, string name, int? minorUnits = null) : ITypeDefinition {
        public ITypeSystem System => host;
        public int? MinorUnitsCounted => minorUnits;
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
        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [];
    }
}