using Poly.DomainModeling;
using Poly.DomainModeling.Libraries.Temporal;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using OntologyPrimitive = Poly.DomainModeling.Ontology.PrimitiveType;
using OntologyValueType = Poly.DomainModeling.Ontology.ValueType;
using Or = Poly.DomainModeling.Ontology.Or;
using Subtract = Poly.DomainModeling.Ontology.Subtract;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// T-03: the Introspection-backed type model. Resolve/Assign/KindOf and the syntax→kind
/// operator table. No production caller exists until T-04.
/// </summary>
public class DomainTypeModelTests {
    private static Domain BuildDomain() => DomainTestFactory.Create("Shop", [
        new EnumType("Status", ["Open", "Closed"], []),
        new Entity("Order", [new Property("Total", new DomainTypeReference("Decimal"), [])], [], [], []),
        new OntologyValueType("Money", [new Property("Amount", new DomainTypeReference("Decimal"), [])], []),
        new OntologyPrimitive("Cents", PrimitiveSemantics.Decimal, []),
        new OntologyPrimitive("Blob", PrimitiveSemantics.Binary, []),
        new OntologyPrimitive("Moment", PrimitiveSemantics.DateTime, [])
    ], []);

    private static DomainTypeModel Model(Action<TypeMappingRegistry>? configure = null) {
        var maps = new TypeMappingRegistry();
        TemporalTypeMaps.Apply(maps);
        configure?.Invoke(maps);
        return new DomainTypeModel(BuildDomain(), maps);
    }

    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)!;

    [Test]
    public async Task Resolve_DecimalAndNumber_AreDistinctDefinitions() {
        var model = Model();
        var decimalType = model.Resolve("Decimal");
        var number = model.Resolve("Number");
        await Assert.That(decimalType.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.Decimal);
        await Assert.That(number.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.Int64);
        await Assert.That(ReferenceEquals(decimalType.Definition, number.Definition)).IsFalse();
    }

    [Test]
    [Arguments("Number", Poly.Introspection.PrimitiveType.Int64)]
    [Arguments("Int", Poly.Introspection.PrimitiveType.Int64)]
    [Arguments("Int64", Poly.Introspection.PrimitiveType.Int64)]
    [Arguments("Int32", Poly.Introspection.PrimitiveType.Int32)]
    [Arguments("Text", Poly.Introspection.PrimitiveType.String)]
    [Arguments("String", Poly.Introspection.PrimitiveType.String)]
    [Arguments("Boolean", Poly.Introspection.PrimitiveType.Boolean)]
    [Arguments("Bool", Poly.Introspection.PrimitiveType.Boolean)]
    [Arguments("Float", Poly.Introspection.PrimitiveType.Float64)]
    [Arguments("Double", Poly.Introspection.PrimitiveType.Float64)]
    [Arguments("Uuid", Poly.Introspection.PrimitiveType.Guid)]
    [Arguments("Guid", Poly.Introspection.PrimitiveType.Guid)]
    [Arguments("Date", Poly.Introspection.PrimitiveType.DateOnly)]
    [Arguments("Time", Poly.Introspection.PrimitiveType.TimeOnly)]
    [Arguments("DateTime", Poly.Introspection.PrimitiveType.DateTime)]
    [Arguments("Duration", Poly.Introspection.PrimitiveType.TimeSpan)]
    public async Task Resolve_Aliases_MatchToClrTypeName(string name, Poly.Introspection.PrimitiveType expected) {
        var resolved = Model().Resolve(name);
        await Assert.That(resolved.IsResolved).IsTrue();
        await Assert.That(resolved.Definition!.PrimitiveType).IsEqualTo(expected);
        await Assert.That(resolved.DomainName).IsEqualTo(name);
    }

    [Test]
    public async Task Resolve_Enum_IsSyntheticEnumDefinition() {
        var status = Model().Resolve("Status");
        await Assert.That(status.Definition!.Kind).IsEqualTo(TypeKind.Enum);
        await Assert.That(status.DomainName).IsEqualTo("Status");
    }

    [Test]
    public async Task Resolve_EntityAndValueType_AreSyntheticClassAndStruct() {
        var model = Model();
        await Assert.That(model.Resolve("Order").Definition!.Kind).IsEqualTo(TypeKind.Class);
        await Assert.That(model.Resolve("Money").Definition!.Kind).IsEqualTo(TypeKind.Struct);
    }

    [Test]
    public async Task Resolve_UnknownName_IsUnresolved() {
        var unresolved = Model().Resolve("Nope");
        await Assert.That(unresolved).IsEqualTo(ExprType.Unresolved);
        await Assert.That(unresolved.Definition).IsNull();
        await Assert.That(unresolved.Optional).IsFalse();
        await Assert.That(unresolved.IsResolved).IsFalse();
        await Assert.That(Model().Resolve("Text")).IsNotEqualTo(ExprType.Unresolved);
    }

    [Test]
    public async Task Resolve_SyntheticDefinitions_AreMemoisedPerName() {
        var model = Model();
        await Assert.That(ReferenceEquals(model.Resolve("Order").Definition, model.Resolve("Order").Definition)).IsTrue();
        foreach (var name in new[] { "Status", "Order", "Money" })
            await Assert.That(model.Assign(model.Resolve(name), model.Resolve(name))).IsEqualTo(Assignability.Identity);
    }

    [Test]
    public async Task Resolve_DeclaredCustomNamedDecimalPrimitive_IsDecimal() {
        var cents = Model().Resolve("Cents");
        await Assert.That(cents.IsResolved).IsTrue();
        await Assert.That(cents.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.Decimal);
    }

    [Test]
    public async Task Resolve_Binary_IsResolved() {
        var blob = Model().Resolve("Blob");
        await Assert.That(blob.IsResolved).IsTrue();
        await Assert.That(blob.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.ByteArray);
        await Assert.That(Model().Resolve("Binary").Definition!.PrimitiveType)
            .IsEqualTo(Poly.Introspection.PrimitiveType.ByteArray);
    }

    [Test]
    public async Task Resolve_PackOverrideOfDate_IsHonoured() {
        var model = Model(maps => maps.OverrideClrTypeName("Date", "DateTime"));
        await Assert.That(model.Resolve("Date").Definition!.PrimitiveType)
            .IsEqualTo(Poly.Introspection.PrimitiveType.DateTime);
    }

    [Test]
    public async Task Resolve_DeclaredTemporalPrimitive_GoesThroughSemanticsName() {
        var moment = Model().Resolve("Moment");
        await Assert.That(moment.Definition!.PrimitiveType).IsEqualTo(Poly.Introspection.PrimitiveType.DateTime);
    }

    [Test]
    public async Task Unresolved_IsNeverAssignableOrComparable() {
        var model = Model();
        foreach (var name in new[] { "Number", "Text", "Status", "Order" }) {
            var x = model.Resolve(name);
            await Assert.That(model.Assign(ExprType.Unresolved, x)).IsEqualTo(Assignability.None);
            await Assert.That(model.Assign(x, ExprType.Unresolved)).IsEqualTo(Assignability.None);
        }
        await Assert.That(model.Assign(ExprType.Unresolved, ExprType.Unresolved)).IsEqualTo(Assignability.None);
    }

    [Test]
    public async Task OfProperty_OptionalConstraint_SetsOptional() {
        var model = Model();
        var optional = model.OfProperty(new Property("Note", new DomainTypeReference("Text"), [new OptionalConstraint()]));
        var required = model.OfProperty(new Property("Note", new DomainTypeReference("Text"), []));
        await Assert.That(optional.Optional).IsTrue();
        await Assert.That(required.Optional).IsFalse();
        await Assert.That(ReferenceEquals(optional.Definition, required.Definition)).IsTrue();
        await Assert.That(optional.DomainName).IsEqualTo("Text");
    }

    [Test]
    public async Task Assign_Identity() {
        var model = Model();
        foreach (var name in new[] { "Number", "Text", "Status" })
            await Assert.That(model.Assign(model.Resolve(name), model.Resolve(name))).IsEqualTo(Assignability.Identity);
    }

    [Test]
    public async Task Assign_IgnoresOptionality() {
        // Optionality is the caller's check (T-05/T-06); this verdict is about the definition only
        // and is not a soundness claim.
        var model = Model();
        var number = model.Resolve("Number");
        var optional = number with { Optional = true };
        await Assert.That(model.Assign(optional, number)).IsEqualTo(model.Assign(number, number));
    }

    [Test]
    public async Task Assign_NumberToDecimal_IsWidening() {
        var model = Model();
        var door = Clr(typeof(decimal)).GetConversionFrom(Clr(typeof(long)));
        await Assert.That(door?.Kind).IsEqualTo(ConversionOperatorKind.Implicit);
        await Assert.That(model.Assign(model.Resolve("Number"), model.Resolve("Decimal"))).IsEqualTo(Assignability.Widening);
    }

    [Test]
    public async Task Assign_DecimalToNumber_IsRoundingNarrowing() {
        var model = Model();
        await Assert.That(model.Assign(model.Resolve("Decimal"), model.Resolve("Number"))).IsEqualTo(Assignability.RoundingNarrowing);
    }

    [Test]
    [Arguments("Int32", "Int64", "Widening")]
    [Arguments("Int32", "Float", "Widening")]
    [Arguments("Int32", "Decimal", "Widening")]
    [Arguments("Number", "Int32", "RoundingNarrowing")]
    [Arguments("Number", "Float", "Widening")]
    [Arguments("Number", "Decimal", "Widening")]
    [Arguments("Float", "Int32", "RoundingNarrowing")]
    [Arguments("Float", "Number", "RoundingNarrowing")]
    [Arguments("Float", "Decimal", "RoundingNarrowing")]
    [Arguments("Decimal", "Int32", "RoundingNarrowing")]
    [Arguments("Decimal", "Number", "RoundingNarrowing")]
    [Arguments("Decimal", "Float", "RoundingNarrowing")]
    [Arguments("Int32", "Int32", "Identity")]
    [Arguments("Number", "Number", "Identity")]
    [Arguments("Float", "Float", "Identity")]
    [Arguments("Decimal", "Decimal", "Identity")]
    public async Task Assign_NumericMatrix_IsClosedAndPinned(string from, string to, string expectedName) {
        var expected = Enum.Parse<Assignability>(expectedName);
        var model = Model();
        await Assert.That(model.Assign(model.Resolve(from), model.Resolve(to))).IsEqualTo(expected);
    }

    [Test]
    public async Task Assign_DoubleToDecimal_IsNotWidening_EvenThoughIsNumericWideningFromSaysYes() {
        var model = Model();
        var floatType = model.Resolve("Float");
        var decimalType = model.Resolve("Decimal");
        await Assert.That(decimalType.Definition!.IsNumericWideningFrom(floatType.Definition!)).IsTrue();
        await Assert.That(model.Assign(floatType, decimalType)).IsEqualTo(Assignability.RoundingNarrowing);
    }

    [Test]
    [Arguments("Text", "Number")]
    [Arguments("Boolean", "Number")]
    [Arguments("Status", "Text")]
    [Arguments("Number", "Text")]
    [Arguments("Order", "Money")]
    [Arguments("Uuid", "Number")]
    public async Task Assign_UnrelatedTypes_IsNone(string from, string to) {
        var model = Model();
        await Assert.That(model.Assign(model.Resolve(from), model.Resolve(to))).IsEqualTo(Assignability.None);
    }

    [Test]
    [Arguments("Number", "Integer64")]
    [Arguments("Int32", "Integer64")]
    [Arguments("Decimal", "Decimal")]
    [Arguments("Float", "Float64")]
    [Arguments("Text", "None")]
    [Arguments("Boolean", "None")]
    [Arguments("Status", "None")]
    [Arguments("Order", "None")]
    [Arguments("Date", "None")]
    [Arguments("Nope", "None")]
    public async Task KindOf_MapsNumericDefinitions(string name, string expectedName) {
        var expected = Enum.Parse<NumericKind>(expectedName);
        var model = Model();
        await Assert.That(model.KindOf(model.Resolve(name))).IsEqualTo(expected);
    }

    private static readonly (DomainExpression Syntax, OperatorKind Expected)[] OperatorCases = [
        (new Add(new Literal(1), new Literal(2)), OperatorKind.Add),
        (new Subtract(new Literal(1), new Literal(2)), OperatorKind.Subtract),
        (new Multiply(new Literal(1), new Literal(2)), OperatorKind.Multiply),
        (new Divide(new Literal(1), new Literal(2)), OperatorKind.Divide),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.Equal), OperatorKind.Equal),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.NotEqual), OperatorKind.NotEqual),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.LessThan), OperatorKind.LessThan),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.LessThanOrEqual), OperatorKind.LessThanOrEqual),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.GreaterThan), OperatorKind.GreaterThan),
        (new Comparison(new Literal(1), new Literal(2), ComparisonKind.GreaterThanOrEqual), OperatorKind.GreaterThanOrEqual),
        (new And(new Literal(true), new Literal(false)), OperatorKind.And),
        (new Or(new Literal(true), new Literal(false)), OperatorKind.Or),
        (new Not(new Literal(true)), OperatorKind.Not)
    ];

    [Test]
    public async Task OperatorTable_MapsEverySyntaxToExactlyOneOperatorKind() {
        var seen = new List<OperatorKind>();
        foreach (var (syntax, expected) in OperatorCases) {
            await Assert.That(OperatorTable.TryKindOf(syntax, out var kind)).IsTrue();
            await Assert.That(kind).IsEqualTo(expected);
            seen.Add(kind);
        }
        await Assert.That(seen.Distinct().Count()).IsEqualTo(OperatorCases.Length);
        await Assert.That(seen.Order().ToArray()).IsEquivalentTo(Enum.GetValues<OperatorKind>().Order().ToArray());
    }

    [Test]
    public async Task OperatorKind_HasExactlyTheThirteenKnownMembers() {
        string[] expected = [
            "Add", "Subtract", "Multiply", "Divide", "Equal", "NotEqual", "LessThan", "LessThanOrEqual",
            "GreaterThan", "GreaterThanOrEqual", "And", "Or", "Not"
        ];
        await Assert.That(Enum.GetNames<OperatorKind>().Order().ToArray()).IsEquivalentTo(expected.Order().ToArray());
    }

    [Test]
    public async Task OperatorTable_NonOperatorNodes_ReturnFalse() {
        await Assert.That(OperatorTable.TryKindOf(new Literal(1), out _)).IsFalse();
        await Assert.That(OperatorTable.TryKindOf(new PropertyAccess("Total"), out _)).IsFalse();
    }

    [Test]
    public async Task OperatorTable_UndefinedComparisonKind_FailsClosed() {
        await Assert.That(() => OperatorTable.KindOf((ComparisonKind)99)).Throws<ArgumentOutOfRangeException>();
        var malformed = new Comparison(new Literal(1), new Literal(2), (ComparisonKind)99);
        await Assert.That(() => OperatorTable.TryKindOf(malformed, out _)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task OperatorTable_NullSyntax_Throws() {
        await Assert.That(() => OperatorTable.TryKindOf(null!, out _)).Throws<ArgumentNullException>();
    }
}