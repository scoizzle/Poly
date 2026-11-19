using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a ground type is, asked of its owners (#998 T8): the CLR host's own primitive table, C#'s simple types, the
/// domain's words. No definition answers it about itself; TypeRuleOwnerTests pins each CLR type's facts.
/// </summary>
public class GroundTypeTests {
    [Test]
    public async Task ClrPrimitives_AreTheHostsOwnTable_AndComposedTypesAreNot() {
        Type[] ground = [
            typeof(bool), typeof(sbyte), typeof(short), typeof(int), typeof(long), typeof(byte), typeof(ushort), typeof(uint),
            typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(char), typeof(DateTime),
            typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan), typeof(Guid), typeof(DateTimeOffset), typeof(byte[]),
            typeof(object), typeof(nint), typeof(nuint),
        ];
        foreach (var type in ground)
            await Assert.That(ClrBuiltInRules.IsPrimitive(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))).IsTrue().Because($"'{type}' is one of the CLR host's primitives");

        Type[] composed = [typeof(Uri), typeof(int?), typeof(DayOfWeek), typeof(List<int>), typeof(KeyValuePair<int, int>), typeof(Action), typeof(int[])];
        foreach (var type in composed)
            await Assert.That(ClrBuiltInRules.IsPrimitive(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))).IsFalse().Because($"'{type}' is not one of the CLR host's primitives");
    }

    [Test]
    public async Task ShiftCountPolicy_AskedOfTheCSharpRule_AgreesWithTheClrTable_ForEveryClrGroundType() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = Poly.Interpretation.Analysis.Semantics.SyntaxTypeCompatibilityAnalyzer.IsPolicyShiftCount(
                Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.SimpleTypeOf(definition));

            await Assert.That(byFact).IsEqualTo(ClrBuiltInRules.IsPolicyShiftCount(primitive))
                .Because($"'{primitive}' is a policy shift count by its representation exactly when the CLR table says so");
        }
    }

    [Test]
    public async Task IntegerFolding_AskedOfTheCSharpRule_IsExactlyTheFixedWidthIntegers() {
        foreach (var primitive in Enum.GetValues<ClrPrimitive>()) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(primitive.GetClrType()!);
            var byFact = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.SimpleTypeOf(definition) is
                Poly.Interpretation.CSharp.SimpleType.SByte or Poly.Interpretation.CSharp.SimpleType.Byte
                or Poly.Interpretation.CSharp.SimpleType.Short or Poly.Interpretation.CSharp.SimpleType.UShort
                or Poly.Interpretation.CSharp.SimpleType.Int or Poly.Interpretation.CSharp.SimpleType.UInt
                or Poly.Interpretation.CSharp.SimpleType.Long or Poly.Interpretation.CSharp.SimpleType.ULong;

            await Assert.That(byFact).IsEqualTo(primitive.IsInteger())
                .Because($"'{primitive}': the fixed-width integers, never a native-sized one, a float, a decimal or a char");
        }
    }

    [Test]
    public async Task DomainWord_OfEveryClrGroundType_IsTheDomainsOwnRule() {
        (Type Type, string? Ground)[] table = [
            (typeof(string), "Text"),
            (typeof(int), "Int32"),
            (typeof(long), "Number"),
            (typeof(float), "Float32"),
            (typeof(double), "Float"),
            (typeof(decimal), "Decimal"),
            (typeof(bool), "Boolean"),
            (typeof(DateOnly), "Date"),
            (typeof(DateTime), "DateTime"),
            (typeof(TimeOnly), "Time"),
            (typeof(TimeSpan), "Duration"),
            (typeof(Guid), "Uuid"),
            (typeof(byte[]), "Binary"),
            // Ground types the domain language has no rule for: they must not join a family because a fact exists.
            (typeof(char), null), (typeof(sbyte), null), (typeof(short), null), (typeof(byte), null), (typeof(ushort), null),
            (typeof(uint), null), (typeof(ulong), null), (typeof(nint), null), (typeof(nuint), null),
            (typeof(DateTimeOffset), null), (typeof(object), null),
            (typeof(Uri), null), (typeof(DayOfWeek), null), (typeof(int?), null),
        ];

        await Assert.That(table.Select(static row => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(row.Type)).Count(ClrBuiltInRules.IsPrimitive))
            .IsEqualTo(Enum.GetValues<ClrPrimitive>().Length).Because("every CLR ground type has a row");
        foreach (var (type, ground) in table)
            await Assert.That(Poly.DomainModeling.Meaning.DomainTypeSystem.WordOf(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)))
                .IsEqualTo(ground).Because($"'{type}'");
    }

}