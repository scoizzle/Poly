using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a type is, asked of the system that owns the question (#998 T8a, decision §3): C#'s rule (its simple types,
/// enums and value semantics) and its words <c>string</c> and <c>object</c>; the CLR host's own table for the types C#
/// has no simple type for (the calendar types, Guid, byte[]); a foreign host's own rule; the domain's declarations.
/// Each row restates, through its owner, a fact a definition used to answer about itself (TypeAnswerTests,
/// GroundTypeTests), so the facts survive the answers' removal in T8b.
/// </summary>
public class TypeRuleOwnerTests {
    private static readonly CSharpTypeSystem CSharp = CSharpTypeSystem.Shared;

    // The owners' answers, as a string: C#'s simple type, then the facts that hold, in a fixed order.
    private static string Owned(ITypeDefinition t) {
        var facts = new (string Name, bool Value)[] {
            ("enum", CSharp.IsEnumType(t)), ("value", CSharp.HasValueSemantics(t)),
            ("string", CSharp.IsSame(CSharp.Bind("string")!, t)), ("object", CSharp.IsSame(CSharp.Bind("object")!, t)),
            ("calendar", ClrBuiltInRules.IsCalendarType(t)), ("timestamp", ClrBuiltInRules.IsTimestamp(t)),
            ("guid", ClrBuiltInRules.IsGuid(t)), ("bytes", ClrBuiltInRules.IsByteArray(t)),
        };
        var parts = new List<string>();
        if (CSharp.SimpleTypeOf(t) is { } simple)
            parts.Add(simple.ToString());
        parts.AddRange(facts.Where(static f => f.Value).Select(static f => f.Name));
        return string.Join(" ", parts);
    }

    public static IEnumerable<(Type Type, string Expected)> ClrRows() => [
        (typeof(bool), "Bool value"),
        (typeof(sbyte), "SByte value"),
        (typeof(short), "Short value"),
        (typeof(int), "Int value"),
        (typeof(long), "Long value"),
        (typeof(byte), "Byte value"),
        (typeof(ushort), "UShort value"),
        (typeof(uint), "UInt value"),
        (typeof(ulong), "ULong value"),
        (typeof(float), "Float value"),
        (typeof(double), "Double value"),
        (typeof(decimal), "Decimal value"),
        (typeof(nint), "NInt value"),
        (typeof(nuint), "NUInt value"),
        (typeof(string), "string"),
        (typeof(char), "Char value"),
        (typeof(DateOnly), "value calendar"),
        (typeof(TimeOnly), "value calendar"),
        (typeof(DateTime), "value calendar timestamp"),
        (typeof(DateTimeOffset), "value calendar timestamp"),
        (typeof(TimeSpan), "value calendar"),
        (typeof(Guid), "value guid"),
        (typeof(byte[]), "bytes"),
        (typeof(object), "object"),
        // Not ground: an enum and a nullable are no simple type of their own.
        (typeof(DayOfWeek), "enum value"),
        (typeof(int?), "value"),
        (typeof(KeyValuePair<int, int>), "value"),
        (typeof(IDisposable), ""),
        (typeof(System.Action), ""),
        (typeof(Delegate), ""),
        (typeof(Uri), ""),
        (typeof(int[]), ""),
    ];

    [Test]
    [MethodDataSource(nameof(ClrRows))]
    public async Task Clr_ItsOwnersStateWhatItIs(Type type, string expected) =>
        await Assert.That(Owned(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))).IsEqualTo(expected);

    [Test]
    public async Task Clr_EveryGroundType_HasARow() {
        var rows = ClrRows().Select(static row => row.Type).ToHashSet();
        foreach (var type in ClrBuiltInTypes.All)
            await Assert.That(rows).Contains(type);
    }

    [Test]
    [Arguments(TypeDeclarationForm.Class, "")]
    [Arguments(TypeDeclarationForm.Struct, "value")]
    [Arguments(TypeDeclarationForm.Interface, "")]
    [Arguments(TypeDeclarationForm.Enum, "enum")]
    public async Task Declaration_CSharpStatesWhatItsFormDeclares(TypeDeclarationForm form, string expected) =>
        await Assert.That(Owned(new AstTypeDefinition(new TypeDefinitionNode("T", Form: form), new AstTypeRegistry()))).IsEqualTo(expected);

    [Test]
    public async Task SecondHost_ItsOwnRuleStatesItsNumbers_AndCSharpStatesNothing() {
        var host = new LedgerHost();
        var rules = ((ITypeSystem)host).Rule<LedgerHost.ILedgerRules>()!;

        await Assert.That(rules.MinorUnits(host.Amount)).IsEqualTo(2);
        await Assert.That(rules.MinorUnits(host.Account)).IsNull();
        await Assert.That(Owned(host.Amount)).IsEqualTo("").Because("C# and the CLR host state nothing about a foreign type");
    }

    [Test]
    public async Task DomainDeclarations_AreCSharpsEnumStructAndClass() {
        var domain = DomainTestFactory.Create("Shop", [
            new EnumType("Status", ["Open", "Closed"], []),
            new Entity("Order", [], [], [], []),
            new Poly.DomainModeling.Ontology.ValueType("Money", [], []),
        ], []);
        var model = new Poly.DomainModeling.Meaning.DomainTypeModel(domain, new TypeMappingRegistry(), CSharp);

        await Assert.That(Owned(model.Resolve("Status").Definition!)).IsEqualTo("enum");
        await Assert.That(Owned(model.Resolve("Money").Definition!)).IsEqualTo("value");
        await Assert.That(Owned(model.Resolve("Order").Definition!)).IsEqualTo("");
    }
}