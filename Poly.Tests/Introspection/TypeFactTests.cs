using Poly.Ast.Nodes;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// What a type is, is a fact its platform states (#998 S4): an open set of typed answers, not a closed list of kinds.
/// </summary>
public class TypeFactTests {
    private static ITypeDefinition Clr<T>() => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(typeof(T));

    [Test]
    public async Task ClrEnum_StatesAnEnumerationOverItsUnderlyingType() {
        var fact = Clr<DayOfWeek>().GetFact<Enumeration>();

        await Assert.That(fact).IsNotNull();
        await Assert.That(fact!.Underlying).IsSameReferenceAs(Clr<int>());
        await Assert.That(Clr<int>().GetFact<Enumeration>()).IsNull();
        await Assert.That(Clr<string>().GetFact<Enumeration>()).IsNull();
    }

    [Test]
    public async Task ClrValueType_StatesValueSemantics_AndAReferenceTypeDoesNot() {
        await Assert.That(Clr<int>().GetFact<ValueSemantics>()).IsNotNull();
        await Assert.That(Clr<DayOfWeek>().GetFact<ValueSemantics>()).IsNotNull();
        await Assert.That(Clr<DateTimeOffset>().GetFact<ValueSemantics>()).IsNotNull();
        await Assert.That(Clr<int?>().GetFact<ValueSemantics>()).IsNotNull();
        await Assert.That(Clr<string>().GetFact<ValueSemantics>()).IsNull();
        await Assert.That(Clr<Uri>().GetFact<ValueSemantics>()).IsNull();
    }

    [Test]
    public async Task ClrInterfaceAndDelegate_StateTheirShape_AndAClassStatesNeither() {
        await Assert.That(Clr<IDisposable>().GetFact<InterfaceContract>()).IsNotNull();
        await Assert.That(Clr<Action>().GetFact<DelegateSignature>()).IsNotNull();
        await Assert.That(Clr<Func<int, int>>().GetFact<DelegateSignature>()).IsNotNull();
        await Assert.That(Clr<Uri>().GetFact<InterfaceContract>()).IsNull();
        await Assert.That(Clr<Uri>().GetFact<DelegateSignature>()).IsNull();
        await Assert.That(Clr<Action>().GetFact<InterfaceContract>()).IsNull();
    }

    [Test]
    public async Task Declaration_StatesTheFactItsFormDeclares() {
        var registry = new AstTypeRegistry();
        ITypeDefinition Declared(TypeDeclarationForm form) =>
            new AstTypeDefinition(new TypeDefinitionNode("T", Form: form), registry);

        await Assert.That(Declared(TypeDeclarationForm.Enum).GetFact<Enumeration>()).IsNotNull();
        await Assert.That(Declared(TypeDeclarationForm.Struct).GetFact<ValueSemantics>()).IsNotNull();
        await Assert.That(Declared(TypeDeclarationForm.Interface).GetFact<InterfaceContract>()).IsNotNull();
        var @class = Declared(TypeDeclarationForm.Class);
        await Assert.That(@class.GetFact<Enumeration>()).IsNull();
        await Assert.That(@class.GetFact<ValueSemantics>()).IsNull();
        await Assert.That(@class.GetFact<InterfaceContract>()).IsNull();
        await Assert.That(@class.GetFact<DelegateSignature>()).IsNull();
    }

    [Test]
    public async Task HostWithNoClrTypes_StatesAFactOfItsOwn_AndNoneItDoesNotKnow() {
        var host = new LedgerHost();

        await Assert.That(host.Amount.GetFact<LedgerHost.Money>()).IsEqualTo(new LedgerHost.Money(MinorUnits: 2))
            .Because("a platform answers a question no other platform has heard of");
        await Assert.That(host.Account.GetFact<LedgerHost.Money>()).IsNull();
        await Assert.That(host.Amount.GetFact<Enumeration>()).IsNull();
        await Assert.That(host.Amount.GetFact<ValueSemantics>()).IsNull();
        await Assert.That(typeof(ITypeDefinition).GetMethod(nameof(ITypeDefinition.GetFact))!.GetGenericArguments()[0].GetGenericParameterConstraints())
            .Contains(typeof(TypeFact)).Because("a fact is asked for by a fact type, never by object");
        await Assert.That(Clr<decimal>().GetFact<LedgerHost.Money>()).IsNull()
            .Because("a platform states nothing for a question it does not know");
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
        await Assert.That(typeof(ITypeDefinition).GetProperty("Kind", any)).IsNull();
        await Assert.That(typeof(ITypeDefinition).GetProperty("PlatformAtom", any)).IsNull();
    }
}