using Poly.Ast.Nodes;
using Poly.Interpretation.CSharp;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Characterization of what the CLR factory's references print as. A type cast is the smallest tree that renders a type
/// reference; <c>typeof</c> renders one through <see cref="TypeOf"/>.
///
/// The invariant: a ground type prints as C# spells it in every position (<c>int</c>, <c>string</c>, <c>DateTime</c>:
/// the keyword is the C# surface's name for the definition, #998), any other plain type prints its full CLR name, a
/// generic prints its C# spelling (<c>List&lt;int&gt;</c>), a nested type prints with dots, and a nullable, array, set or
/// dictionary prints its C# form. Corrected with the primitive node's removal: a ground type used to print its CLR name
/// (<c>System.Int32</c>) at the top level when it came from this factory and its keyword when it came from a keyword. A neutral
/// <c>NamedTypeReference</c> alone cannot reproduce that — it prints its full name in every position — so the writer's
/// context-aware spelling and the factory change together, not the factory alone.
/// </summary>
public class ClrReferencePrintTests {
    private static readonly (string Name, Node Printed, string Expected)[] Cases = [
        Cast("int", TypeReference.To<int>(), "_ = (int)1;"),
        Cast("string", TypeReference.To<string>(), "_ = (string)1;"),
        Cast("bool", TypeReference.To<bool>(), "_ = (bool)1;"),
        Cast("object", TypeReference.To<object>(), "_ = (object)1;"),
        Cast("decimal", TypeReference.To<decimal>(), "_ = (decimal)1;"),
        Cast("int?", TypeReference.To<int?>(), "_ = (int?)1;"),
        Cast("List<int>", TypeReference.To<List<int>>(), "_ = (List<int>)1;"),
        Cast("Dictionary<string,int>", TypeReference.To<Dictionary<string, int>>(), "_ = (Dictionary<string, int>)1;"),
        Cast("ISet<long>", TypeReference.To<ISet<long>>(), "_ = (ISet<long>)1;"),
        Cast("int[]", TypeReference.To<int[]>(), "_ = (System.Int32[])1;"),
        Cast("KeyValuePair<int,int>", TypeReference.To<KeyValuePair<int, int>>(), "_ = (KeyValuePair<int, int>)1;"),
        Cast("DayOfWeek", TypeReference.To<DayOfWeek>(), "_ = (System.DayOfWeek)1;"),
        Cast("DateTime", TypeReference.To<DateTime>(), "_ = (DateTime)1;"),
        Cast("Guid", TypeReference.To<Guid>(), "_ = (Guid)1;"),
        Cast("Uri", TypeReference.To<Uri>(), "_ = (System.Uri)1;"),
        Cast("Of(long)", TypeReference.Of(typeof(long)), "_ = (long)1;"),
        // The shapes the first factory attempt exposed.
        Cast("Func<int,int>", TypeReference.To<Func<int, int>>(), "_ = (Func<int, int>)1;"),
        Cast("Action<int>", TypeReference.To<Action<int>>(), "_ = (Action<int>)1;"),
        Cast("Task<int>", TypeReference.To<Task<int>>(), "_ = (Task<int>)1;"),
        Cast("nested", TypeReference.To<AstParityTests.ParityWidget>(), "_ = (Poly.Tests.Interpretation.AstParityTests.ParityWidget)1;"),
        Cast("void", TypeReference.Of(typeof(void)), "_ = (System.Void)1;"),
        Cast("int[][]", TypeReference.To<int[][]>(), "_ = (System.Int32[][])1;"),
        // The old printer answered int? here (TypeCode reports an enum as its underlying integer); the reference
        // spells the type it was given.
        Cast("DayOfWeek?", TypeReference.To<DayOfWeek?>(), "_ = (System.DayOfWeek?)1;"),
        ("typeof(int)", new TypeOf(TypeReference.To<int>()), "_ = typeof(int);")
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