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
}