using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// I4: the contract's shape decides which platform answers a mixed pair. An operator is asked of the left operand's
/// definition, a conversion of the destination's, so each pair asserts the answering side directly instead of relying
/// on the CLR's own rule happening to agree.
/// </summary>
public class MixedPairAuthorityTests {
    [Test]
    public async Task Operator_MixedPair_TheLeftOperandsPlatformAnswers() {
        var third = new ThirdPlatformDefinition("Third");
        var clr = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        var askedOfThird = third.GetOperator(OperatorKind.Add, clr);
        var askedOfClr = clr.GetOperator(OperatorKind.Add, third);

        await Assert.That(askedOfThird).IsSameReferenceAs(third.AddOperator);
        await Assert.That(askedOfClr).IsNull();
    }

    [Test]
    public async Task Conversion_MixedPair_TheDestinationsPlatformAnswers() {
        var third = new ThirdPlatformDefinition("Third");
        var clr = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        var intoThird = ((ITypeDefinition)third).ResolveConversionFrom(clr);
        var intoClr = clr.ResolveConversionFrom(third);

        await Assert.That(intoThird).IsSameReferenceAs(third.ImplicitConversion);
        await Assert.That(intoClr).IsNull();
    }

    /// <summary>A definition from a platform the root knows nothing about: it answers its own identity, operator and
    /// conversion, which is what makes the answering side observable.</summary>
    private sealed class ThirdPlatformDefinition : ITypeDefinition {
        public ThirdPlatformDefinition(string name) {
            Name = name;
            AddOperator = new ThirdOperator(this);
            ImplicitConversion = new ThirdConversion(this);
        }

        public ITypeOperator AddOperator { get; }
        public ITypeConversion ImplicitConversion { get; }

        public string Name { get; }
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
        public IEnumerable<ITypeOperator> Operators => [AddOperator];
        public ITypeDefinition? NullableOf => null;

        public bool IsSameTypeAs(ITypeDefinition other) => ReferenceEquals(this, other);

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [ImplicitConversion];
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

        ITypeOperator? ITypeDefinition.ResolveOperator(OperatorKind kind, ITypeDefinition? right) =>
            kind == OperatorKind.Add ? AddOperator : null;

        ITypeConversion? ITypeDefinition.ResolveConversionFrom(ITypeDefinition source) => ImplicitConversion;
    }

    private sealed class ThirdOperator(ThirdPlatformDefinition declaring) : ITypeOperator {
        public OperatorKind Kind => OperatorKind.Add;
        public ITypeDefinition DeclaringTypeDefinition => declaring;
        public IEnumerable<IParameter> Parameters => [];
        public ITypeDefinition ResultTypeDefinition => declaring;
        public ITypeMethod? Method => null;
    }

    private sealed class ThirdConversion(ThirdPlatformDefinition declaring) : ITypeConversion {
        public ConversionOperatorKind Kind => ConversionOperatorKind.Implicit;
        public ITypeDefinition SourceTypeDefinition => declaring;
        public ITypeDefinition ResultTypeDefinition => declaring;
        public ITypeMethod? Method => null;
    }
}