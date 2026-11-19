using Poly.DomainModeling;
using Poly.DomainModeling.Meaning;
using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Introspection;

/// <summary>
/// A definition belongs to the system that made it, and identity and element type are that system's rules (#998 T9,
/// decision §2.4). The pins hold today's answers so the move does not change one.
/// </summary>
public class TypeSystemOwnershipTests {
    private static readonly CSharpTypeSystem CSharp = CSharpTypeSystem.Shared;
    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    [Arguments(typeof(List<int>), "Int32")]
    [Arguments(typeof(byte[]), "Byte")]
    [Arguments(typeof(int[]), "Int32")]
    [Arguments(typeof(System.Collections.IEnumerable), "Object")]
    [Arguments(typeof(Dictionary<string, int>), "KeyValuePair`2")]
    [Arguments(typeof(string), "Char")]
    public async Task ElementType_IsCSharpsRule(Type type, string element) =>
        await Assert.That(CSharp.ElementType(Clr(type))?.Name).IsEqualTo(element);

    [Test]
    [Arguments(typeof(int))]
    [Arguments(typeof(Uri))]
    public async Task ElementType_OfANonSequence_IsNull(Type type) =>
        await Assert.That(CSharp.ElementType(Clr(type))).IsNull();

    [Test]
    public async Task Identity_ADeclaredCollection_IsNotTheClrCollection() {
        var declared = new AstCollectionTypeDefinition(Clr(typeof(int)), CollectionKind.List);

        await Assert.That(TypeDefinitionExtensions.IsSameType(declared, Clr(typeof(List<int>)))).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(declared, new AstCollectionTypeDefinition(Clr(typeof(int)), CollectionKind.List)))
            .IsTrue().Because("two synthetic collections of one kind and element are one type");
    }

    [Test]
    public async Task Identity_AWordBoundThroughALayer_IsTheTargetsDefinition() {
        var domain = new DomainTypeSystem(new TypeMappingRegistry(), CSharp);

        await Assert.That(TypeDefinitionExtensions.IsSameType(domain.Bind("Int32")!, CSharp.Bind("int")!)).IsTrue();
        await Assert.That(TypeDefinitionExtensions.IsSameType(domain.Bind("Number")!, CSharp.Bind("int")!)).IsFalse();
    }

    [Test]
    public async Task Identity_AcrossSystems_IsNeverTheSame() {
        var ledger = new LedgerHost();

        await Assert.That(TypeDefinitionExtensions.IsSameType(ledger.Amount, Clr(typeof(decimal)))).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(Clr(typeof(decimal)), ledger.Amount)).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(ledger.Amount, ledger.Amount)).IsTrue();
    }

    [Test]
    public async Task Identity_AFreshClrRegistrysDefinition_IsItsOwn() {
        // CLR definitions compare by reference: a definition from another registry instance is not the shared one's.
        var fresh = new ClrTypeDefinitionRegistry().GetTypeDefinition(typeof(int));

        await Assert.That(TypeDefinitionExtensions.IsSameType(fresh, Clr(typeof(int)))).IsFalse();
        await Assert.That(TypeDefinitionExtensions.IsSameType(Clr(typeof(int)), Clr(typeof(int)))).IsTrue();
    }
}