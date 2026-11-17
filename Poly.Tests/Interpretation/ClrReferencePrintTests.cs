using Poly.Ast.Nodes;
using Poly.Interpretation.CSharp;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Characterization of what the CLR factory's references print as. A type cast is the smallest tree that renders a type
/// reference; <c>typeof</c> renders one through <see cref="TypeOf"/>.
///
/// This is the invariant the factory's redefinition to neutral nodes must hold: a plain type prints its full CLR name
/// (<c>System.Int32</c>), <c>object</c> and a generic print their C# spelling (<c>object</c>, <c>List&lt;int&gt;</c>), a
/// nested type prints with dots, and a nullable, array, set or dictionary prints its C# form. A neutral
/// <c>NamedTypeReference</c> alone cannot reproduce that — it prints its full name in every position — so the writer's
/// context-aware spelling and the factory change together, not the factory alone.
/// </summary>
public class ClrReferencePrintTests {
    private static readonly (string Name, Node Printed, string Expected)[] Cases = [
        Cast("int", ClrTypeReferences.To<int>(), "_ = (System.Int32)1;"),
        Cast("string", ClrTypeReferences.To<string>(), "_ = (System.String)1;"),
        Cast("bool", ClrTypeReferences.To<bool>(), "_ = (System.Boolean)1;"),
        Cast("object", ClrTypeReferences.To<object>(), "_ = (object)1;"),
        Cast("decimal", ClrTypeReferences.To<decimal>(), "_ = (System.Decimal)1;"),
        Cast("int?", ClrTypeReferences.To<int?>(), "_ = (int?)1;"),
        Cast("List<int>", ClrTypeReferences.To<List<int>>(), "_ = (List<int>)1;"),
        Cast("Dictionary<string,int>", ClrTypeReferences.To<Dictionary<string, int>>(), "_ = (Dictionary<string, int>)1;"),
        Cast("ISet<long>", ClrTypeReferences.To<ISet<long>>(), "_ = (ISet<long>)1;"),
        Cast("int[]", ClrTypeReferences.To<int[]>(), "_ = (System.Int32[])1;"),
        Cast("KeyValuePair<int,int>", ClrTypeReferences.To<KeyValuePair<int, int>>(), "_ = (KeyValuePair<int, int>)1;"),
        Cast("DayOfWeek", ClrTypeReferences.To<DayOfWeek>(), "_ = (System.DayOfWeek)1;"),
        Cast("DateTime", ClrTypeReferences.To<DateTime>(), "_ = (System.DateTime)1;"),
        Cast("Guid", ClrTypeReferences.To<Guid>(), "_ = (System.Guid)1;"),
        Cast("Uri", ClrTypeReferences.To<Uri>(), "_ = (System.Uri)1;"),
        Cast("Of(long)", ClrTypeReferences.Of(typeof(long)), "_ = (System.Int64)1;"),
        // The shapes the first factory attempt exposed.
        Cast("Func<int,int>", ClrTypeReferences.To<Func<int, int>>(), "_ = (Func<int, int>)1;"),
        Cast("Action<int>", ClrTypeReferences.To<Action<int>>(), "_ = (Action<int>)1;"),
        Cast("Task<int>", ClrTypeReferences.To<Task<int>>(), "_ = (Task<int>)1;"),
        Cast("nested", ClrTypeReferences.To<AstParityTests.ParityWidget>(), "_ = (Poly.Tests.Interpretation.AstParityTests.ParityWidget)1;"),
        Cast("void", ClrTypeReferences.Of(typeof(void)), "_ = (System.Void)1;"),
        Cast("int[][]", ClrTypeReferences.To<int[][]>(), "_ = (System.Int32[][])1;"),
        // A known wrong answer, pinned so a change to it is visible: the old printer asks TypeCode, which an enum
        // reports as its underlying integer, so DayOfWeek? prints as int? instead of DayOfWeek?.
        Cast("DayOfWeek?", ClrTypeReferences.To<DayOfWeek?>(), "_ = (int?)1;"),
        ("typeof(int)", new TypeOf(ClrTypeReferences.To<int>()), "_ = typeof(System.Int32);")
    ];

    private static (string, Node, string) Cast(string name, Node reference, string expected) =>
        (name, new TypeCast(new Constant(1), reference), expected);

    [Test]
    public async Task ClrFactory_Print_IsPinned() {
        foreach (var (name, printed, expected) in Cases) {
            await Assert.That(CSharpLanguage.Write(printed)).IsEqualTo(expected)
                .Because($"'{name}' prints as");
        }
    }
}