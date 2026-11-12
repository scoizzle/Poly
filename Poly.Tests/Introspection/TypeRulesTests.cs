using System.Reflection;

using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.DomainModeling.Analysis;

using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-03b: what an <see cref="ITypeDefinition"/> answers about conversions and operators.
/// </summary>
public class TypeRulesTests {
    /// <summary>
    /// B7: the public conversion door as it stood on master before T-03b, over every CLR primitive pair.
    /// One row per destination, one cell per source in <see cref="Prim"/> order. A cell is three letters:
    /// GetConversionFrom (N none, I implicit, E explicit), IsAssignableFrom (T/F), IsNumericWideningFrom (T/F).
    /// Captured from origin/master 51b9453e; never regenerate it from the code under test.
    /// </summary>
    private static readonly Dictionary<Prim, string> MasterDoorMatrix = new() {
        [Poly.Introspection.PrimitiveType.Boolean] = "NTF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int8] = "NFF NTT NFF NFF NFF NFT NFF NFF NFF NFF NFF EFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int16] = "NFF NFT NTT NFF NFF NFT NFT NFF NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int32] = "NFF NFT NFT NTT NFF NFT NFT NFT NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Int64] = "NFF NFT NFT NFT NTT NFT NFT NFT NFT NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt8] = "NFF NFT NFF NFF NFF NTT NFF NFF NFF NFF NFF EFF NFF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt16] = "NFF NFT NFT NFF NFF NFT NTT NFF NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt32] = "NFF NFT NFT NFT NFF NFT NFT NTT NFF NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.UInt64] = "NFF NFT NFT NFT NFT NFT NFT NFT NTT NFF NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Float32] = "NFF NFT NFT NFT NFT NFT NFT NFT NFT NTT NFF EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Float64] = "NFF NFT NFT NFT NFT NFT NFT NFT NFT NFT NTT EFF NFF NFT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Decimal] = "NFF ITT ITT ITT ITT ITT ITT ITT ITT EFT EFT NTT NFF ITT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.String] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Char] = "NFF NFT NFT NFF NFF NFT NFT NFF NFF NFF NFF EFF NFF NTT NFF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.DateTime] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.DateOnly] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.TimeOnly] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.TimeSpan] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF NFF",
        [Poly.Introspection.PrimitiveType.Guid] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF NFF",
        [Poly.Introspection.PrimitiveType.ByteArray] = "NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NFF NTF NFF",
        [Poly.Introspection.PrimitiveType.Structure] = "NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF NTF",
    };

    [Test]
    public async Task PublicConversionDoor_IsUnchanged() {
        var prims = Enum.GetValues<Prim>();
        var mismatches = new List<string>();
        foreach (var to in prims) {
            var cells = MasterDoorMatrix[to].Split(' ');
            var target = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(to.GetClrType()!);
            for (var i = 0; i < prims.Length; i++) {
                var source = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(prims[i].GetClrType()!);
                var conversion = target.GetConversionFrom(source);
                var actual = string.Concat(
                    conversion is null ? 'N' : conversion.Value.Kind == ConversionOperatorKind.Implicit ? 'I' : 'E',
                    target.IsAssignableFrom(source) ? 'T' : 'F',
                    target.IsNumericWideningFrom(source) ? 'T' : 'F');
                if (actual != cells[i])
                    mismatches.Add($"{prims[i]} -> {to}: expected {cells[i]}, was {actual}");
            }
        }
        await Assert.That(mismatches).IsEmpty();
    }

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    private static ITypeDefinition[] ParametersOf(DeclaredOperator declared) =>
        [.. declared.Signature.Parameters.Select(static p => p.ParameterTypeDefinition)];

    // ---- ResolveConversionFrom: intrinsic conversions are reds on the stub, reflected ones are pins ----

    [Test]
    [Arguments(typeof(long), typeof(double), ConversionOperatorKind.Implicit)]
    [Arguments(typeof(int), typeof(long), ConversionOperatorKind.Implicit)]
    [Arguments(typeof(int), typeof(double), ConversionOperatorKind.Implicit)]
    [Arguments(typeof(double), typeof(long), ConversionOperatorKind.Explicit)]
    [Arguments(typeof(double), typeof(int), ConversionOperatorKind.Explicit)]
    [Arguments(typeof(long), typeof(int), ConversionOperatorKind.Explicit)]
    public async Task ResolveConversionFrom_ClosedNumericSet_IsIntrinsic(Type from, Type to, ConversionOperatorKind kind) {
        var conversion = Clr(to).ResolveConversionFrom(Clr(from));
        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Value.Kind).IsEqualTo(kind);
        await Assert.That(conversion.Value.Method).IsNull();
    }

    [Test]
    public async Task ResolveConversionFrom_Float64ToDecimal_IsExplicitReflected() {
        var conversion = Clr(typeof(decimal)).ResolveConversionFrom(Clr(typeof(double)));
        await Assert.That(conversion!.Value.Kind).IsEqualTo(ConversionOperatorKind.Explicit);
        await Assert.That(conversion.Value.Method).IsNotNull();
        await Assert.That(conversion.Value.Method!.DeclaringTypeDefinition).IsSameReferenceAs(Clr(typeof(decimal)));
    }

    [Test]
    public async Task ResolveConversionFrom_Int64ToDecimal_IsImplicitReflected() {
        var conversion = Clr(typeof(decimal)).ResolveConversionFrom(Clr(typeof(long)));
        await Assert.That(conversion!.Value.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(conversion.Value.Method).IsNotNull();
    }

    [Test]
    public async Task ResolveConversionFrom_Float32Pairs_AreNone() {
        // The closed set answers none, not unknown, for Float32: C# defines float to double, the CLR declares nothing.
        var single = Clr(typeof(float));
        foreach (var other in new[] { typeof(int), typeof(long), typeof(double) }) {
            await Assert.That(single.ResolveConversionFrom(Clr(other))).IsNull();
            await Assert.That(Clr(other).ResolveConversionFrom(single)).IsNull();
        }
    }

    [Test]
    public async Task ResolveConversionFrom_UnrelatedTypes_IsNone() {
        await Assert.That(Clr(typeof(string)).ResolveConversionFrom(Clr(typeof(long)))).IsNull();
        await Assert.That(Clr(typeof(bool)).ResolveConversionFrom(Clr(typeof(long)))).IsNull();
    }

    [Test]
    public async Task ResolveConversionFrom_Modeled_AnswersThroughThePublicDoor() {
        var set = new ModeledTypeSet();
        var money = set.Add("Money", ModeledTypeSet.Static("op_Implicit", "Money", "Cents"));
        var cents = set.Add("Cents");
        var conversion = ((ITypeDefinition)money).ResolveConversionFrom(cents);
        await Assert.That(conversion!.Value.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(conversion.Value.Method).IsNotNull();
        await Assert.That(((ITypeDefinition)cents).ResolveConversionFrom(money)).IsNull();
    }

    // ---- GetOperators ----

    [Test]
    public async Task GetOperators_Int64Add_YieldsIntrinsicLongLongToLong() {
        var longType = Clr(typeof(long));
        var operators = longType.GetOperators(OperatorKind.Add).ToArray();
        await Assert.That(operators.Length).IsEqualTo(1);
        await Assert.That(operators[0].PlatformApplied).IsTrue();
        await Assert.That(operators[0].Signature.MemberTypeDefinition).IsSameReferenceAs(longType);
        var parameters = ParametersOf(operators[0]);
        await Assert.That(parameters.Length).IsEqualTo(2);
        await Assert.That(parameters[0]).IsSameReferenceAs(longType);
        await Assert.That(parameters[1]).IsSameReferenceAs(longType);
        await Assert.That(operators[0].Signature.IsStatic).IsTrue();
    }

    [Test]
    public async Task GetOperators_Int64Comparison_YieldsIntrinsicBoolean() {
        var operators = Clr(typeof(long)).GetOperators(OperatorKind.LessThan).ToArray();
        await Assert.That(operators.Length).IsEqualTo(1);
        await Assert.That(operators[0].PlatformApplied).IsTrue();
        await Assert.That(operators[0].Signature.MemberTypeDefinition).IsSameReferenceAs(Clr(typeof(bool)));
    }

    [Test]
    public async Task GetOperators_DecimalAdd_YieldsDeclaredDecimalOperator() {
        var decimalType = Clr(typeof(decimal));
        var operators = decimalType.GetOperators(OperatorKind.Add).ToArray();
        await Assert.That(operators.Length).IsEqualTo(1);
        await Assert.That(operators[0].PlatformApplied).IsFalse();
        await Assert.That(operators[0].Signature.DeclaringTypeDefinition).IsSameReferenceAs(decimalType);
        await Assert.That(operators[0].Signature.MemberTypeDefinition).IsSameReferenceAs(decimalType);
        await Assert.That(ParametersOf(operators[0]).Length).IsEqualTo(2);
    }

    [Test]
    public async Task GetOperators_Float32_ArithmeticIsNone_ComparisonsAreClrDeclaredNeverIntrinsic() {
        // The closed set gives Float32 no intrinsic operator. The CLR itself declares Single's six comparisons,
        // which is a declared member, not a built-in rule (A9 expected none for these; reflection says otherwise).
        var single = Clr(typeof(float));
        foreach (var kind in new[] { OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.And, OperatorKind.Or, OperatorKind.Not })
            await Assert.That(single.GetOperators(kind)).IsEmpty();
        foreach (var kind in Enum.GetValues<OperatorKind>())
            await Assert.That(single.GetOperators(kind).Any(static declared => declared.PlatformApplied)).IsFalse();
        await Assert.That(single.GetOperators(OperatorKind.LessThan).Count()).IsEqualTo(1);
        // The sibling: Float64 does have intrinsic arithmetic, and its comparison is one intrinsic, not two.
        await Assert.That(Clr(typeof(double)).GetOperators(OperatorKind.Add).Single().PlatformApplied).IsTrue();
        await Assert.That(Clr(typeof(double)).GetOperators(OperatorKind.LessThan).Single().PlatformApplied).IsTrue();
    }

    [Test]
    public async Task GetOperators_ModeledValueType_FromDeclaredOpMember() {
        var set = new ModeledTypeSet();
        var vec = set.Add("Vec", ModeledTypeSet.Static("op_Addition", "Vec", "Vec", "Vec"));
        var operators = ((ITypeDefinition)vec).GetOperators(OperatorKind.Add).ToArray();
        await Assert.That(operators.Length).IsEqualTo(1);
        await Assert.That(operators[0].PlatformApplied).IsFalse();
        await Assert.That(operators[0].Signature.DeclaringTypeDefinition).IsSameReferenceAs(vec);
        await Assert.That(((ITypeDefinition)vec).GetOperators(OperatorKind.Subtract)).IsEmpty();
    }

    [Test]
    public async Task GetOperators_ModeledNonStaticOrWrongArity_IsIgnored() {
        var set = new ModeledTypeSet();
        var vec = set.Add("Vec",
            new Poly.Ast.Nodes.MethodDefinitionNode("op_Addition", new Poly.Ast.Nodes.NamedTypeReference("Vec"),
                [new Poly.Ast.Nodes.Parameter("a", new Poly.Ast.Nodes.NamedTypeReference("Vec"))], IsStatic: false),
            ModeledTypeSet.Static("op_Subtraction", "Vec", "Vec"));
        await Assert.That(((ITypeDefinition)vec).GetOperators(OperatorKind.Add)).IsEmpty();
        await Assert.That(((ITypeDefinition)vec).GetOperators(OperatorKind.Subtract)).IsEmpty();
    }

    // ---- GetCommonType (B9) ----

    private static ITypeDefinition?[] Common(params Type[][] sets) => [.. sets.Select(set => set.Select(Clr).GetCommonType())];

    [Test]
    public async Task GetCommonType_NumericSets() {
        var results = Common(
            [typeof(int), typeof(long)],
            [typeof(int), typeof(long), typeof(double)],
            [typeof(long), typeof(decimal)],
            [typeof(long), typeof(long)],
            [typeof(int)]);
        await Assert.That(results[0]).IsSameReferenceAs(Clr(typeof(long)));
        await Assert.That(results[1]).IsSameReferenceAs(Clr(typeof(double)));
        await Assert.That(results[2]).IsSameReferenceAs(Clr(typeof(decimal)));
        await Assert.That(results[3]).IsSameReferenceAs(Clr(typeof(long)));
        await Assert.That(results[4]).IsSameReferenceAs(Clr(typeof(int)));
    }

    [Test]
    public async Task GetCommonType_NoUniqueCommonType_IsNone() {
        var results = Common(
            [typeof(double), typeof(decimal)],
            [typeof(long), typeof(double), typeof(decimal)],
            [typeof(string), typeof(long)],
            [typeof(int), typeof(long), typeof(double), typeof(decimal)],
            []);
        foreach (var result in results)
            await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCommonType_NullInput_Throws() {
        await Assert.That(() => ((IEnumerable<ITypeDefinition>)null!).GetCommonType()).ThrowsExactly<ArgumentNullException>();
        await Assert.That(() => new ITypeDefinition[] { null! }.GetCommonType()).ThrowsExactly<ArgumentNullException>();
    }

    // ---- contract neutrality (R1, R6a) ----

    [Test]
    public async Task ConversionContract_ContainsNoRepresentationOrReflectionTypes() {
        var members = new List<MethodBase?> {
            typeof(ITypeDefinition).GetMethod(nameof(ITypeDefinition.ResolveConversionFrom), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
            typeof(ITypeDefinition).GetMethod(nameof(ITypeDefinition.GetOperators), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        };
        members.AddRange(typeof(TypeDefinitionOperatorExtensions)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static m => m.Name is "GetOperator" or "GetCommonType"));
        var signatureTypes = new List<Type>();
        foreach (var member in members) {
            await Assert.That(member).IsNotNull();
            signatureTypes.AddRange(member!.GetParameters().Select(static p => p.ParameterType));
            if (member is MethodInfo method)
                signatureTypes.Add(method.ReturnType);
        }
        foreach (var contract in new[] { typeof(TypeConversion), typeof(DeclaredOperator), typeof(OperatorResolution) }) {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            signatureTypes.AddRange(contract.GetProperties(all).Select(static p => p.PropertyType));
            signatureTypes.AddRange(contract.GetFields(all).Select(static f => f.FieldType));
        }
        var offenders = signatureTypes.SelectMany(Flatten).Distinct().Where(IsForbidden).Select(static t => t.FullName).ToArray();
        await Assert.That(offenders).IsEmpty();

        static IEnumerable<Type> Flatten(Type type) {
            yield return type;
            if (type.HasElementType)
                foreach (var inner in Flatten(type.GetElementType()!)) yield return inner;
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments().SelectMany(Flatten)) yield return argument;
        }

        static bool IsForbidden(Type type) =>
            type == typeof(Type)
            || type.Namespace is { } ns
                && (ns.StartsWith("System.Reflection", StringComparison.Ordinal)
                    || ns.StartsWith("System.Linq.Expressions", StringComparison.Ordinal)
                    || ns.StartsWith("Poly.Interpretation.Vm", StringComparison.Ordinal)
                    || ns.StartsWith("Poly.Introspection.CommonLanguageRuntime", StringComparison.Ordinal));
    }
}