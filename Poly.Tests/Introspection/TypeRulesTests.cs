using System.Reflection;

using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
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

    /// <summary>
    /// T-16: the 21 pairs the private rank table called widening and the C# specification (10.2.3) does not:
    /// a signed integral to an unsigned one, an unsigned one to its signed sibling, anything into Char, and
    /// Float32 or Float64 into Decimal (explicit). The provider answers none, so overload scoring stops counting them.
    /// </summary>
    internal static readonly (Prim From, Prim To)[] RemovedWidenings = [
        (Prim.UInt8, Prim.Int8), (Prim.UInt16, Prim.Int16), (Prim.UInt32, Prim.Int32), (Prim.UInt64, Prim.Int64), (Prim.Char, Prim.Int16),
        (Prim.Int8, Prim.UInt8), (Prim.Int8, Prim.UInt16), (Prim.Int8, Prim.UInt32), (Prim.Int8, Prim.UInt64),
        (Prim.Int16, Prim.UInt16), (Prim.Int16, Prim.UInt32), (Prim.Int16, Prim.UInt64),
        (Prim.Int32, Prim.UInt32), (Prim.Int32, Prim.UInt64), (Prim.Int64, Prim.UInt64),
        (Prim.Int8, Prim.Char), (Prim.Int16, Prim.Char), (Prim.UInt8, Prim.Char), (Prim.UInt16, Prim.Char),
        (Prim.Float32, Prim.Decimal), (Prim.Float64, Prim.Decimal),
    ];

    private static bool IsRankedNumber(Prim primitive) => primitive is
        Prim.Int8 or Prim.Int16 or Prim.Int32 or Prim.Int64 or Prim.UInt8 or Prim.UInt16 or Prim.UInt32 or Prim.UInt64
        or Prim.Float32 or Prim.Float64 or Prim.Decimal or Prim.Char;

    /// <summary>
    /// The matrix captured from master is the oracle; this reads it through the provider alone. Letter 1 is a declared
    /// operator (a conversion that states a method), letter 2 is assignability, letter 3 is "implicitly convertible":
    /// the same ranked number, or any implicit conversion into a non-object target. The provider reproduces every cell
    /// except the 21 removed widenings, each of which master's rank table said yes to and the specification says no.
    /// </summary>
    [Test]
    public async Task ConversionDoor_ProviderReproducesTheMasterMatrix_ExceptTheRemovedWidenings() {
        var prims = Enum.GetValues<Prim>();
        var removed = RemovedWidenings.ToHashSet();
        await Assert.That(removed.Count).IsEqualTo(21);
        var mismatches = new List<string>();
        foreach (var to in prims) {
            var cells = MasterDoorMatrix[to].Split(' ');
            var target = Clr(to.GetClrType()!);
            for (var i = 0; i < prims.Length; i++) {
                var from = prims[i];
                var source = Clr(from.GetClrType()!);
                var conversion = target.ResolveConversionFrom(source);
                var declared = conversion is { Method: not null } stated
                    ? (stated.Kind == ConversionOperatorKind.Implicit ? 'I' : 'E')
                    : 'N';
                var convertible = to != Prim.Structure && ((from == to && IsRankedNumber(from)) || conversion is { Kind: ConversionOperatorKind.Implicit });
                var actual = string.Concat(declared, target.IsAssignableFrom(source) ? 'T' : 'F', convertible ? 'T' : 'F');
                var expected = string.Concat(cells[i][0], cells[i][1], cells[i][2] == 'T' && !removed.Contains((from, to)) ? 'T' : 'F');
                if (actual != expected)
                    mismatches.Add($"{from} -> {to}: expected {expected}, was {actual}");
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
    public async Task ResolveConversionFrom_Float32Pairs_AreIntrinsic() {
        // T-24 flipped this pin: C# defines every Float32 conversion, and the CLR declares none of them.
        var single = Clr(typeof(float));
        foreach (var (from, to, kind) in new[] {
            (typeof(int), typeof(float), ConversionOperatorKind.Implicit),
            (typeof(long), typeof(float), ConversionOperatorKind.Implicit),
            (typeof(float), typeof(double), ConversionOperatorKind.Implicit),
            (typeof(float), typeof(int), ConversionOperatorKind.Explicit),
            (typeof(float), typeof(long), ConversionOperatorKind.Explicit),
            (typeof(double), typeof(float), ConversionOperatorKind.Explicit) }) {
            var conversion = Clr(to).ResolveConversionFrom(Clr(from));
            await Assert.That(conversion?.Kind).IsEqualTo(kind);
            await Assert.That(conversion!.Value.Method).IsNull();
        }
        await Assert.That(single.ResolveConversionFrom(single)).IsNull();
    }

    // ---- T-24: the numeric conversion table, with the C# binder as the oracle ----

    private static readonly Type[] NumericTypes = [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(char), typeof(float), typeof(double), typeof(decimal)
    ];

    private static string NameOf(Type type) => type.GetPrimitiveType()?.GetDisplayName() ?? type.Name;

    private static T ImplicitTo<T>(object value) {
        dynamic operand = value;
        T converted = operand;
        return converted;
    }

    private static T ExplicitTo<T>(object value) {
        dynamic operand = value;
        return (T)operand;
    }

    /// <summary>The conversion the C# runtime binder applies to a boxed (non-constant) value of <paramref name="from"/>.</summary>
    private static ConversionOperatorKind? BinderConversion(Type from, Type to) {
        var value = Convert.ChangeType(1, from, System.Globalization.CultureInfo.InvariantCulture);
        if (Binds(nameof(ImplicitTo)))
            return ConversionOperatorKind.Implicit;
        return Binds(nameof(ExplicitTo)) ? ConversionOperatorKind.Explicit : null;

        bool Binds(string name) {
            var method = typeof(TypeRulesTests).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(to);
            try {
                method.Invoke(null, [value]);
                return true;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) {
                return false;
            }
        }
    }

    [Test]
    public async Task ResolveConversionFrom_NumericTable_MatchesTheCSharpBinder() {
        var mismatches = new List<string>();
        var (implicitCount, explicitCount) = (0, 0);
        foreach (var from in NumericTypes) {
            foreach (var to in NumericTypes) {
                var conversion = Clr(to).ResolveConversionFrom(Clr(from));
                if (from == to) {
                    if (conversion is not null)
                        mismatches.Add($"{NameOf(from)} -> {NameOf(to)}: identity is not a conversion, provider {conversion.Value.Kind}");
                    continue;
                }
                var expected = BinderConversion(from, to);
                implicitCount += expected is ConversionOperatorKind.Implicit ? 1 : 0;
                explicitCount += expected is ConversionOperatorKind.Explicit ? 1 : 0;
                if (conversion?.Kind != expected) {
                    mismatches.Add($"{NameOf(from)} -> {NameOf(to)}: C# {expected?.ToString() ?? "none"}, provider {conversion?.Kind.ToString() ?? "none"}");
                    continue;
                }
                // Decimal's conversions are CLR-declared members; every other numeric conversion is built in.
                var declared = from == typeof(decimal) || to == typeof(decimal);
                if (conversion is { } found && (found.Method is not null) != declared)
                    mismatches.Add($"{NameOf(from)} -> {NameOf(to)}: method {(found.Method is null ? "none" : "declared")}, expected {(declared ? "declared" : "none")}");
            }
        }
        await Assert.That(mismatches).IsEmpty();
        // The oracle itself: §10.2.3's 42 implicit pairs plus decimal's 9; every other ordered pair is explicit (§10.3.2).
        await Assert.That(implicitCount).IsEqualTo(51);
        await Assert.That(explicitCount).IsEqualTo(81);
    }

    [Test]
    public async Task ResolveConversionFrom_NothingConvertsImplicitlyIntoChar_AndInt32ToCharIsExplicit() {
        var character = Clr(typeof(char));
        foreach (var from in NumericTypes.Where(static t => t != typeof(char)))
            await Assert.That(character.ResolveConversionFrom(Clr(from))?.Kind).IsNotEqualTo(ConversionOperatorKind.Implicit);
        var narrowing = character.ResolveConversionFrom(Clr(typeof(int)));
        await Assert.That(narrowing?.Kind).IsEqualTo(ConversionOperatorKind.Explicit);
        await Assert.That(narrowing!.Value.Method).IsNull();
        // The sibling: Char widens to Int32.
        await Assert.That(Clr(typeof(int)).ResolveConversionFrom(character)?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
    }

    [Test]
    public async Task ResolveConversionFrom_ClrToObject_IsImplicit() {
        var objectType = Clr(typeof(object));
        foreach (var source in new[] { typeof(int), typeof(string), typeof(Guid), typeof(DayOfWeek), typeof(List<int>) }) {
            var conversion = objectType.ResolveConversionFrom(Clr(source));
            await Assert.That(conversion?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
            await Assert.That(conversion!.Value.Method).IsNull();
        }
        // Identity is not a conversion, a modeled definition does not box, and nothing leaves Object implicitly.
        await Assert.That(objectType.ResolveConversionFrom(objectType)).IsNull();
        await Assert.That(objectType.ResolveConversionFrom(new ModeledTypeSet().Add("Money"))).IsNull();
        await Assert.That(Clr(typeof(int)).ResolveConversionFrom(objectType)).IsNull();
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
    public async Task GetOperators_Float32_ArithmeticAndComparisonsAreIntrinsic_TheClrDeclaredComparisonIsSubsumed() {
        // T-24 flipped this pin: Float32 joins the built-in set. The CLR also declares Single's six comparisons with
        // the intrinsic's exact signature, so the intrinsic is the one answer, as for Double.
        var single = Clr(typeof(float));
        foreach (var kind in new[] { OperatorKind.And, OperatorKind.Or, OperatorKind.Not, OperatorKind.BitwiseAnd, OperatorKind.BitwiseNot, OperatorKind.ShiftLeft })
            await Assert.That(single.GetOperators(kind)).IsEmpty();
        foreach (var kind in new[] { OperatorKind.Add, OperatorKind.Subtract, OperatorKind.Multiply, OperatorKind.Divide, OperatorKind.Modulo, OperatorKind.LessThan, OperatorKind.Equal }) {
            var declared = single.GetOperators(kind).Single();
            await Assert.That(declared.PlatformApplied).IsTrue();
            await Assert.That(ParametersOf(declared).All(p => ReferenceEquals(p, single))).IsTrue();
        }
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

    // ---- type identity: the notion IsAssignableFrom uses (fresh synthetic collection per resolve) ----

    [Test]
    public async Task GetCommonType_TwoEqualSyntheticCollections_ReturnsThatType() {
        var element = Clr(typeof(int));
        ITypeDefinition first = new AstCollectionTypeDefinition(element, CollectionKind.List);
        ITypeDefinition second = new AstCollectionTypeDefinition(element, CollectionKind.List);
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        var common = new[] { first, second }.GetCommonType();
        await Assert.That(common).IsNotNull();
        await Assert.That(ReferenceEquals(common, first) || ReferenceEquals(common, second)).IsTrue();
        // The sibling: collections of different elements, or kinds, have no common type.
        ITypeDefinition other = new AstCollectionTypeDefinition(Clr(typeof(long)), CollectionKind.List);
        await Assert.That(new[] { first, other }.GetCommonType()).IsNull();
    }

    [Test]
    public async Task GetOperator_IdentityStep_TreatsEqualSyntheticCollectionsAsIdentical() {
        var set = new ModeledTypeSet();
        var bag = set.Add("Bag", new MethodDefinitionNode("op_Addition", new NamedTypeReference("Bag"),
            [new Parameter("a", new NamedTypeReference("Bag")), new Parameter("b", new CollectionTypeReference(new NamedTypeReference("Bag"), CollectionKind.List))],
            IsStatic: true));
        ITypeDefinition bagType = bag;
        ITypeDefinition freshList = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        var resolution = bagType.GetOperator(OperatorKind.Add, freshList);
        await Assert.That(resolution).IsNotNull();
        await Assert.That(resolution!.Value.Result).IsSameReferenceAs(bag);
    }

    // ---- PlatformApplied: true for every simple type but decimal, whose operators the CLR declares ----

    [Test]
    public async Task Comparisons_Float32Float64AndInt32_ArePlatformApplied_WithIdenticalResults_DecimalIsDeclared() {
        // T-24 flipped this pin: Float32's comparisons were the CLR-declared members; the built-in set now answers them.
        var boolean = Clr(typeof(bool));
        foreach (var (type, platform) in new[] { (typeof(float), true), (typeof(double), true), (typeof(int), true), (typeof(decimal), false) }) {
            var definition = Clr(type);
            var operators = definition.GetOperators(OperatorKind.LessThan).ToArray();
            await Assert.That(operators.Length).IsEqualTo(1);
            await Assert.That(operators[0].PlatformApplied).IsEqualTo(platform);
            await Assert.That(operators[0].Signature.MemberTypeDefinition).IsSameReferenceAs(boolean);
            var resolution = definition.GetOperator(OperatorKind.LessThan, definition);
            await Assert.That(resolution!.Value.PlatformApplied).IsEqualTo(platform);
            await Assert.That(resolution.Value.Result).IsSameReferenceAs(boolean);
        }
    }

    [Test]
    public async Task ResolveConversionFrom_Modeled_FreshEqualSyntheticCollectionParameter_IsFound() {
        var set = new ModeledTypeSet();
        var bag = set.Add("Bag", new MethodDefinitionNode("op_Implicit", new NamedTypeReference("Bag"),
            [new Parameter("a", new CollectionTypeReference(new NamedTypeReference("Bag"), CollectionKind.List))], IsStatic: true));
        ITypeDefinition fresh = new AstCollectionTypeDefinition(bag, CollectionKind.List);
        var conversion = ((ITypeDefinition)bag).ResolveConversionFrom(fresh);
        await Assert.That(conversion).IsNotNull();
        await Assert.That(conversion!.Value.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
    }
}