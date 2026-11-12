using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using OntologyValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>Operand definitions shared by the operator tests and the cross-check ledger.</summary>
internal static class OperatorOperands {
    public static Domain BuildDomain() => DomainTestFactory.Create("Shop", [
        new EnumType("Status", ["Open", "Closed"], []),
        new EnumType("Priority", ["Low", "High"], []),
        new Entity("Order", [new Property("Total", new DomainTypeReference("Decimal"), [])], [], [], []),
        new Entity("Customer", [new Property("Name", new DomainTypeReference("Text"), [])], [], [], []),
        new OntologyValueType("Money", [new Property("Amount", new DomainTypeReference("Decimal"), [])], []),
        new OntologyValueType("Weight", [new Property("Grams", new DomainTypeReference("Decimal"), [])], [])
    ], []);

    public static DomainTypeModel Model() {
        var maps = new TypeMappingRegistry();
        TemporalTypeMaps.Apply(maps);
        return new DomainTypeModel(BuildDomain(), maps);
    }

    public static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
}

/// <summary>Modeled definitions declared by hand, so a test controls every operator and conversion member.</summary>
internal sealed class ModeledTypeSet : ITypeDefinitionProvider {
    private readonly Dictionary<string, AstTypeDefinition> _types = new(StringComparer.Ordinal);

    public AstTypeDefinition Add(string name, params MethodDefinitionNode[] methods) {
        var definition = new AstTypeDefinition(new TypeDefinitionNode(name, Kind: TypeKind.Struct, Methods: methods), this);
        _types[name] = definition;
        return definition;
    }

    public ITypeDefinition? GetTypeDefinition(string name) => _types.GetValueOrDefault(name);

    public ITypeDefinition? GetTypeDefinition(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    /// <summary>A static member: <c>static result name(parameters)</c>.</summary>
    public static MethodDefinitionNode Static(string name, string result, params string[] parameters) =>
        new(name, new NamedTypeReference(result),
            [.. parameters.Select((type, index) => new Parameter($"p{index}", new NamedTypeReference(type)))],
            IsStatic: true);
}

/// <summary>
/// T-03b: <c>GetOperator</c> over the Introspection interface. Operands come from the domain type model, so
/// the aliases (Number is Int64, Float is Float64) are the ones the language uses.
/// </summary>
public class OperatorLookupTests {
    private static readonly DomainTypeModel Model = OperatorOperands.Model();

    private static ITypeDefinition Def(string name) => Model.Resolve(name).Definition!;

    private static OperatorResolution? Op(string left, OperatorKind kind, string? right) =>
        Def(left).GetOperator(kind, right is null ? null : Def(right));

    private static ITypeDefinition[] ParametersOf(OperatorResolution resolution) =>
        [.. resolution.Signature.Parameters.Select(static p => p.ParameterTypeDefinition)];

    private static async Task AssertResult(OperatorResolution? resolution, ITypeDefinition expected) {
        await Assert.That(resolution).IsNotNull();
        await Assert.That(resolution!.Value.Result).IsSameReferenceAs(expected);
    }

    // ---- numeric family ----

