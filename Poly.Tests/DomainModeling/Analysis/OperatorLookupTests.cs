using System.Globalization;

using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Meaning;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
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
        return new DomainTypeModel(BuildDomain(), maps, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
    }

    public static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
}

/// <summary>Modeled definitions declared by hand, so a test controls every operator and conversion member.</summary>
internal sealed class ModeledTypeSet : IClrTypeDefinitionProvider {
    private readonly Dictionary<string, AstTypeDefinition> _types = new(StringComparer.Ordinal);

    public AstTypeDefinition Add(string name, params MethodDefinitionNode[] methods) {
        var definition = new AstTypeDefinition(new TypeDefinitionNode(name, Form: TypeDeclarationForm.Struct, Methods: methods), this);
        _types[name] = definition;
        return definition;
    }

    public ITypeDefinition? GetTypeDefinition(string name) => _types.GetValueOrDefault(name);

    public ITypeDefinition? GetTypeDefinition(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    public IEnumerable<ITypeDefinition> Types => _types.Values;

    /// <summary>A static member: <c>static result name(parameters)</c>.</summary>
    public static MethodDefinitionNode Static(string name, string result, params string[] parameters) =>
        new(name, new NamedTypeReference(result),
            [.. parameters.Select((type, index) => new Parameter($"p{index}", new NamedTypeReference(type)))],
            IsStatic: true);

    /// <summary>A static member marked as the <paramref name="kind"/> operator: <c>static result Kind(parameters)</c>.</summary>
    public static MethodDefinitionNode Operator(OperatorKind kind, string result, params string[] parameters) =>
        Static(kind.ToString(), result, parameters) with { Operator = kind };

    /// <summary>A static member marked as the <paramref name="kind"/> conversion: <c>static result Conversion(parameters)</c>.</summary>
    public static MethodDefinitionNode Conversion(ConversionOperatorKind kind, string result, params string[] parameters) =>
        Static(kind.ToString(), result, parameters) with { Conversion = kind };
}

/// <summary>
/// T-03b: <c>GetOperator</c> over the Introspection interface. Operands come from the domain type model, so
/// the aliases (Number is Int64, Float is Float64) are the ones the language uses.
/// </summary>
public class OperatorLookupTests {
    private static readonly DomainTypeModel Model = OperatorOperands.Model();

    private static ITypeDefinition Def(string name) => Model.Resolve(name).Definition!;

    private static ITypeOperator? Op(string left, OperatorKind kind, string? right) =>
        Def(left).GetOperator(kind, right is null ? null : Def(right), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

    private static ITypeDefinition[] ParametersOf(ITypeOperator resolution) =>
        [.. resolution.Parameters.Select(static p => p.ParameterTypeDefinition)];

    private static async Task AssertResult(ITypeOperator? resolution, ITypeDefinition expected) {
        await Assert.That(resolution).IsNotNull();
        await Assert.That(resolution!.ResultTypeDefinition).IsSameReferenceAs(expected);
    }

    // ---- numeric family ----

    [Test]
    public async Task Number_Div_Number_IsInt64IntegerDivision() {
        var resolution = Op("Number", OperatorKind.Divide, "Number");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(long)));
        await Assert.That(resolution!.Method).IsNull();
    }

    [Test]
    public async Task Decimal_Plus_Number_ResolvesDecimalOperator() {
        var resolution = Op("Decimal", OperatorKind.Add, "Number");
        var decimalType = OperatorOperands.Clr(typeof(decimal));
        await AssertResult(resolution, decimalType);
        await Assert.That(resolution!.Method).IsNotNull();
        await Assert.That(resolution.Method!.DeclaringTypeDefinition).IsSameReferenceAs(decimalType);
        var parameters = ParametersOf(resolution);
        await Assert.That(parameters.Length).IsEqualTo(2);
        await Assert.That(parameters[0]).IsSameReferenceAs(decimalType);
        await Assert.That(parameters[1]).IsSameReferenceAs(decimalType);
    }

    [Test]
    public async Task Decimal_Plus_Number_RequiresImplicitNumberToDecimalOperandConversion() {
        // R6: the conversion is a statement the tree makes. The interface names it; the ABI must not invent it.
        var resolution = Op("Decimal", OperatorKind.Add, "Number")!;
        var operands = new[] { Def("Decimal"), Def("Number") };
        var parameters = ParametersOf(resolution);
        await Assert.That(ReferenceEquals(parameters[0], operands[0])).IsTrue();
        var conversion = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(operands[1], parameters[1]);
        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
    }

