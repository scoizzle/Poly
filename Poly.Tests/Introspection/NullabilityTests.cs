using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// I5: nullability is a platform-stated relation. A definition that is a host's nullable form names what it wraps,
/// so a conversion into it can be stated without the root knowing the host's nullability model.
/// </summary>
public class NullabilityTests {
    [Test]
    public async Task NeutralDefinition_StatesItsNullableForm() {
        var member = typeof(ITypeDefinition).GetProperty(
            "NullableOf", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        await Assert.That(member).IsNotNull();
    }

    [Test]
    public async Task ClrNullable_StatesWhatItWraps_AndTheConversionAgrees() {
        var registry = ClrTypeDefinitionRegistry.Shared;
        var nullableLong = (ITypeDefinition)registry.GetTypeDefinition(typeof(long?));
        var longType = (ITypeDefinition)registry.GetTypeDefinition(typeof(long));

        await Assert.That(nullableLong.NullableOf).IsSameReferenceAs(longType);
        await Assert.That(longType.NullableOf).IsNull();

        // The stated relation agrees with the conversion: Int32 -> Int64? is implicit and the platform applies it.
        var conversion = nullableLong.ResolveConversionFrom(registry.GetTypeDefinition(typeof(int)));

        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(conversion.Method).IsNull();
    }

    [Test]
    public async Task Nullability_IsThePlatformsRelationNotTheRoots() {
        // A host the root knows nothing about states its own nullable form, with no CLR type in sight.
        var underlying = new HostType("Text");
        var nullable = new HostType("Text?") { NullableOf = underlying };

        await Assert.That(nullable.NullableOf).IsSameReferenceAs(underlying);
        await Assert.That(underlying.NullableOf).IsNull();
    }

    private sealed class HostType(string name) : ITypeDefinition {
        public ITypeDefinition? NullableOf { get; init; }
        public string Name => name;
        public string? Namespace => null;
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
        bool ITypeDefinition.IsNumber => false;
        bool ITypeDefinition.IsIntegral => false;
        bool ITypeDefinition.IsSigned => false;
        bool ITypeDefinition.IsDecimal => false;
        bool ITypeDefinition.IsNativeSized => false;
        int ITypeDefinition.NumberBits => 0;
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