using Poly.Grammar;

namespace Poly.Tests.Grammar;

/// <summary>
/// <see cref="Printer{TToken,TTokenKind}.Render(string, string, IReadOnlyDictionary{string, string}, string?[])"/>
/// (G-27): content values fill a row's content positions in element order, named holes come from the
/// fills, and the count is strict both ways. A surplus or a missing value throws naming the row, after the
/// print is reported to <see cref="PrintTrace"/> with what was supplied and what the row consumed.
/// </summary>
public sealed class PrinterRenderTests {
    private static string Canonical(TestKind kind) => kind switch {
        TestKind.Colon => ":",
        TestKind.Comma => ",",
        TestKind.Star => "*",
        TestKind.Entity => "entity",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No canonical text."),
    };

    // decl/typed: name ':' <type> ['*']  → two content positions (the type, the optional star).
    private static Grammar<TestToken, TestKind> Table() => new GrammarBuilder<TestToken, TestKind>()
        .Define("decl")
            .Pattern("typed")
                .Value(TestKind.Identifier, "name").Kind(TestKind.Colon).Value(TestKind.Identifier)
                .Optional(TestKind.Star).Commit()
        .Build();

    private static Printer<TestToken, TestKind> NewPrinter() => new(Table(), Canonical);

    [Test]
    public async Task Render_ContentFillsPositionsInOrder() {
        var text = NewPrinter().Render("decl", "typed", Fills.Of(("name", "Count")), "Number", "*");

        await Assert.That(text).IsEqualTo("Count:Number*");
    }

    [Test]
    public async Task Render_NullContent_IsAnEmptySlot() {
        var text = NewPrinter().Render("decl", "typed", Fills.Of(("name", "Count")), "Number", null);

        await Assert.That(text).IsEqualTo("Count:Number");
    }

    [Test]
    public async Task Render_NamedFills_Emitted() {
        var text = NewPrinter().Render("decl", "typed", Fills.Of(("name", "Title")), "Text", "");

        await Assert.That(text).StartsWith("Title:");
    }

    [Test]
    public async Task Render_SurplusContent_ThrowsNamingTheRow() {
        var printer = NewPrinter();

        var error = Assert.Throws<InvalidOperationException>(() =>
            printer.Render("decl", "typed", Fills.Of(("name", "Count")), "Number", null, "spare"));

        await Assert.That(error.Message).Contains("'decl/typed'");
        await Assert.That(error.Message).Contains("has 2 content positions; the caller supplied 3");
    }

    [Test]
    public async Task Render_MissingContent_ThrowsNamingTheRow() {
        var printer = NewPrinter();

        var error = Assert.Throws<InvalidOperationException>(() =>
            printer.Render("decl", "typed", Fills.Of(("name", "Count")), "Number"));

        await Assert.That(error.Message).Contains("'decl/typed'");
        await Assert.That(error.Message).Contains("has 2 content positions; the caller supplied 1");
    }

    [Test]
    public async Task Render_ReportsSuppliedAndConsumedCountsBeforeItsStrictThrow() {
        var printer = NewPrinter();
        IReadOnlyList<PrintObservation> observations;
        using (var scope = PrintTrace.Begin()) {
            _ = Assert.Throws<InvalidOperationException>(() =>
                printer.Render("decl", "typed", Fills.Of(("name", "A")), "B"));
            _ = Assert.Throws<InvalidOperationException>(() =>
                printer.Render("decl", "typed", Fills.Of(("name", "A")), "B", null, "C"));
            printer.Render("decl", "typed", Fills.Of(("name", "A")), "B", null);
            observations = scope.Observations;
        }

        await Assert.That(observations.Count).IsEqualTo(3);
        await Assert.That(observations.Select(o => o.SuppliedContentCount)).IsEquivalentTo(new int?[] { 1, 3, 2 });
        await Assert.That(observations.All(o => o.ContentEmitted.Count == 2)).IsTrue();
        await Assert.That(observations.All(o => o is { Rule: "decl", Pattern: "typed" })).IsTrue();
    }

    [Test]
    public async Task Render_UsesOneWriterAndTheSameTextAsPrint() {
        var writers = 0;
        var printer = new Printer<TestToken, TestKind>(Table(), Canonical, () => {
            writers++;
            return new StringTokenWriter<TestKind>(Canonical);
        });
        var fills = Fills.Of(("name", "Count"));

        var rendered = printer.Render("decl", "typed", fills, "Number", "*");
        var rendersWriters = writers;
        var content = new Queue<string>(["Number", "*"]);
        var printed = printer.Print("decl", "typed", ctx => ctx.Emit(content.Dequeue()), fills);

        await Assert.That(rendersWriters).IsEqualTo(1);
        await Assert.That(rendered).IsEqualTo(printed);
    }

    [Test]
    public async Task Fills_Of_ANameGivenTwice_Throws() {
        var error = Assert.Throws<ArgumentException>(() => Fills.Of(("name", "A"), ("name", "B")));

        await Assert.That(error.Message).Contains("'name'");
    }
}