    [Test]
    public async Task Number_Div_Number_IsInt64IntegerDivision() {
        var resolution = Op("Number", OperatorKind.Divide, "Number");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(long)));
        await Assert.That(resolution!.Value.PlatformApplied).IsTrue();
    }

    [Test]
    public async Task Decimal_Plus_Number_ResolvesDecimalOperator() {
        var resolution = Op("Decimal", OperatorKind.Add, "Number");
        var decimalType = OperatorOperands.Clr(typeof(decimal));
        await AssertResult(resolution, decimalType);
        await Assert.That(resolution!.Value.PlatformApplied).IsFalse();
        await Assert.That(resolution.Value.Signature.DeclaringTypeDefinition).IsSameReferenceAs(decimalType);
        var parameters = ParametersOf(resolution.Value);
        await Assert.That(parameters.Length).IsEqualTo(2);
        await Assert.That(parameters[0]).IsSameReferenceAs(decimalType);
        await Assert.That(parameters[1]).IsSameReferenceAs(decimalType);
    }

    [Test]
    public async Task Decimal_Plus_Number_RequiresImplicitNumberToDecimalOperandConversion() {
        // R6: the conversion is a statement the tree makes. The interface names it; the ABI must not invent it.
        var resolution = Op("Decimal", OperatorKind.Add, "Number")!.Value;
        var operands = new[] { Def("Decimal"), Def("Number") };
        var parameters = ParametersOf(resolution);
        await Assert.That(ReferenceEquals(parameters[0], operands[0])).IsTrue();
        var conversion = parameters[1].ResolveConversionFrom(operands[1]);
        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Value.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
    }

    [Test]
    public async Task Number_Plus_Decimal_ResolvesDecimalOperator() {
        var resolution = Op("Number", OperatorKind.Add, "Decimal");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(decimal)));
        await Assert.That(resolution!.Value.PlatformApplied).IsFalse();
    }

    [Test]
    public async Task Decimal_Times_Decimal_IsDecimal() =>
        await AssertResult(Op("Decimal", OperatorKind.Multiply, "Decimal"), OperatorOperands.Clr(typeof(decimal)));

    [Test]
    public async Task Float64_Plus_Float64_IsFloat64() =>
        await AssertResult(Op("Float", OperatorKind.Add, "Float"), OperatorOperands.Clr(typeof(double)));

    [Test]
    public async Task Float64_Plus_Decimal_IsNone() {
        await Assert.That(Op("Float", OperatorKind.Add, "Decimal")).IsNull();
        await Assert.That(Op("Decimal", OperatorKind.Add, "Float")).IsNull();
    }

    [Test]
    public async Task Float64_Times_Decimal_IsNone() {
        await Assert.That(Op("Float", OperatorKind.Multiply, "Decimal")).IsNull();
        await Assert.That(Op("Decimal", OperatorKind.Multiply, "Float")).IsNull();
    }

    [Test]
    public async Task Int64_Plus_Float64_IsFloat64() {
        await AssertResult(Op("Number", OperatorKind.Add, "Float"), OperatorOperands.Clr(typeof(double)));
        await AssertResult(Op("Float", OperatorKind.Add, "Number"), OperatorOperands.Clr(typeof(double)));
    }

    [Test]
    public async Task Int32_Plus_Int64_IsInt64() {
        await AssertResult(Op("Int32", OperatorKind.Add, "Number"), OperatorOperands.Clr(typeof(long)));
        await AssertResult(Op("Number", OperatorKind.Add, "Int32"), OperatorOperands.Clr(typeof(long)));
    }

    [Test]
    public async Task Int32_Plus_Int32_IsInt32() =>
        await AssertResult(Op("Int32", OperatorKind.Add, "Int32"), OperatorOperands.Clr(typeof(int)));

    [Test]
    [Arguments(OperatorKind.Equal)]
    [Arguments(OperatorKind.NotEqual)]
    [Arguments(OperatorKind.LessThan)]
    [Arguments(OperatorKind.LessThanOrEqual)]
    [Arguments(OperatorKind.GreaterThan)]
    [Arguments(OperatorKind.GreaterThanOrEqual)]
    public async Task NumericComparisons_AreBoolean(OperatorKind kind) {
        await AssertResult(Op("Number", kind, "Number"), OperatorOperands.Clr(typeof(bool)));
        await AssertResult(Op("Number", kind, "Float"), OperatorOperands.Clr(typeof(bool)));
        await AssertResult(Op("Decimal", kind, "Number"), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    public async Task Float32_Add_IsNone() {
        var single = OperatorOperands.Clr(typeof(float));
        await Assert.That(single.GetOperator(OperatorKind.Add, single)).IsNull();
        await Assert.That(single.GetOperator(OperatorKind.Multiply, single)).IsNull();
        // The CLR declares Single's comparisons itself; they are a declared member, never a built-in rule.
        var comparison = single.GetOperator(OperatorKind.LessThan, single);
        await AssertResult(comparison, OperatorOperands.Clr(typeof(bool)));
        await Assert.That(comparison!.Value.PlatformApplied).IsFalse();
        // The sibling: the closed set still answers for Float64.
        await AssertResult(Op("Float", OperatorKind.Add, "Float"), OperatorOperands.Clr(typeof(double)));
    }

    // ---- text and boolean ----

    [Test]
    public async Task Text_Plus_Text_IsText() {
        var resolution = Op("Text", OperatorKind.Add, "Text");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(string)));
        await Assert.That(resolution!.Value.PlatformApplied).IsTrue();
    }

    [Test]
    public async Task Text_LessThan_Text_IsNone() {
        await Assert.That(Op("Text", OperatorKind.LessThan, "Text")).IsNull();
        await Assert.That(Op("Text", OperatorKind.Subtract, "Text")).IsNull();
        // The sibling: text equality is defined.
        await AssertResult(Op("Text", OperatorKind.Equal, "Text"), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    public async Task Boolean_Ordering_IsNone() {
        await Assert.That(Op("Boolean", OperatorKind.LessThan, "Boolean")).IsNull();
        await Assert.That(Op("Boolean", OperatorKind.Add, "Boolean")).IsNull();
        await AssertResult(Op("Boolean", OperatorKind.Equal, "Boolean"), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    [Arguments(OperatorKind.Equal)]
    [Arguments(OperatorKind.NotEqual)]
    [Arguments(OperatorKind.And)]
    [Arguments(OperatorKind.Or)]
    public async Task Boolean_Binary_IsBoolean(OperatorKind kind) =>
        await AssertResult(Op("Boolean", kind, "Boolean"), OperatorOperands.Clr(typeof(bool)));

    [Test]
    public async Task Boolean_Not_IsUnary() {
        await AssertResult(Op("Boolean", OperatorKind.Not, null), OperatorOperands.Clr(typeof(bool)));
        await Assert.That(Op("Number", OperatorKind.Not, null)).IsNull();
    }

    [Test]
    public async Task And_Or_OnNonBoolean_AreNone() {
        await Assert.That(Op("Number", OperatorKind.And, "Number")).IsNull();
        await Assert.That(Op("Decimal", OperatorKind.Or, "Decimal")).IsNull();
        // BigInteger declares op_BitwiseAnd, which is not logical-and: And has no reflected name.
        var big = OperatorOperands.Clr(typeof(System.Numerics.BigInteger));
        await Assert.That(big.GetOperator(OperatorKind.And, big)).IsNull();
        await AssertResult(Op("Boolean", OperatorKind.And, "Boolean"), OperatorOperands.Clr(typeof(bool)));
    }

    // ---- enums and entities ----

    [Test]
    [Arguments("Status")]
    [Arguments("Order")]
    public async Task SyntheticEqual_SameDefinition_IsBoolean(string name) {
        await AssertResult(Op(name, OperatorKind.Equal, name), OperatorOperands.Clr(typeof(bool)));
        await AssertResult(Op(name, OperatorKind.NotEqual, name), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    public async Task SyntheticEqual_DifferentDefinitions_IsNone() {
        await Assert.That(Op("Status", OperatorKind.Equal, "Priority")).IsNull();
        await Assert.That(Op("Order", OperatorKind.Equal, "Customer")).IsNull();
        await Assert.That(Op("Status", OperatorKind.Equal, "Text")).IsNull();
    }

    [Test]
    public async Task SyntheticOrdering_IsNone() {
        await Assert.That(Op("Status", OperatorKind.LessThan, "Status")).IsNull();
        await Assert.That(Op("Order", OperatorKind.Add, "Order")).IsNull();
        await Assert.That(Op("Money", OperatorKind.Add, "Money")).IsNull();
    }

    // ---- declared members ----

    private readonly record struct Vec(int X) {
        public static Vec operator +(Vec a, Vec b) => new(a.X + b.X);
    }

    [Test]
    public async Task ValueTypeOperatorMember_Clr_IsFoundOnTheValueType_WithNoRegistry() {
        var vec = OperatorOperands.Clr(typeof(Vec));
        var resolution = vec.GetOperator(OperatorKind.Add, vec);
        await AssertResult(resolution, vec);
        await Assert.That(resolution!.Value.PlatformApplied).IsFalse();
        await Assert.That(resolution.Value.Signature.DeclaringTypeDefinition).IsSameReferenceAs(vec);
    }

    [Test]
    public async Task ValueTypeOperatorMember_Modeled_IsFoundOnTheDeclaringDefinition() {
        var set = new ModeledTypeSet();
        var vec = set.Add("Vec", ModeledTypeSet.Static("op_Addition", "Vec", "Vec", "Vec"));
        var resolution = ((ITypeDefinition)vec).GetOperator(OperatorKind.Add, vec);
        await AssertResult(resolution, vec);
        await Assert.That(resolution!.Value.PlatformApplied).IsFalse();
        await Assert.That(resolution.Value.Signature.DeclaringTypeDefinition).IsSameReferenceAs(vec);
        // The sibling: an operator the type does not declare is none.
        await Assert.That(((ITypeDefinition)vec).GetOperator(OperatorKind.Subtract, vec)).IsNull();
    }

    [Test]
    public async Task AmbiguousOperators_IsNone() {
        var set = new ModeledTypeSet();
        var x = set.Add("X", ModeledTypeSet.Static("op_Implicit", "X", "L"));
        var y = set.Add("Y", ModeledTypeSet.Static("op_Implicit", "Y", "R"));
        var l = set.Add("L", ModeledTypeSet.Static("op_Addition", "X", "X", "R"));
        var r = set.Add("R", ModeledTypeSet.Static("op_Addition", "Y", "L", "Y"));
        await Assert.That(((ITypeDefinition)x).ResolveConversionFrom(l)).IsNotNull();
        await Assert.That(((ITypeDefinition)y).ResolveConversionFrom(r)).IsNotNull();
        // (X, R) needs L to X; (L, Y) needs R to Y. Both apply and neither is all-identity.
        await Assert.That(((ITypeDefinition)l).GetOperator(OperatorKind.Add, r)).IsNull();
        // The sibling: with the second candidate gone the first is the only one.
        var set2 = new ModeledTypeSet();
        set2.Add("X", ModeledTypeSet.Static("op_Implicit", "X", "L"));
        var l2 = set2.Add("L", ModeledTypeSet.Static("op_Addition", "X", "X", "R"));
        var r2 = set2.Add("R");
        await AssertResult(((ITypeDefinition)l2).GetOperator(OperatorKind.Add, r2), set2.GetTypeDefinition("X")!);
    }

    [Test]
    public async Task TwoIdentityCandidates_IsNone() {
        var set = new ModeledTypeSet();
        var l = set.Add("L", ModeledTypeSet.Static("op_Addition", "L", "L", "R"));
        var r = set.Add("R", ModeledTypeSet.Static("op_Addition", "R", "L", "R"));
        await Assert.That(((ITypeDefinition)l).GetOperator(OperatorKind.Add, r)).IsNull();
        // The sibling: one identity candidate wins.
        var set2 = new ModeledTypeSet();
        var l2 = set2.Add("L", ModeledTypeSet.Static("op_Addition", "L", "L", "R"));
        var r2 = set2.Add("R");
        await AssertResult(((ITypeDefinition)l2).GetOperator(OperatorKind.Add, r2), l2);
    }

    // ---- temporal: what the CLR declares, and what it does not ----

    [Test]
    public async Task Temporal_ClrDeclared_ResolveFromTemporalDefinition() {
        var dateTime = OperatorOperands.Clr(typeof(DateTime));
        var timeSpan = OperatorOperands.Clr(typeof(TimeSpan));

        var difference = Op("DateTime", OperatorKind.Subtract, "DateTime");
        await AssertResult(difference, timeSpan);
        await Assert.That(difference!.Value.PlatformApplied).IsFalse();
        await Assert.That(difference.Value.Signature.DeclaringTypeDefinition).IsSameReferenceAs(dateTime);

        var shifted = Op("DateTime", OperatorKind.Add, "Duration");
        await AssertResult(shifted, dateTime);
        await Assert.That(shifted!.Value.Signature.DeclaringTypeDefinition).IsSameReferenceAs(dateTime);
    }

    [Test]
    public async Task DateOnly_Minus_DateOnly_IsNone() {
        // The CLR declares no op_Subtraction on DateOnly. Date difference is the temporal library's (T-10).
        await Assert.That(Op("Date", OperatorKind.Subtract, "Date")).IsNull();
        // The sibling: DateTime does declare it.
        await AssertResult(Op("DateTime", OperatorKind.Subtract, "DateTime"), OperatorOperands.Clr(typeof(TimeSpan)));
    }

    [Test]
    public async Task Duration_Plus_Duration_Pinned() {
        // Pinned to what the CLR declares today; the analyzer rejects it (ledgered).
        var resolution = Op("Duration", OperatorKind.Add, "Duration");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(TimeSpan)));
        await Assert.That(resolution!.Value.PlatformApplied).IsFalse();
    }

    // ---- arity and failure ----

    [Test]
    public async Task GetOperator_Null_Throws() {
        await Assert.That(() => ((ITypeDefinition)null!).GetOperator(OperatorKind.Add, Def("Number")))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => Def("Number").GetOperator(OperatorKind.Add, null))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task GetOperator_UndefinedKind_Throws() {
        await Assert.That(() => Def("Number").GetOperator((OperatorKind)99, Def("Number")))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetOperator_ArityMismatch_Throws() {
        await Assert.That(() => Def("Boolean").GetOperator(OperatorKind.Not, Def("Boolean")))
            .ThrowsExactly<ArgumentException>();
        // The sibling: the right arity is accepted.
        await AssertResult(Op("Boolean", OperatorKind.Not, null), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    public async Task EveryOperatorKind_IsClassifiedForArity() {
        // F-04 (unary minus) and F-05 (modulo) will add kinds; an unclassified kind must fail loudly here.
        var boolean = Def("Boolean");
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            var unary = kind is OperatorKind.Not;
            var call = () => boolean.GetOperator(kind, unary ? null : boolean);
            await Assert.That(call).ThrowsNothing();
        }
    }

    [Test]
    public async Task OperatorNameMap_IsTotalAndExact() {
        var expected = new Dictionary<OperatorKind, string?> {
            [OperatorKind.Add] = "op_Addition",
            [OperatorKind.Subtract] = "op_Subtraction",
            [OperatorKind.Multiply] = "op_Multiply",
            [OperatorKind.Divide] = "op_Division",
            [OperatorKind.Equal] = "op_Equality",
            [OperatorKind.NotEqual] = "op_Inequality",
            [OperatorKind.LessThan] = "op_LessThan",
            [OperatorKind.LessThanOrEqual] = "op_LessThanOrEqual",
            [OperatorKind.GreaterThan] = "op_GreaterThan",
            [OperatorKind.GreaterThanOrEqual] = "op_GreaterThanOrEqual",
            [OperatorKind.Not] = "op_LogicalNot",
            // No reflected or modeled name: intrinsic only.
            [OperatorKind.And] = null,
            [OperatorKind.Or] = null
        };
        await Assert.That(expected.Count).IsEqualTo(Enum.GetValues<OperatorKind>().Length);
        foreach (var (kind, name) in expected)
            await Assert.That(ModeledOperators.NameOf(kind)).IsEqualTo(name);
        await Assert.That(() => ModeledOperators.NameOf((OperatorKind)99)).ThrowsExactly<ArgumentOutOfRangeException>();
    }
}