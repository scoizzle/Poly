using Poly.Ast.Nodes;
using Poly.Interpretation.CSharp;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Characterization of what the CLR factory's references print as. A type cast is the smallest tree that renders a type
/// reference, so each case prints one.
///
/// This is the invariant the factory's redefinition to neutral nodes must hold: a plain type prints its full CLR name
/// (<c>System.Int32</c>), <c>object</c> and a generic print their C# spelling (<c>object</c>, <c>List&lt;int&gt;</c>), and
/// a nullable, array, set or dictionary prints its C# form. A neutral <c>NamedTypeReference</c> alone cannot reproduce
/// that — it prints its full name in every position — so the writer's context-aware spelling and the factory change
/// together, not the factory alone.
/// </summary>
public class ClrReferencePrintTests {
    private static readonly (string Name, Node Reference, string Printed)[] Cases = [
        ("int", ClrTypeReferences.To<int>(), "_ = (System.Int32)1;"),
        ("string", ClrTypeReferences.To<string>(), "_ = (System.String)1;"),
        ("bool", ClrTypeReferences.To<bool>(), "_ = (System.Boolean)1;"),
        ("object", ClrTypeReferences.To<object>(), "_ = (object)1;"),
        ("decimal", ClrTypeReferences.To<decimal>(), "_ = (System.Decimal)1;"),
        ("int?", ClrTypeReferences.To<int?>(), "_ = (int?)1;"),
        ("List<int>", ClrTypeReferences.To<List<int>>(), "_ = (List<int>)1;"),
        ("Dictionary<string,int>", ClrTypeReferences.To<Dictionary<string, int>>(), "_ = (Dictionary<string, int>)1;"),
        ("ISet<long>", ClrTypeReferences.To<ISet<long>>(), "_ = (ISet<long>)1;"),
        ("int[]", ClrTypeReferences.To<int[]>(), "_ = (System.Int32[])1;"),
        ("KeyValuePair<int,int>", ClrTypeReferences.To<KeyValuePair<int, int>>(), "_ = (KeyValuePair<int, int>)1;"),
        ("DayOfWeek", ClrTypeReferences.To<DayOfWeek>(), "_ = (System.DayOfWeek)1;"),
        ("DateTime", ClrTypeReferences.To<DateTime>(), "_ = (System.DateTime)1;"),
        ("Guid", ClrTypeReferences.To<Guid>(), "_ = (System.Guid)1;"),
        ("Uri", ClrTypeReferences.To<Uri>(), "_ = (System.Uri)1;"),
        ("Of(long)", ClrTypeReferences.Of(typeof(long)), "_ = (System.Int64)1;")
    ];

    [Test]
    public async Task ClrFactory_Print_IsPinned() {
        foreach (var (name, reference, printed) in Cases) {
            await Assert.That(CSharpLanguage.Write(new TypeCast(new Constant(1), reference))).IsEqualTo(printed)
                .Because($"'{name}' prints as");
        }
    }
}