    [Test]
    public async Task Number_Plus_Decimal_ResolvesDecimalOperator() {
        var resolution = Op("Number", OperatorKind.Add, "Decimal");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(decimal)));
        await Assert.That(resolution!.Method).IsNotNull();
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
    public async Task Float32_Add_IsFloat32_AndItsComparisonIsPlatformApplied() {
        // T-24 flipped this pin: Float32 arithmetic is built in, and the CLR-declared comparison is subsumed.
        var single = OperatorOperands.Clr(typeof(float));
        await AssertResult(single.GetOperator(OperatorKind.Add, single, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), single);
        await AssertResult(single.GetOperator(OperatorKind.Multiply, single, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), single);
        var comparison = single.GetOperator(OperatorKind.LessThan, single, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        await AssertResult(comparison, OperatorOperands.Clr(typeof(bool)));
        await Assert.That(comparison!.Method).IsNull();
        // The sibling: Float32 with Float64 is Float64.
        await AssertResult(single.GetOperator(OperatorKind.Add, OperatorOperands.Clr(typeof(double)), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), OperatorOperands.Clr(typeof(double)));
    }

    // ---- text and boolean ----

    [Test]
    public async Task Text_Plus_Text_IsText() {
        var resolution = Op("Text", OperatorKind.Add, "Text");
        await AssertResult(resolution, OperatorOperands.Clr(typeof(string)));
        await Assert.That(resolution!.Method).IsNull();
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
        // BigInteger declares op_BitwiseAnd, which is BitwiseAnd, not logical-and: And has no reflected name.
        var big = OperatorOperands.Clr(typeof(System.Numerics.BigInteger));
        await Assert.That(big.GetOperator(OperatorKind.And, big, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
        await AssertResult(big.GetOperator(OperatorKind.BitwiseAnd, big, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), big);
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
        var resolution = vec.GetOperator(OperatorKind.Add, vec, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        await AssertResult(resolution, vec);
        await Assert.That(resolution!.Method).IsNotNull();
        await Assert.That(resolution.Method!.DeclaringTypeDefinition).IsSameReferenceAs(vec);
    }

    [Test]
    public async Task ValueTypeOperatorMember_Modeled_IsFoundOnTheDeclaringDefinition() {
        var set = new ModeledTypeSet();
        var vec = set.Add("Vec", ModeledTypeSet.Operator(OperatorKind.Add, "Vec", "Vec", "Vec"));
        var resolution = ((ITypeDefinition)vec).GetOperator(OperatorKind.Add, vec, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        await AssertResult(resolution, vec);
        await Assert.That(resolution!.Method).IsNotNull();
        await Assert.That(resolution.Method!.DeclaringTypeDefinition).IsSameReferenceAs(vec);
        // The sibling: an operator the type does not declare is none.
        await Assert.That(((ITypeDefinition)vec).GetOperator(OperatorKind.Subtract, vec, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
    }

    [Test]
    public async Task AmbiguousOperators_IsNone() {
        var set = new ModeledTypeSet();
        var x = set.Add("X", ModeledTypeSet.Conversion(ConversionOperatorKind.Implicit, "X", "L"));
        var y = set.Add("Y", ModeledTypeSet.Conversion(ConversionOperatorKind.Implicit, "Y", "R"));
        var l = set.Add("L", ModeledTypeSet.Operator(OperatorKind.Add, "X", "X", "R"));
        var r = set.Add("R", ModeledTypeSet.Operator(OperatorKind.Add, "Y", "L", "Y"));
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(l, ((ITypeDefinition)x))).IsNotNull();
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(r, ((ITypeDefinition)y))).IsNotNull();
        // (X, R) needs L to X; (L, Y) needs R to Y. Both apply and neither is all-identity.
        await Assert.That(((ITypeDefinition)l).GetOperator(OperatorKind.Add, r, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
        // The sibling: with the second candidate gone the first is the only one.
        var set2 = new ModeledTypeSet();
        set2.Add("X", ModeledTypeSet.Conversion(ConversionOperatorKind.Implicit, "X", "L"));
        var l2 = set2.Add("L", ModeledTypeSet.Operator(OperatorKind.Add, "X", "X", "R"));
        var r2 = set2.Add("R");
        await AssertResult(((ITypeDefinition)l2).GetOperator(OperatorKind.Add, r2, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), set2.GetTypeDefinition("X")!);
    }

    [Test]
    public async Task TwoIdentityCandidates_IsNone() {
        var set = new ModeledTypeSet();
        var l = set.Add("L", ModeledTypeSet.Operator(OperatorKind.Add, "L", "L", "R"));
        var r = set.Add("R", ModeledTypeSet.Operator(OperatorKind.Add, "R", "L", "R"));
        await Assert.That(((ITypeDefinition)l).GetOperator(OperatorKind.Add, r, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
        // The sibling: one identity candidate wins.
        var set2 = new ModeledTypeSet();
        var l2 = set2.Add("L", ModeledTypeSet.Operator(OperatorKind.Add, "L", "L", "R"));
        var r2 = set2.Add("R");
        await AssertResult(((ITypeDefinition)l2).GetOperator(OperatorKind.Add, r2, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), l2);
    }

    // ---- temporal: what the CLR declares, and what it does not ----

    [Test]
    public async Task Temporal_ClrDeclared_ResolveFromTemporalDefinition() {
        var dateTime = OperatorOperands.Clr(typeof(DateTime));
        var timeSpan = OperatorOperands.Clr(typeof(TimeSpan));

        var difference = Op("DateTime", OperatorKind.Subtract, "DateTime");
        await AssertResult(difference, timeSpan);
        await Assert.That(difference!.Method).IsNotNull();
        await Assert.That(difference.Method!.DeclaringTypeDefinition).IsSameReferenceAs(dateTime);

        var shifted = Op("DateTime", OperatorKind.Add, "Duration");
        await AssertResult(shifted, dateTime);
        await Assert.That(shifted!.Method!.DeclaringTypeDefinition).IsSameReferenceAs(dateTime);
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
        await Assert.That(resolution!.Method).IsNotNull();
    }

    // ---- arity and failure ----

    [Test]
    public async Task GetOperator_Null_Throws() {
        await Assert.That(() => ((ITypeDefinition)null!).GetOperator(OperatorKind.Add, Def("Number"), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared))
            .ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => Def("Number").GetOperator(OperatorKind.Add, null, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared))
            .ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task GetOperator_UndefinedKind_Throws() {
        await Assert.That(() => Def("Number").GetOperator((OperatorKind)99, Def("Number"), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetOperator_ArityMismatch_Throws() {
        await Assert.That(() => Def("Boolean").GetOperator(OperatorKind.Not, Def("Boolean"), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared))
            .ThrowsExactly<ArgumentException>();
        // The sibling: the right arity is accepted.
        await AssertResult(Op("Boolean", OperatorKind.Not, null), OperatorOperands.Clr(typeof(bool)));
    }

    private static readonly OperatorKind[] UnaryKinds = [OperatorKind.Not, OperatorKind.UnaryMinus, OperatorKind.UnaryPlus, OperatorKind.BitwiseNot];

    [Test]
    public async Task EveryOperatorKind_IsClassifiedForArity() {
        // An unclassified kind must fail loudly here. The unary set is literal, not read from the code under test.
        var boolean = Def("Boolean");
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            var unary = UnaryKinds.Contains(kind);
            var call = () => boolean.GetOperator(kind, unary ? null : boolean, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
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
            [OperatorKind.Modulo] = "op_Modulus",
            [OperatorKind.UnaryMinus] = "op_UnaryNegation",
            [OperatorKind.UnaryPlus] = "op_UnaryPlus",
            [OperatorKind.BitwiseAnd] = "op_BitwiseAnd",
            [OperatorKind.BitwiseOr] = "op_BitwiseOr",
            [OperatorKind.BitwiseXor] = "op_ExclusiveOr",
            [OperatorKind.BitwiseNot] = "op_OnesComplement",
            [OperatorKind.ShiftLeft] = "op_LeftShift",
            [OperatorKind.ShiftRight] = "op_RightShift",
            // No reflected or modeled name: intrinsic only.
            [OperatorKind.And] = null,
            [OperatorKind.Or] = null
        };
        await Assert.That(expected.Count).IsEqualTo(Enum.GetValues<OperatorKind>().Length);
        foreach (var (kind, name) in expected)
            await Assert.That(ClrSpecialNames.OperatorName(kind)).IsEqualTo(name);
        await Assert.That(() => ClrSpecialNames.OperatorName((OperatorKind)99)).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    // ---- T-24: every operator C# defines for the built-in types, with the C# binder as the oracle ----

    private static readonly Type[] BinderOperands = [
        typeof(bool), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal), typeof(string)
    ];

    private static string NameOf(Type type) => type.Name;

    /// <summary>A boxed value of <paramref name="type"/>. Boolean is false for Or and true otherwise, so neither conditional operator short-circuits.</summary>
    private static object One(Type type, OperatorKind kind) =>
        type == typeof(bool) ? kind is not OperatorKind.Or
        : type == typeof(string) ? "1"
        : Convert.ChangeType(1, type, CultureInfo.InvariantCulture);

    /// <summary>The type of the result the C# runtime binder gives for runtime-typed (non-constant) operands, or null when C# defines no operator.</summary>
    private static Type? Binder(OperatorKind kind, object left, object? right) => CSharpOperatorBinder.ResultType(kind, left, right);

    /// <summary>
    /// The parameter types C# binds a defined cell to. A comparison's Boolean result hides the promoted type, so a
    /// numeric pair reads it from the arithmetic twin (<c>l + r</c>), a shift from <c>l &lt;&lt; 1</c>, a unary operator from itself.
    /// </summary>
    private static Type?[] BinderParameters(OperatorKind kind, Type left, Type? right) {
        if (right is null)
            return [kind is OperatorKind.Not ? typeof(bool) : Binder(kind, One(left, kind), null)];
        if (kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight)
            return [Binder(OperatorKind.ShiftLeft, One(left, kind), 1), typeof(int)];
        if (left == typeof(string) || right == typeof(string))
            return left == right ? [left, right] : [left == typeof(string) ? left : typeof(object), right == typeof(string) ? right : typeof(object)];
        if (left == typeof(bool))
            return [left, right];
        var promoted = Binder(OperatorKind.Add, One(left, kind), One(right, kind));
        return [promoted, promoted];
    }

    [Test]
    public async Task GetOperator_AgreesWithTheCSharpBinder() {
        var mismatches = new List<string>();
        var defined = 0;
        var undefined = 0;
        var policy = 0;
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            var rights = UnaryKinds.Contains(kind) ? [null] : BinderOperands.Select(static t => (Type?)t).ToArray();
            foreach (var left in BinderOperands) {
                foreach (var right in rights) {
                    var cell = right is null ? $"{kind} {NameOf(left)}" : $"{NameOf(left)} {kind} {NameOf(right)}";
                    // Poly's shift-count policy (C# rejects, Poly defines): the cell is C#'s shift by an int count, its count
                    // parameter the wider count type itself.
                    if (IsPolicyShift(kind, right) && Binder(kind, One(left, kind), 1) is { } shifted) {
                        policy++;
                        if (ClrOp(left, kind, right) is not { } policyShift || !policyShift.ResultTypeDefinition.TryGetRuntimeType(out var shiftResult) || shiftResult != shifted
                            || ParametersOf(policyShift).Select(static p => p.GetRuntimeType()).ToArray() is not [var l, var r] || l != shifted || r != right)
                            mismatches.Add($"{cell}: policy expects C#'s int-count shift ({NameOf(shifted)}) over ({NameOf(left)}, {NameOf(right!)})");
                        continue;
                    }
                    var expected = Binder(kind, One(left, kind), right is null ? null : One(right, kind));
                    var operands = right is null ? new[] { OperatorOperands.Clr(left) } : [OperatorOperands.Clr(left), OperatorOperands.Clr(right)];
                    var resolution = operands[0].GetOperator(kind, right is null ? null : operands[1], Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
                    if (expected is null) {
                        undefined++;
                        if (resolution is { } extra)
                            mismatches.Add($"{cell}: C# none, provider {extra.ResultTypeDefinition.Name}");
                        continue;
                    }
                    defined++;
                    if (resolution is not { } found) {
                        mismatches.Add($"{cell}: C# {NameOf(expected)}, provider none");
                        continue;
                    }
                    if (!found.ResultTypeDefinition.TryGetRuntimeType(out var result) || result != expected)
                        mismatches.Add($"{cell}: C# {NameOf(expected)}, provider {found.ResultTypeDefinition.Name}");
                    var parameters = found.Parameters.Select(static p => p.ParameterTypeDefinition).ToArray();
                    var bound = BinderParameters(kind, left, right);
                    for (var i = 0; i < operands.Length; i++) {
                        if (!parameters[i].TryGetRuntimeType(out var parameter) || parameter != bound[i])
                            mismatches.Add($"{cell}: parameter {i} C# {(bound[i] is { } b ? NameOf(b) : "?")}, provider {parameters[i].Name}");
                        else if (!ReferenceEquals(parameters[i], operands[i])
                            && Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(operands[i], parameters[i]) is not { Kind: ConversionOperatorKind.Implicit })
                            mismatches.Add($"{cell}: operand {i} does not convert implicitly to {parameters[i].Name}");
                    }
                }
            }
        }
        await Assert.That(mismatches).IsEmpty();
        // The counts are pinned, so a family that answers none on both sides moves a number and fails here. The 54 policy
        // cells are the 9 integral lefts by the 3 wider counts (long, uint, ulong) by the 2 shifts.
        await Assert.That(defined).IsEqualTo(1848);
        await Assert.That(undefined).IsEqualTo(1682);
        await Assert.That(policy).IsEqualTo(54);
    }

    private static bool IsPolicyShift(OperatorKind kind, Type? count) =>
        kind is OperatorKind.ShiftLeft or OperatorKind.ShiftRight
        && CSharpBuiltIns.IsPolicyShiftCount(Poly.Interpretation.CSharp.CSharpTypeSystem.SimpleTypeOf(count));

    private static ITypeOperator? ClrOp(Type left, OperatorKind kind, Type? right) =>
        OperatorOperands.Clr(left).GetOperator(kind, right is null ? null : OperatorOperands.Clr(right), Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);

    private static async Task AssertParameters(ITypeOperator? resolution, params Type[] expected) {
        await Assert.That(resolution).IsNotNull();
        var parameters = ParametersOf(resolution!);
        await Assert.That(parameters.Length).IsEqualTo(expected.Length);
        for (var i = 0; i < expected.Length; i++)
            await Assert.That(parameters[i]).IsSameReferenceAs(OperatorOperands.Clr(expected[i]));
    }

    [Test]
    public async Task UInt64_WithASignedIntegral_IsNone_WithAnUnsignedOne_IsUInt64() {
        // §12.4.7.3: ulong with sbyte, short, int or long is a binding-time error.
        foreach (var signed in new[] { typeof(int), typeof(long), typeof(sbyte), typeof(short) }) {
            await Assert.That(ClrOp(typeof(ulong), OperatorKind.Add, signed)).IsNull();
            await Assert.That(ClrOp(signed, OperatorKind.Add, typeof(ulong))).IsNull();
        }
        foreach (var unsigned in new[] { typeof(byte), typeof(uint) }) {
            await AssertResult(ClrOp(typeof(ulong), OperatorKind.Add, unsigned), OperatorOperands.Clr(typeof(ulong)));
            await AssertResult(ClrOp(unsigned, OperatorKind.Add, typeof(ulong)), OperatorOperands.Clr(typeof(ulong)));
        }
    }

    [Test]
    public async Task Decimal_WithFloat32OrFloat64_IsNone_WithInt32_IsDecimal() {
        foreach (var kind in new[] { OperatorKind.Add, OperatorKind.Subtract }) {
            foreach (var real in new[] { typeof(float), typeof(double) }) {
                await Assert.That(ClrOp(typeof(decimal), kind, real)).IsNull();
                await Assert.That(ClrOp(real, kind, typeof(decimal))).IsNull();
            }
        }
        await AssertResult(ClrOp(typeof(decimal), OperatorKind.Add, typeof(int)), OperatorOperands.Clr(typeof(decimal)));
        await AssertResult(ClrOp(typeof(int), OperatorKind.Modulo, typeof(decimal)), OperatorOperands.Clr(typeof(decimal)));
    }

    [Test]
    public async Task MixedPromotions_FollowTheSpec() {
        // The float rule precedes the ulong rule; char and the small integers promote to int.
        await AssertResult(ClrOp(typeof(float), OperatorKind.Add, typeof(ulong)), OperatorOperands.Clr(typeof(float)));
        await AssertResult(ClrOp(typeof(uint), OperatorKind.Add, typeof(char)), OperatorOperands.Clr(typeof(uint)));
        await AssertResult(ClrOp(typeof(char), OperatorKind.Add, typeof(char)), OperatorOperands.Clr(typeof(int)));
        var mixed = ClrOp(typeof(uint), OperatorKind.Add, typeof(int));
        await AssertResult(mixed, OperatorOperands.Clr(typeof(long)));
        await AssertParameters(mixed, typeof(long), typeof(long));
        // A comparison carries the promoted parameters too.
        await AssertParameters(ClrOp(typeof(byte), OperatorKind.LessThan, typeof(sbyte)), typeof(int), typeof(int));
    }

    [Test]
    public async Task UnaryPromotion_FollowsTheSpec() {
        await Assert.That(ClrOp(typeof(ulong), OperatorKind.UnaryMinus, null)).IsNull();
        var negated = ClrOp(typeof(uint), OperatorKind.UnaryMinus, null);
        await AssertResult(negated, OperatorOperands.Clr(typeof(long)));
        await AssertParameters(negated, typeof(long));
        await AssertResult(ClrOp(typeof(uint), OperatorKind.UnaryPlus, null), OperatorOperands.Clr(typeof(uint)));
        await AssertResult(ClrOp(typeof(byte), OperatorKind.BitwiseNot, null), OperatorOperands.Clr(typeof(int)));
        await AssertResult(ClrOp(typeof(char), OperatorKind.UnaryMinus, null), OperatorOperands.Clr(typeof(int)));
        await Assert.That(ClrOp(typeof(double), OperatorKind.BitwiseNot, null)).IsNull();
        // The siblings: +ulong is defined, and so is -long.
        await AssertResult(ClrOp(typeof(ulong), OperatorKind.UnaryPlus, null), OperatorOperands.Clr(typeof(ulong)));
        await AssertResult(ClrOp(typeof(long), OperatorKind.UnaryMinus, null), OperatorOperands.Clr(typeof(long)));
    }

    /// <summary>
    /// C# (§12.11) shifts by a count that converts implicitly to int. Poly's policy (owner ruling 2026-11-13) adds a count
    /// of any other built-in integer type: the operator takes it as its own type and the result is the left operand's.
    /// A non-integer count stays none.
    /// </summary>
    [Test]
    public async Task Shift_CountConvertsToInt32_OrIsAWiderIntegerByPolicy() {
        foreach (var count in new[] { typeof(long), typeof(uint), typeof(ulong), typeof(nint), typeof(nuint) }) {
            foreach (var kind in new[] { OperatorKind.ShiftLeft, OperatorKind.ShiftRight }) {
                var shift = ClrOp(typeof(int), kind, count);
                await AssertResult(shift, OperatorOperands.Clr(typeof(int)));
                await AssertParameters(shift, typeof(int), count);
            }
        }
        foreach (var count in new[] { typeof(double), typeof(decimal), typeof(string), typeof(bool) })
            await Assert.That(ClrOp(typeof(int), OperatorKind.ShiftLeft, count)).IsNull();
        foreach (var count in new[] { typeof(short), typeof(char) })
            await AssertResult(ClrOp(typeof(int), OperatorKind.ShiftLeft, count), OperatorOperands.Clr(typeof(int)));
        var wide = ClrOp(typeof(long), OperatorKind.ShiftLeft, typeof(short));
        await AssertResult(wide, OperatorOperands.Clr(typeof(long)));
        await AssertParameters(wide, typeof(long), typeof(int));
        // A small left promotes as for an int count.
        var small = ClrOp(typeof(short), OperatorKind.ShiftLeft, typeof(long));
        await AssertResult(small, OperatorOperands.Clr(typeof(int)));
        await AssertParameters(small, typeof(int), typeof(long));
        var bytes = ClrOp(typeof(byte), OperatorKind.ShiftLeft, typeof(byte));
        await AssertResult(bytes, OperatorOperands.Clr(typeof(int)));
        await AssertParameters(bytes, typeof(int), typeof(int));
    }

    [Test]
    public async Task Modulo_And_Bitwise_FollowTheOperandFamily() {
        await AssertResult(Op("Number", OperatorKind.Modulo, "Number"), OperatorOperands.Clr(typeof(long)));
        await AssertResult(Op("Float", OperatorKind.Modulo, "Float"), OperatorOperands.Clr(typeof(double)));
        await Assert.That(Op("Float", OperatorKind.BitwiseAnd, "Float")).IsNull();
        await AssertResult(Op("Number", OperatorKind.BitwiseAnd, "Number"), OperatorOperands.Clr(typeof(long)));
        await AssertResult(Op("Boolean", OperatorKind.BitwiseXor, "Boolean"), OperatorOperands.Clr(typeof(bool)));
    }

    [Test]
    public async Task Text_Plus_Int32_IsStringObjectConcatenation_WithAnImplicitBoxing() {
        var text = OperatorOperands.Clr(typeof(string));
        var resolution = ClrOp(typeof(string), OperatorKind.Add, typeof(int));
        await AssertResult(resolution, text);
        await Assert.That(resolution!.Method).IsNull();
        await AssertParameters(resolution, typeof(string), typeof(object));
        var boxing = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(OperatorOperands.Clr(typeof(int)), OperatorOperands.Clr(typeof(object)));
        await Assert.That(boxing?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(boxing!.Method).IsNull();
        // The mirror, and the sibling: Text + Text stays (String, String).
        await AssertParameters(ClrOp(typeof(int), OperatorKind.Add, typeof(string)), typeof(object), typeof(string));
        await AssertParameters(Op("Text", OperatorKind.Add, "Text"), typeof(string), typeof(string));
        // A modeled operand does not box, so modeled concatenation stays none.
        await Assert.That(Op("Text", OperatorKind.Add, "Money")).IsNull();
    }

    [Test]
    public async Task NumericBesideANonNumericOperand_OffersItsOwnOperator_SoAModeledImplicitConversionReachesIt() {
        // Beside an operand that is not one of the simple numeric types, Int64 offers its own (Int64, Int64) addition.
        // A modeled type that converts implicitly to Int64 relies on that: the operand converts and the operator applies, as in C#.
        var set = new ModeledTypeSet();
        ITypeDefinition cents = set.Add("Cents", new MethodDefinitionNode("op_Implicit", ClrTypeReferences.Of(typeof(long)),
            [new Parameter("p0", new NamedTypeReference("Cents"))], IsStatic: true) { Conversion = ConversionOperatorKind.Implicit });
        var number = OperatorOperands.Clr(typeof(long));
        var offered = CSharpBuiltIns.OperatorsBeside((ClrTypeDefinition)number, OperatorKind.Add, cents).Single();
        await Assert.That(offered.Method).IsNull();
        await Assert.That(offered.Parameters.All(p => ReferenceEquals(p.ParameterTypeDefinition, number))).IsTrue();
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Conversion(cents, number)?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        var resolution = number.GetOperator(OperatorKind.Add, cents, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared);
        await AssertResult(resolution, number);
        await AssertParameters(resolution, typeof(long), typeof(long));
        await AssertResult(cents.GetOperator(OperatorKind.Add, number, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared), number);
        // The sibling: without the conversion the same operator is offered and does not apply.
        ITypeDefinition plain = set.Add("Plain");
        await Assert.That(CSharpBuiltIns.OperatorsBeside((ClrTypeDefinition)number, OperatorKind.Add, plain).Count).IsEqualTo(1);
        await Assert.That(number.GetOperator(OperatorKind.Add, plain, Poly.Interpretation.CSharp.CSharpTypeSystem.Shared)).IsNull();
    }

    [Test]
    public async Task DeliberateNone_EnumAndLiftedOperators() {
        // C# defines these; the rule set leaves them none on purpose. Enum operators are T-25b's and T-29's, lifted
        // operators the caller's (T-06).
        var day = typeof(DayOfWeek);
        await Assert.That(ClrOp(day, OperatorKind.Add, typeof(int))).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.Equal, day)).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.LessThan, day)).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.BitwiseOr, day)).IsNull();
        await Assert.That(ClrOp(typeof(int?), OperatorKind.Add, typeof(int?))).IsNull();
        await Assert.That(ClrOp(typeof(int?), OperatorKind.Equal, typeof(int))).IsNull();
        // A value type has no reference equality: int == object is none (C# boxes nothing for ==).
        await Assert.That(ClrOp(typeof(int), OperatorKind.Equal, typeof(object))).IsNull();
        // The siblings: the underlying value operators are defined.
        await AssertResult(ClrOp(typeof(int), OperatorKind.Add, typeof(int)), OperatorOperands.Clr(typeof(int)));
        await AssertResult(ClrOp(typeof(int), OperatorKind.Equal, typeof(int)), OperatorOperands.Clr(typeof(bool)));
        await AssertResult(ClrOp(typeof(string), OperatorKind.Equal, typeof(string)), OperatorOperands.Clr(typeof(bool)));
    }

    private interface IShape;

    private class Shape : IShape;

    private sealed class Circle : Shape;

    private class Unrelated;

    private sealed class Sealed;

    /// <summary>
    /// C# 12.12.9, delegate equality, as Roslyn binds it: two operands of one delegate type <c>D</c> have
    /// <c>D.operator ==(D, D)</c> (equal invocation lists); two different delegate types have none (CS0019); a delegate beside
    /// <c>object</c> or <c>System.Delegate</c> is reference equality (CS0252, a warning); <c>Delegate == Delegate</c> is
    /// <c>System.Delegate</c>'s own operator.
    /// </summary>
    [Test]
    public async Task DelegateEquality_IsCSharps_12_12_9() {
        var boolean = OperatorOperands.Clr(typeof(bool));
        foreach (var kind in new[] { OperatorKind.Equal, OperatorKind.NotEqual }) {
            foreach (var type in new[] { typeof(System.Action), typeof(Func<int>), typeof(EventHandler) }) {
                var found = ClrOp(type, kind, type);
                await AssertResult(found, boolean);
                await AssertParameters(found, type, type);
            }
            await Assert.That(ClrOp(typeof(System.Action), kind, typeof(Func<int>))).IsNull();
            await Assert.That(ClrOp(typeof(System.Action), kind, typeof(EventHandler))).IsNull();
            await AssertParameters(ClrOp(typeof(System.Action), kind, typeof(object)), typeof(object), typeof(object));
            await AssertParameters(ClrOp(typeof(System.Action), kind, typeof(Delegate)), typeof(object), typeof(object));
            await AssertParameters(ClrOp(typeof(Delegate), kind, typeof(Delegate)), typeof(Delegate), typeof(Delegate));
        }
    }

    /// <summary>
    /// T-22a, C# 12.12.7 reference type equality: <c>bool ==(object, object)</c> and <c>!=</c> when both operands are
    /// reference types with a reference conversion between them in either direction. Two unrelated classes have none, and
    /// string's own equality wins over the reference one.
    /// </summary>
    [Test]
    public async Task ReferenceEquality_IsCSharps_1212_7() {
        var boolean = OperatorOperands.Clr(typeof(bool));
        foreach (var (left, right) in new[] {
                     (typeof(object), typeof(object)), (typeof(object), typeof(string)), (typeof(Shape), typeof(Circle)),
                     (typeof(Circle), typeof(Shape)), (typeof(IShape), typeof(Circle)), (typeof(IShape), typeof(Unrelated)),
                     (typeof(long[]), typeof(long[])), (typeof(List<int>), typeof(object)) }) {
            foreach (var kind in new[] { OperatorKind.Equal, OperatorKind.NotEqual }) {
                var found = ClrOp(left, kind, right);
                await AssertResult(found, boolean);
                await AssertParameters(found, typeof(object), typeof(object));
            }
        }
        await Assert.That(ClrOp(typeof(Circle), OperatorKind.Equal, typeof(Unrelated))).IsNull();
        await Assert.That(ClrOp(typeof(IShape), OperatorKind.Equal, typeof(Sealed))).IsNull();
        await Assert.That(ClrOp(typeof(long[]), OperatorKind.Equal, typeof(int[]))).IsNull();
        await Assert.That(ClrOp(typeof(Shape), OperatorKind.LessThan, typeof(Shape))).IsNull();
        await AssertParameters(ClrOp(typeof(string), OperatorKind.Equal, typeof(string)), typeof(string), typeof(string));
    }
}