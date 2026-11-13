using Poly.Interpretation.Analysis.Semantics;
using Poly.Introspection;
using Poly.Tests.DomainModeling.Analysis;

namespace Poly.Tests.Introspection;

/// <summary>
/// T-16 rows (b) and (c): type identity for the two synthetic definitions that are built fresh on every resolve.
/// <see cref="TypeDefinitionExtensions"/> has one identity notion; a nested synthetic collection compares its element
/// by that notion, and two resolutions of one generic parameter are the same type.
/// </summary>
public class GenericParameterIdentityTests {
    private sealed record TestParameter(string Name) : IParameter {
        public int Position => 0;
        public ITypeDefinition ParameterTypeDefinition => null!;
        public bool IsOptional => false;
        public object? DefaultValue => null;
    }

    private static ITypeDefinition Declared(string name, string? ns = null) =>
        new AstTypeDefinition(new TypeDefinitionNode(name, ns), new ModeledTypeSet());

    private static ITypeDefinition ListOf(ITypeDefinition element) => new AstCollectionTypeDefinition(element, CollectionKind.List);

    private static ITypeDefinition Parameter(string name, ITypeDefinition declaring) => new AstGenericParameterTypeDefinition(new TestParameter(name), declaring);

    // ---- row (b): nested synthetic collections ----

    [Test]
    public async Task NestedCollections_OfElementsInDifferentNamespaces_AreDifferentTypes() {
        var left = ListOf(ListOf(Declared("A", "ns1")));
        var right = ListOf(ListOf(Declared("A", "ns2")));
        await Assert.That(left.IsAssignableFrom(right)).IsFalse();
        await Assert.That(right.IsAssignableFrom(left)).IsFalse();
    }

    [Test]
    public async Task NestedCollections_OfOneElement_AreTheSameType_EvenWhenBuiltTwice() {
        var left = ListOf(ListOf(Declared("A", "ns1")));
        var right = ListOf(ListOf(Declared("A", "ns1")));
        await Assert.That(left.IsAssignableFrom(right)).IsTrue();
        await Assert.That(left.IsAssignableFrom(ListOf(ListOf(Declared("B", "ns1"))))).IsFalse();
    }

    [Test]
    public async Task FlatCollections_StayComparedByElementNameAndNamespace() {
        await Assert.That(ListOf(Declared("A", "ns1")).IsAssignableFrom(ListOf(Declared("A", "ns1")))).IsTrue();
        await Assert.That(ListOf(Declared("A", "ns1")).IsAssignableFrom(ListOf(Declared("A", "ns2")))).IsFalse();
    }

    // ---- row (c): generic parameters ----

    [Test]
    public async Task GenericParameter_ResolvedTwice_IsTheSameType() {
        var owner = Declared("Box");
        await Assert.That(Parameter("T", owner).IsAssignableFrom(Parameter("T", owner))).IsTrue();
    }

    [Test]
    public async Task GenericParameters_WithDifferentNamesOnOneType_AreDifferentTypes() {
        var owner = Declared("Pair");
        await Assert.That(Parameter("K", owner).IsAssignableFrom(Parameter("V", owner))).IsFalse();
        await Assert.That(Parameter("V", owner).IsAssignableFrom(Parameter("K", owner))).IsFalse();
    }

    [Test]
    public async Task GenericParameters_OfOneNameOnDifferentTypes_AreDifferentTypes() {
        await Assert.That(Parameter("T", Declared("Box")).IsAssignableFrom(Parameter("T", Declared("Bag")))).IsFalse();
    }

    [Test]
    public async Task GenericParameters_OfOneNameFromTwoMethodsOnOneType_AreConflated_ALimitation() {
        // The definition carries a name and a declaring TYPE only. A method-level T on Map and another on Filter of the
        // same type compare equal; telling them apart needs the declaring member, which the definition does not hold.
        // Stated, not fixed: no DSL construct declares a method-level type parameter.
        var owner = Declared("Sequence");
        await Assert.That(Parameter("T", owner).IsAssignableFrom(Parameter("T", owner))).IsTrue();
    }
}