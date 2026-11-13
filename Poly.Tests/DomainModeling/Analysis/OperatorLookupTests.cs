using System.Globalization;

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
    public async Task Float32_Add_IsFloat32_AndItsComparisonIsPlatformApplied() {
        // T-24 flipped this pin: Float32 arithmetic is built in, and the CLR-declared comparison is subsumed.
        var single = OperatorOperands.Clr(typeof(float));
        await AssertResult(single.GetOperator(OperatorKind.Add, single), single);
        await AssertResult(single.GetOperator(OperatorKind.Multiply, single), single);
        var comparison = single.GetOperator(OperatorKind.LessThan, single);
        await AssertResult(comparison, OperatorOperands.Clr(typeof(bool)));
        await Assert.That(comparison!.Value.PlatformApplied).IsTrue();
        // The sibling: Float32 with Float64 is Float64.
        await AssertResult(single.GetOperator(OperatorKind.Add, OperatorOperands.Clr(typeof(double))), OperatorOperands.Clr(typeof(double)));
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
        // BigInteger declares op_BitwiseAnd, which is BitwiseAnd, not logical-and: And has no reflected name.
        var big = OperatorOperands.Clr(typeof(System.Numerics.BigInteger));
        await Assert.That(big.GetOperator(OperatorKind.And, big)).IsNull();
        await AssertResult(big.GetOperator(OperatorKind.BitwiseAnd, big), big);
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

    private static readonly OperatorKind[] UnaryKinds = [OperatorKind.Not, OperatorKind.UnaryMinus, OperatorKind.UnaryPlus, OperatorKind.BitwiseNot];

    [Test]
    public async Task EveryOperatorKind_IsClassifiedForArity() {
        // An unclassified kind must fail loudly here. The unary set is literal, not read from the code under test.
        var boolean = Def("Boolean");
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            var unary = UnaryKinds.Contains(kind);
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
            await Assert.That(ModeledOperators.NameOf(kind)).IsEqualTo(name);
        await Assert.That(() => ModeledOperators.NameOf((OperatorKind)99)).ThrowsExactly<ArgumentOutOfRangeException>();
    }

    // ---- T-24: every operator C# defines for the built-in types, with the C# binder as the oracle ----

    private static readonly Type[] BinderOperands = [
        typeof(bool), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal), typeof(string)
    ];

    private static string NameOf(Type type) => type.GetPrimitiveType()?.GetDisplayName() ?? type.Name;

    /// <summary>A boxed value of <paramref name="type"/>. Boolean is false for Or and true otherwise, so neither conditional operator short-circuits.</summary>
    private static object One(Type type, OperatorKind kind) =>
        type == typeof(bool) ? kind is not OperatorKind.Or
        : type == typeof(string) ? "1"
        : Convert.ChangeType(1, type, CultureInfo.InvariantCulture);

    /// <summary>The type of the result the C# runtime binder gives for runtime-typed (non-constant) operands, or null when C# defines no operator.</summary>
    private static Type? Binder(OperatorKind kind, object left, object? right) {
        dynamic l = left;
        dynamic? r = right;
        try {
            object result = kind switch {
                OperatorKind.Add => l + r,
                OperatorKind.Subtract => l - r,
                OperatorKind.Multiply => l * r,
                OperatorKind.Divide => l / r,
                OperatorKind.Modulo => l % r,
                OperatorKind.Equal => l == r,
                OperatorKind.NotEqual => l != r,
                OperatorKind.LessThan => l < r,
                OperatorKind.LessThanOrEqual => l <= r,
                OperatorKind.GreaterThan => l > r,
                OperatorKind.GreaterThanOrEqual => l >= r,
                OperatorKind.And => l && r,
                OperatorKind.Or => l || r,
                OperatorKind.BitwiseAnd => l & r,
                OperatorKind.BitwiseOr => l | r,
                OperatorKind.BitwiseXor => l ^ r,
                OperatorKind.ShiftLeft => l << r,
                OperatorKind.ShiftRight => l >> r,
                OperatorKind.Not => !l,
                OperatorKind.UnaryMinus => -l,
                OperatorKind.UnaryPlus => +l,
                OperatorKind.BitwiseNot => ~l,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            return result.GetType();
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
            return null;
        }
    }

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
        foreach (var kind in Enum.GetValues<OperatorKind>()) {
            var rights = UnaryKinds.Contains(kind) ? [null] : BinderOperands.Select(static t => (Type?)t).ToArray();
            foreach (var left in BinderOperands) {
                foreach (var right in rights) {
                    var cell = right is null ? $"{kind} {NameOf(left)}" : $"{NameOf(left)} {kind} {NameOf(right)}";
                    var expected = Binder(kind, One(left, kind), right is null ? null : One(right, kind));
                    var operands = right is null ? new[] { OperatorOperands.Clr(left) } : [OperatorOperands.Clr(left), OperatorOperands.Clr(right)];
                    var resolution = operands[0].GetOperator(kind, right is null ? null : operands[1]);
                    if (expected is null) {
                        if (resolution is { } extra)
                            mismatches.Add($"{cell}: C# none, provider {extra.Result.Name}");
                        continue;
                    }
                    defined++;
                    if (resolution is not { } found) {
                        mismatches.Add($"{cell}: C# {NameOf(expected)}, provider none");
                        continue;
                    }
                    if (!found.Result.TryGetRuntimeType(out var result) || result != expected)
                        mismatches.Add($"{cell}: C# {NameOf(expected)}, provider {found.Result.Name}");
                    var parameters = found.Signature.Parameters.Select(static p => p.ParameterTypeDefinition).ToArray();
                    var bound = BinderParameters(kind, left, right);
                    for (var i = 0; i < operands.Length; i++) {
                        if (!parameters[i].TryGetRuntimeType(out var parameter) || parameter != bound[i])
                            mismatches.Add($"{cell}: parameter {i} C# {(bound[i] is { } b ? NameOf(b) : "?")}, provider {parameters[i].Name}");
                        else if (!ReferenceEquals(parameters[i], operands[i])
                            && parameters[i].ResolveConversionFrom(operands[i]) is not { Kind: ConversionOperatorKind.Implicit })
                            mismatches.Add($"{cell}: operand {i} does not convert implicitly to {parameters[i].Name}");
                    }
                }
            }
        }
        await Assert.That(mismatches).IsEmpty();
        await Assert.That(defined).IsGreaterThan(0);
    }

    private static OperatorResolution? ClrOp(Type left, OperatorKind kind, Type? right) =>
        OperatorOperands.Clr(left).GetOperator(kind, right is null ? null : OperatorOperands.Clr(right));

    private static async Task AssertParameters(OperatorResolution? resolution, params Type[] expected) {
        await Assert.That(resolution).IsNotNull();
        var parameters = ParametersOf(resolution!.Value);
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

    [Test]
    public async Task Shift_CountMustConvertToInt32() {
        foreach (var count in new[] { typeof(long), typeof(uint), typeof(ulong) }) {
            await Assert.That(ClrOp(typeof(int), OperatorKind.ShiftLeft, count)).IsNull();
            await Assert.That(ClrOp(typeof(int), OperatorKind.ShiftRight, count)).IsNull();
        }
        foreach (var count in new[] { typeof(short), typeof(char) })
            await AssertResult(ClrOp(typeof(int), OperatorKind.ShiftLeft, count), OperatorOperands.Clr(typeof(int)));
        var wide = ClrOp(typeof(long), OperatorKind.ShiftLeft, typeof(short));
        await AssertResult(wide, OperatorOperands.Clr(typeof(long)));
        await AssertParameters(wide, typeof(long), typeof(int));
        await Assert.That(ClrOp(typeof(short), OperatorKind.ShiftLeft, typeof(long))).IsNull();
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
        await Assert.That(resolution!.Value.PlatformApplied).IsTrue();
        await AssertParameters(resolution, typeof(string), typeof(object));
        var boxing = OperatorOperands.Clr(typeof(object)).ResolveConversionFrom(OperatorOperands.Clr(typeof(int)));
        await Assert.That(boxing?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(boxing!.Value.Method).IsNull();
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
        ITypeDefinition cents = set.Add("Cents", new MethodDefinitionNode("op_Implicit", new ClrTypeReference(typeof(long)),
            [new Parameter("p0", new NamedTypeReference("Cents"))], IsStatic: true));
        var number = OperatorOperands.Clr(typeof(long));
        var offered = number.GetOperators(OperatorKind.Add, cents).Single();
        await Assert.That(offered.PlatformApplied).IsTrue();
        await Assert.That(offered.Signature.Parameters.All(p => ReferenceEquals(p.ParameterTypeDefinition, number))).IsTrue();
        await Assert.That(number.ResolveConversionFrom(cents)?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        var resolution = number.GetOperator(OperatorKind.Add, cents);
        await AssertResult(resolution, number);
        await AssertParameters(resolution, typeof(long), typeof(long));
        await AssertResult(cents.GetOperator(OperatorKind.Add, number), number);
        // The sibling: without the conversion the same operator is offered and does not apply.
        ITypeDefinition plain = set.Add("Plain");
        await Assert.That(number.GetOperators(OperatorKind.Add, plain).Count()).IsEqualTo(1);
        await Assert.That(number.GetOperator(OperatorKind.Add, plain)).IsNull();
    }

    [Test]
    public async Task DeliberateNone_EnumLiftedReferenceDelegateAndObjectOperators() {
        // C# defines these; the rule set leaves them none on purpose. Enum and lifted operators are the caller's
        // (T-06); reference, delegate and object equality are not value operators.
        var day = typeof(DayOfWeek);
        await Assert.That(ClrOp(day, OperatorKind.Add, typeof(int))).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.Equal, day)).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.LessThan, day)).IsNull();
        await Assert.That(ClrOp(day, OperatorKind.BitwiseOr, day)).IsNull();
        await Assert.That(ClrOp(typeof(int?), OperatorKind.Add, typeof(int?))).IsNull();
        await Assert.That(ClrOp(typeof(int?), OperatorKind.Equal, typeof(int))).IsNull();
        await Assert.That(ClrOp(typeof(object), OperatorKind.Equal, typeof(string))).IsNull();
        await Assert.That(ClrOp(typeof(int), OperatorKind.Equal, typeof(object))).IsNull();
        await Assert.That(ClrOp(typeof(object), OperatorKind.Equal, typeof(object))).IsNull();
        await Assert.That(ClrOp(typeof(System.Action), OperatorKind.Equal, typeof(System.Action))).IsNull();
        // The siblings: the underlying value operators are defined.
        await AssertResult(ClrOp(typeof(int), OperatorKind.Add, typeof(int)), OperatorOperands.Clr(typeof(int)));
        await AssertResult(ClrOp(typeof(int), OperatorKind.Equal, typeof(int)), OperatorOperands.Clr(typeof(bool)));
        await AssertResult(ClrOp(typeof(string), OperatorKind.Equal, typeof(string)), OperatorOperands.Clr(typeof(bool)));
    }
}