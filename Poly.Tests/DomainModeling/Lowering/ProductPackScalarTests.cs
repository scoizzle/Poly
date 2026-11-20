using Poly.Interpretation.CSharp;
using Poly.Introspection;
using Poly.Introspection.CommonLanguageRuntime;
using Poly.Tests.Interpretation;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// The product pack classifies a type position by C#'s rules (#998 T6c): a C# simple type, string, the CLR's calendar
/// types, Guid and byte[] are scalars; object is the top type; a foreign definition is neither, whatever it answers about
/// itself.
/// </summary>
public class ProductPackScalarTests {
    private static ITypeDefinition Clr(Type type) => ClrTypeDefinitionRegistry.Shared.GetTypeDefinition(type);

    [Test]
    public async Task Scalar_IsCSharpsRule_NotWhatAForeignTypeAnswers() {
        var csharp = CSharpTypeSystem.Shared;
        foreach (var type in new[] { typeof(int), typeof(long), typeof(bool), typeof(char), typeof(decimal), typeof(string),
                     typeof(DateTime), typeof(DateOnly), typeof(TimeSpan), typeof(Guid), typeof(byte[]), typeof(nint) })
            await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsScalarDefinition(csharp, Clr(type))).IsTrue().Because($"'{type}'");
        foreach (var type in new[] { typeof(Uri), typeof(int[]), typeof(DayOfWeek), typeof(int?) })
            await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsScalarDefinition(csharp, Clr(type))).IsFalse().Because($"'{type}'");

        await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsScalarDefinition(csharp, new ForeignUnsignedWord())).IsFalse()
            .Because("a foreign definition is no C# scalar, though it answers that it is a number");
        await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsScalarDefinition(csharp, new LedgerHost().Amount)).IsFalse();
    }

    [Test]
    public async Task Top_IsTheDefinitionCSharpsObjectBinds() {
        var csharp = CSharpTypeSystem.Shared;
        await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsTopDefinition(csharp, Clr(typeof(object)))).IsTrue();
        await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsTopDefinition(csharp, Clr(typeof(string)))).IsFalse();
        await Assert.That(Poly.Packs.Product.ActionReadsWalker.IsTopDefinition(new LedgerHost(), Clr(typeof(object)))).IsFalse()
            .Because("a target with no word for object has no top type to match");
    }
}