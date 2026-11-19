using Poly.Interpretation.Analysis.Semantics;
using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Introspection;

/// <summary>
/// A definition declares its members once (#998 T11, decision §1 row 8): <c>Members</c> is every member it declares,
/// constructors included, and the typed views (fields, properties, methods, constructors) are views over it.
/// </summary>
public class MemberViewTests {
    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    public async Task ClrMembers_IncludeItsConstructors_StaticOnesToo() {
        var uri = (ClrTypeDefinition)Clr(typeof(Uri));
        var constructors = uri.Members.OfType<ITypeConstructor>().ToList();

        await Assert.That(constructors).IsNotEmpty();
        await Assert.That(constructors.Count).IsEqualTo(uri.Constructors.Count);
        await Assert.That(constructors.Any(static c => c.IsStatic)).IsEqualTo(uri.Constructors.Any(static c => c.IsStatic))
            .Because("a static constructor is a member as it is a constructor");
    }

    [Test]
    public async Task TypedViews_AreTheHostsOwnLists_InTheSameOrder() {
        // The interface's views (extensions over Members) are the host's own typed lists, by reference and in order:
        // what each implementer built before the views became extensions (#998 T11).
        foreach (var type in new[] { typeof(Uri), typeof(List<int>), typeof(string) }) {
            var clr = (ClrTypeDefinition)Clr(type);
            ITypeDefinition definition = clr;
            await Assert.That(definition.Fields.SequenceEqual(clr.Fields, ReferenceEqualityComparer.Instance)).IsTrue().Because($"{type}: fields");
            await Assert.That(definition.Properties.SequenceEqual(clr.Properties, ReferenceEqualityComparer.Instance)).IsTrue().Because($"{type}: properties");
            await Assert.That(definition.Methods.SequenceEqual(clr.Methods, ReferenceEqualityComparer.Instance)).IsTrue().Because($"{type}: methods");
            await Assert.That(definition.Constructors.SequenceEqual(clr.Constructors, ReferenceEqualityComparer.Instance)).IsTrue().Because($"{type}: constructors");
        }
        var collection = new AstCollectionTypeDefinition(Clr(typeof(int)), CollectionKind.List);
        ITypeDefinition modeled = collection;
        await Assert.That(modeled.Properties.SequenceEqual(collection.Properties, ReferenceEqualityComparer.Instance)).IsTrue();
        await Assert.That(modeled.Methods.SequenceEqual(collection.Methods, ReferenceEqualityComparer.Instance)).IsTrue();
        await Assert.That(modeled.Constructors.SequenceEqual(collection.Constructors, ReferenceEqualityComparer.Instance)).IsTrue();
        await Assert.That(modeled.Fields.SequenceEqual(collection.Fields, ReferenceEqualityComparer.Instance)).IsTrue();
    }

    [Test]
    public async Task MemberAccess_NamedLikeItsType_NeverResolvesToAConstructor() {
        // A CLR constructor is named after its type; C# member access never reaches one (fail closed).
        var result = CSharpScript.Run("var u = new System.Uri(\"http://example.com\"); u.Uri");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Diagnostics.Any(static d => d.Contains("does not contain a member named 'Uri'"))).IsTrue();
    }
}