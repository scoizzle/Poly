using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What shape a type has is a rule of the system that owns the question (#998 T8): C#'s enums and value semantics, a
/// host's own rule; never a closed list of kinds, and the definition answers none of it.
/// </summary>
public class TypeShapeTests {
    private static ITypeDefinition Clr<T>() => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(T));
    private static readonly Poly.Interpretation.CSharp.CSharpTypeSystem CSharp = Poly.Interpretation.CSharp.CSharpTypeSystem.Shared;

    [Test]
    public async Task ClrEnum_IsAnEnumByCSharpsRule() {
        await Assert.That(CSharp.IsEnumType(Clr<DayOfWeek>())).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Clr<DayOfWeek>())).IsTrue().Because("the answers are not exclusive");
        await Assert.That(CSharp.IsEnumType(Clr<int>())).IsFalse();
        await Assert.That(CSharp.IsEnumType(Clr<string>())).IsFalse();
    }

    [Test]
    public async Task ClrValueType_HasValueSemanticsByCSharpsRule_AndAReferenceTypeDoesNot() {
        await Assert.That(CSharp.HasValueSemantics(Clr<int>())).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Clr<DayOfWeek>())).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Clr<DateTimeOffset>())).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Clr<int?>())).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Clr<string>())).IsFalse();
        await Assert.That(CSharp.HasValueSemantics(Clr<Uri>())).IsFalse();
    }

    [Test]
    public async Task Declaration_CSharpStatesWhatItsFormDeclares() {
        var registry = new AstTypeRegistry();
        ITypeDefinition Declared(TypeDeclarationForm form) =>
            new AstTypeDefinition(new TypeDefinitionNode("T", Form: form), registry);

        await Assert.That(CSharp.IsEnumType(Declared(TypeDeclarationForm.Enum))).IsTrue();
        await Assert.That(CSharp.HasValueSemantics(Declared(TypeDeclarationForm.Struct))).IsTrue();
        var @class = Declared(TypeDeclarationForm.Class);
        await Assert.That(CSharp.IsEnumType(@class)).IsFalse();
        await Assert.That(CSharp.HasValueSemantics(@class)).IsFalse();
    }

    [Test]
    public async Task HostWithNoClrTypes_AnswersAQuestionOfItsOwn_AsARuleOfItsSystem() {
        ITypeSystem ledger = new LedgerHost();
        var host = (LedgerHost)ledger;

        await Assert.That(ledger.Rule<LedgerHost.ILedgerRules>()).IsNotNull();
        await Assert.That(ledger.Rule<LedgerHost.ILedgerRules>()?.MinorUnits(host.Amount)).IsEqualTo(2)
            .Because("a type system answers a question no other system has heard of, as a rule of its own");
        await Assert.That(ledger.Rule<LedgerHost.ILedgerRules>()?.MinorUnits(host.Account)).IsNull();
        await Assert.That(CSharp.IsEnumType(host.Amount)).IsFalse();
        await Assert.That(CSharp.HasValueSemantics(host.Amount)).IsFalse();
        await Assert.That(Poly.Interpretation.CSharp.CSharpTypeSystem.Shared.Rule<LedgerHost.ILedgerRules>()).IsNull()
            .Because("a system is never asked a question it does not own");
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
        "System", "Name", "Namespace", "FullName", "BaseType", "Interfaces", "GenericParameters", "Members", "Operators",
        "Conversions",
    ];

    private static readonly string[] RemainingClassificationProperties = [];

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