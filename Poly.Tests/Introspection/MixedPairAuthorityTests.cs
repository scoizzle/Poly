using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// The target type system decides a mixed pair (#998 T3; owner ruling 2 on
/// docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md §2.3). It reads the operators and
/// conversions both types declare and applies its own rules; a foreign definition's own answer never decides for it.
/// Formerly (I4) the left operand's definition answered an operator and the destination's a conversion, so a foreign
/// definition that answers anything decided every pair it stood on the deciding side of.
/// </summary>
public class MixedPairAuthorityTests {
    private static readonly ITypeSystem Target = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;

    [Test]
    public async Task Operator_ForeignOperandOnTheRight_TheTargetFindsTheOperatorItDeclares() {
        var third = new ThirdPlatformDefinition("Third");
        var clrLong = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        await Assert.That(Target.Operator(OperatorKind.Add, clrLong, third)).IsSameReferenceAs(third.AddOperator)
            .Because("Third declares +(long, Third): a declaration on either operand is a candidate");
    }

    [Test]
    public async Task Operator_ForeignOperandOnTheLeft_TheTargetDecides_NotTheForeignDefinition() {
        var third = new ThirdPlatformDefinition("Third");
        var clrLong = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        await Assert.That(third.System.Operator(OperatorKind.Add, third, clrLong)).IsSameReferenceAs(third.AddOperator)
            .Because("the foreign platform's own system answers any Add; that must not decide (#998 T9: no door on the definition)");
        await Assert.That(Target.Operator(OperatorKind.Add, third, clrLong)).IsNull()
            .Because("+(long, Third) does not apply to (Third, long), and C# has no other candidate");
    }

    [Test]
    public async Task Operator_ForeignParameterType_ApplicabilityIsTheTargetsConversion_NotTheParameterTypes() {
        // Third also declares +(Third, Third). Its own door turns any source into Third with no method, so asking the
        // parameter type would make the foreign system decide one level down; the target's conversion rule does not.
        var third = new ThirdPlatformDefinition("Third", declaresSelfAddition: true);
        var clrLong = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        await Assert.That(Target.Operator(OperatorKind.Add, third, clrLong)).IsNull();
        await Assert.That(Target.Operator(OperatorKind.Add, third, third)).IsSameReferenceAs(third.SelfAddition)
            .Because("identical operands need no conversion");
    }

    [Test]
    public async Task Operator_UnaryOnAForeignOperand_TheTargetDecides() {
        var third = new ThirdPlatformDefinition("Third");

        await Assert.That(Target.Operator(OperatorKind.UnaryMinus, third, null)).IsNull()
            .Because("Third declares no unary operator and C# has none for it");
    }

    [Test]
    public async Task Operator_ModeledLeftOperand_ForeignRight_TheTargetFindsTheRightsDeclaration() {
        var third = new ThirdPlatformDefinition("Third");
        ITypeDefinition modeled = new Poly.Interpretation.Analysis.Semantics.AstTypeDefinition(
            new Poly.Ast.Nodes.TypeDefinitionNode("Modeled"), new Poly.Interpretation.Analysis.Semantics.AstTypeRegistry());

        await Assert.That(Target.Operator(OperatorKind.Add, modeled, third)).IsNull()
            .Because("+(long, Third) needs a long on the left; a modeled class does not convert to long");
    }

