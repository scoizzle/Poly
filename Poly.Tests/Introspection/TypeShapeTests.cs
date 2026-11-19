using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What shape a type has, the definition answers (#998): answers that are not exclusive, not a closed list of kinds.
/// </summary>
public class TypeShapeTests {
    private static ITypeDefinition Clr<T>() => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(T));

    [Test]
    public async Task ClrEnum_AnswersThatItIsAnEnum() {
        await Assert.That(Clr<DayOfWeek>().IsEnumeration).IsTrue();
        await Assert.That(Clr<DayOfWeek>().HasValueSemantics).IsTrue().Because("answers are not exclusive");
        await Assert.That(Clr<int>().IsEnumeration).IsFalse();
        await Assert.That(Clr<string>().IsEnumeration).IsFalse();
    }

    [Test]
    public async Task ClrValueType_StatesValueSemantics_AndAReferenceTypeDoesNot() {
        await Assert.That(Clr<int>().HasValueSemantics).IsTrue();
        await Assert.That(Clr<DayOfWeek>().HasValueSemantics).IsTrue();
        await Assert.That(Clr<DateTimeOffset>().HasValueSemantics).IsTrue();
        await Assert.That(Clr<int?>().HasValueSemantics).IsTrue();
        await Assert.That(Clr<string>().HasValueSemantics).IsFalse();
        await Assert.That(Clr<Uri>().HasValueSemantics).IsFalse();
    }

    [Test]
    public async Task ClrInterfaceAndDelegate_StateTheirShape_AndAClassStatesNeither() {
        await Assert.That(Clr<IDisposable>().IsInterface).IsTrue();
        await Assert.That(Clr<Action>().IsDelegate).IsTrue();
        await Assert.That(Clr<Func<int, int>>().IsDelegate).IsTrue();
        await Assert.That(Clr<Uri>().IsInterface).IsFalse();
        await Assert.That(Clr<Uri>().IsDelegate).IsFalse();
        await Assert.That(Clr<Action>().IsInterface).IsFalse();
    }

    [Test]
    public async Task Declaration_StatesTheFactItsFormDeclares() {
        var registry = new AstTypeRegistry();
        ITypeDefinition Declared(TypeDeclarationForm form) =>
            new AstTypeDefinition(new TypeDefinitionNode("T", Form: form), registry);

        await Assert.That(Declared(TypeDeclarationForm.Enum).IsEnumeration).IsTrue();
        await Assert.That(Declared(TypeDeclarationForm.Struct).HasValueSemantics).IsTrue();
        await Assert.That(Declared(TypeDeclarationForm.Interface).IsInterface).IsTrue();
        var @class = Declared(TypeDeclarationForm.Class);
        await Assert.That(@class.IsEnumeration).IsFalse();
        await Assert.That(@class.HasValueSemantics).IsFalse();
        await Assert.That(@class.IsInterface).IsFalse();
        await Assert.That(@class.IsDelegate).IsFalse();
    }

    [Test]
    public async Task HostWithNoClrTypes_AnswersAQuestionOfItsOwn_OnItsOwnDefinitions() {
        var host = new LedgerHost();

        await Assert.That(host.Amount is LedgerHost.ILedgerType { MinorUnits: 2 }).IsTrue()
            .Because("a platform answers a question no other platform has heard of, on its own definitions");
        await Assert.That(host.Account is LedgerHost.ILedgerType { MinorUnits: null }).IsTrue();
        await Assert.That(host.Amount.IsEnumeration).IsFalse();
        await Assert.That(host.Amount.HasValueSemantics).IsFalse();
        await Assert.That(Clr<decimal>() is LedgerHost.ILedgerType).IsFalse()
            .Because("a platform is never asked a question it does not know");
    }

    [Test]
    public async Task Contract_DeclaresNoClosedListOfKinds() {
        var introspection = typeof(ITypeDefinition).Assembly.GetTypes()
            .Where(static type => type.Namespace == typeof(ITypeDefinition).Namespace)
            .Select(static type => type.Name).ToList();

        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        await Assert.That(introspection).DoesNotContain("TypeKind");
        await Assert.That(introspection).DoesNotContain("PrimitiveAtom");
        await Assert.That(introspection).DoesNotContain("TypeFact").Because("what a type is, the definition answers");
        await Assert.That(typeof(ITypeDefinition).GetMethod("GetFact", any)).IsNull();
        await Assert.That(typeof(ITypeDefinition).GetProperty("Kind", any)).IsNull();
        await Assert.That(typeof(ITypeDefinition).GetProperty("PlatformAtom", any)).IsNull();
    }

    // The contract's size is decided, not grown (docs/decisions/2026-11-19-a-type-system-owns-definitions-vocabulary-and-rules.md).
    // Both lists are ratchets that a row of #998 shortens. A member is added only by editing the member list in the same
    // change, citing that decision; the classification list only shrinks. They end at the decision's ten members and no
    // classification property.
    private static readonly string[] DecidedMembers = [
        "Name", "Namespace", "AccessModifier", "FullName", "BaseType", "Interfaces", "GenericParameters", "Members",
        "Fields", "Properties", "Methods", "Constructors", "Operators", "IsSameTypeAs", "IsCollectionShape",
        "SequenceElementType", "Conversions", "NullableOf", "IsEnumeration", "HasValueSemantics", "IsInterface",
        "IsDelegate", "IsBoolean", "IsText", "IsCharacter", "IsNumber", "IsIntegral", "IsSigned", "IsDecimal",
        "IsNativeSized", "NumberBits", "IsTemporal", "HasDate", "HasTime", "HasOffset", "IsDuration",
        "IsUniqueIdentifier", "IsBinary", "IsTopType", "ResolveConversionFrom", "IsAssignableWithoutConversion",
        "ResolveOperator",
    ];

    private static readonly string[] RemainingClassificationProperties = [
        "AccessModifier", "IsCollectionShape", "IsEnumeration", "HasValueSemantics", "IsInterface", "IsDelegate",
        "IsBoolean", "IsText", "IsCharacter", "IsNumber", "IsIntegral", "IsSigned", "IsDecimal", "IsNativeSized",
        "NumberBits", "IsTemporal", "HasDate", "HasTime", "HasOffset", "IsDuration", "IsUniqueIdentifier", "IsBinary",
        "IsTopType",
    ];

    private const System.Reflection.BindingFlags Declared = System.Reflection.BindingFlags.Instance
        | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

    // A property's or an event's accessor is that member, not another; every other method counts, operators included.
    private static bool IsAccessor(System.Reflection.MemberInfo member) =>
        member is System.Reflection.MethodInfo { IsSpecialName: true } method
        && (method.Name.StartsWith("get_", StringComparison.Ordinal) || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal));

    [Test]
    public async Task ITypeDefinition_IsExactlyTheDecidedMembers() {
        var declared = typeof(ITypeDefinition).GetMembers(Declared)
            .Where(static member => !IsAccessor(member))
            .Select(static member => member.Name);

        await Assert.That(declared).IsEquivalentTo(DecidedMembers)
            .Because("a member is added or removed only by editing the decided list, citing the type-system decision");
    }

    [Test]
    public async Task ITypeDefinition_StatesNoClassification() {
        var classifying = typeof(ITypeDefinition).GetProperties(Declared)
            .Where(static property => property.PropertyType == typeof(bool) || property.PropertyType == typeof(int)
                || property.PropertyType.IsEnum)
            .Select(static property => property.Name);

        await Assert.That(classifying).IsEquivalentTo(RemainingClassificationProperties)
            .Because("what kind of type something is, is its type system's rule; this list only shrinks");
    }
}