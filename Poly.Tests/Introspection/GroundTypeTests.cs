using Poly.Interpretation.CSharp;
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
        foreach (var type in ClrBuiltInTypes.All)
            await Assert.That(ClrBuiltInRules.IsPrimitive(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))).IsTrue().Because($"'{type}' is one of the CLR host's primitives");

        Type[] composed = [typeof(Uri), typeof(int?), typeof(DayOfWeek), typeof(List<int>), typeof(KeyValuePair<int, int>), typeof(Action), typeof(int[])];
        foreach (var type in composed)
            await Assert.That(ClrBuiltInRules.IsPrimitive(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type))).IsFalse().Because($"'{type}' is not one of the CLR host's primitives");
    }

    [Test]
    public async Task ShiftCountPolicy_AskedOfTheCSharpRule_AgreesWithTheRuntimeTypes_ForEveryClrGroundType() {
        foreach (var type in ClrBuiltInTypes.All) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
            var byFact = CSharpBuiltIns.IsPolicyShiftCount(CSharpTypeSystem.Shared.SimpleTypeOf(definition));
            var byRepresentation = type == typeof(long) || type == typeof(uint) || type == typeof(ulong)
                || type == typeof(nint) || type == typeof(nuint);

            await Assert.That(byFact).IsEqualTo(byRepresentation)
                .Because($"'{type}' is a policy shift count by its representation exactly when the runtime type says so");
        }
    }

    [Test]
    public async Task IntegerFolding_AskedOfTheCSharpRule_IsExactlyTheFixedWidthIntegers() {
        foreach (var type in ClrBuiltInTypes.All) {
            ITypeDefinition definition = ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);
            var byFact = CSharpTypeSystem.Shared.SimpleTypeOf(definition) is
                SimpleType.SByte or SimpleType.Byte
                or SimpleType.Short or SimpleType.UShort
                or SimpleType.Int or SimpleType.UInt
                or SimpleType.Long or SimpleType.ULong;
            var byRepresentation = type == typeof(sbyte) || type == typeof(byte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong);

            await Assert.That(byFact).IsEqualTo(byRepresentation)
                .Because($"'{type}': the fixed-width integers, never a native-sized one, a float, a decimal or a char");
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
            .IsEqualTo(ClrBuiltInTypes.All.Length).Because("every CLR ground type has a row");
        foreach (var (type, ground) in table)
            await Assert.That(Poly.DomainModeling.Meaning.DomainTypeSystem.WordOf(ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type)))
                .IsEqualTo(ground).Because($"'{type}'");
    }
}