    [Test]
    public async Task Conversion_IntoAForeignDefinition_TheTargetDecides_NotTheDestination() {
        var third = new ThirdPlatformDefinition("Third");
        var clrLong = (ITypeDefinition)ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long));

        await Assert.That(third.System.Conversion(clrLong, third)).IsSameReferenceAs(third.ImplicitConversion)
            .Because("the foreign platform's own system answers any source; that must not decide (#998 T9)");
        await Assert.That(Target.Conversion(clrLong, third)).IsNull()
            .Because("Third declares no conversion C# can apply (a C# user-defined conversion is a method)");
        await Assert.That(Target.Conversion(third, clrLong)).IsNull();
    }

    /// <summary>A definition from a platform the root knows nothing about; its own system answers its identity,
    /// operator and conversion.</summary>
    private sealed class ThirdPlatformDefinition : ITypeDefinition {
        public ThirdPlatformDefinition(string name, bool declaresSelfAddition = false) {
            Name = name;
            AddOperator = new ThirdOperator(this);
            SelfAddition = declaresSelfAddition ? new ThirdSelfAddition(this) : null;
            ImplicitConversion = new ThirdConversion(this);
        }

        public ITypeOperator? SelfAddition { get; }

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
        public IEnumerable<ITypeOperator> Operators => SelfAddition is null ? [AddOperator] : [AddOperator, SelfAddition];

        public ITypeSystem System => ThirdPlatform.Instance;

        IEnumerable<ITypeConversion> ITypeDefinition.Conversions => [ImplicitConversion];
    }

    /// <summary>The third platform's own system: it answers any Add with the left operand's operator and turns any
    /// source into one of its definitions with no method, which is what makes the deciding side observable.</summary>
    private sealed class ThirdPlatform : ITypeSystem {
        public static readonly ThirdPlatform Instance = new();
        public ITypeSystem? Target => null;
        public IReadOnlyList<ITypeSource> Sources => [];
        public ITypeDefinition? Resolve(string qualifiedName) => null;
        public ITypeDefinition? Bind(string word) => null;
        public string? Spell(ITypeDefinition definition) => null;
        public bool IsSame(ITypeDefinition a, ITypeDefinition b) => ReferenceEquals(a, b);
        public ITypeDefinition? NullableOf(ITypeDefinition definition) => null;
        public ITypeDefinition? ElementType(ITypeDefinition definition) => null;
        public bool IsAssignableWithoutConversion(ITypeDefinition destination, ITypeDefinition source) => IsSame(destination, source);
        public ITypeConversion? Conversion(ITypeDefinition source, ITypeDefinition destination) =>
            destination is ThirdPlatformDefinition third ? third.ImplicitConversion : null;
        public ITypeOperator? Operator(OperatorKind kind, ITypeDefinition left, ITypeDefinition? right) =>
            kind == OperatorKind.Add && left is ThirdPlatformDefinition third ? third.AddOperator : null;
        public TRule? Rule<TRule>() where TRule : class, ITypeSystemRule => null;
    }

    // +(long, Third) -> Third: the right operand's type declares it.
    private sealed class ThirdOperator(ThirdPlatformDefinition declaring) : ITypeOperator {
        public OperatorKind Kind => OperatorKind.Add;
        public ITypeDefinition DeclaringTypeDefinition => declaring;
        public IEnumerable<IParameter> Parameters => [
            new Operand(0, "left", ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(long))),
            new Operand(1, "right", declaring),
        ];
        public ITypeDefinition ResultTypeDefinition => declaring;
        public ITypeMethod? Method => null;
    }

    // +(Third, Third) -> Third.
    private sealed class ThirdSelfAddition(ThirdPlatformDefinition declaring) : ITypeOperator {
        public OperatorKind Kind => OperatorKind.Add;
        public ITypeDefinition DeclaringTypeDefinition => declaring;
        public IEnumerable<IParameter> Parameters => [new Operand(0, "left", declaring), new Operand(1, "right", declaring)];
        public ITypeDefinition ResultTypeDefinition => declaring;
        public ITypeMethod? Method => null;
    }

    private sealed record Operand(int Position, string Name, ITypeDefinition ParameterTypeDefinition) : IParameter {
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }

    private sealed class ThirdConversion(ThirdPlatformDefinition declaring) : ITypeConversion {
        public ConversionOperatorKind Kind => ConversionOperatorKind.Implicit;
        public ITypeDefinition SourceTypeDefinition => declaring;
        public ITypeDefinition ResultTypeDefinition => declaring;
        public ITypeMethod? Method => null;
    }
}