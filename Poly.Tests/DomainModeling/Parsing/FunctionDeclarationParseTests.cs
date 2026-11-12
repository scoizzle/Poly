using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Add = Poly.DomainModeling.Ontology.Add;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// Issue #406 slice 1: authoring a <c>function</c> declaration in product DSL.
/// The record, printer, and <see cref="AddFunctionChange"/> already exist; the
/// parser rejects the keyword. These tests pin parse, lossless print/re-parse,
/// and the fail-closed shape.
/// </summary>
public sealed class FunctionDeclarationParseTests {
    private const string Poly = """
        domain Shop

        Product: entity {
          Price: Number
        }

        function Add(a: Number, b: Number): Number = a + b
        """;

    private static Domain Apply(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Apply failed: " + result.FailureSummary);
        return result.Root!;
    }

    [Test]
    public async Task Parse_FunctionDeclaration_ProducesDomainFunctionWithBoundParameters() {
        var domain = Apply(Poly);

        var function = domain.Functions.Single();
        await Assert.That(function.Name).IsEqualTo("Add");
        await Assert.That(function.Parameters.Count).IsEqualTo(2);
        await Assert.That(function.Parameters[0].Name).IsEqualTo("a");
        await Assert.That(function.Parameters[0].Type.TypeName).IsEqualTo("Number");
        await Assert.That(function.Parameters[1].Name).IsEqualTo("b");
        await Assert.That(function.ReturnType.TypeName).IsEqualTo("Number");
        var add = (Add)function.Body;
        await Assert.That(add.Left).IsTypeOf<ParameterAccess>();
        await Assert.That(add.Right).IsTypeOf<ParameterAccess>();
        await Assert.That(((ParameterAccess)add.Left).Name).IsEqualTo("a");
        await Assert.That(((ParameterAccess)add.Right).Name).IsEqualTo("b");
    }

    [Test]
    public async Task ParseThenPrint_Function_RoundTripsLossless() {
        var printed = new DomainDslPrinter().Print(Apply(Poly));

        var reprinted = new DomainDslPrinter().Print(Apply(printed));

        await Assert.That(reprinted).IsEqualTo(printed);
    }

    [Test]
    public async Task ParseThenPrint_Function_ParsedBodyMatchesPrintedBody() {
        var printer = new DomainDslPrinter();
        var first = Apply(Poly).Functions.Single();
        var second = Apply(printer.Print(Apply(Poly))).Functions.Single();

        await Assert.That(second.Name).IsEqualTo(first.Name);
        await Assert.That(printer.PrintTestExpression(second.Body))
            .IsEqualTo(printer.PrintTestExpression(first.Body));
        await Assert.That(printer.PrintTestExpression(second.Body)).IsEqualTo("(a + b)");
        await Assert.That(second.ReturnType.TypeName).IsEqualTo(first.ReturnType.TypeName);
    }

    [Test]
    public async Task Parse_NoParameterFunction_RoundTrips() {
        const string poly = """
            domain Shop

            function Pi(): Number = 3
            """;
        var printed = new DomainDslPrinter().Print(Apply(poly));

        await Assert.That(printed).Contains("function Pi(): Number = 3");
        await Assert.That(Apply(printed).Functions.Single().Name).IsEqualTo("Pi");
    }

    [Test]
    public async Task Parse_FunctionWithoutBody_FailsClosedAtNextDeclaration() {
        const string poly = """
            domain Shop

            function Add(a: Number): Number
            Product: entity {
              Price: Number
            }
            """;

        var error = Assert.Throws<GrammarException>(() => new PolyDslParser(poly).Parse())!;

        await Assert.That(error.Detail)
            .IsEqualTo("Expected '=' after function 'Add' return type, got 'Product'");
        await Assert.That(error.Line).IsEqualTo(4);
        await Assert.That(error.Column).IsEqualTo(1);
    }

    /// <summary>1-based line and column of <paramref name="word"/>, derived from the source text.</summary>
    private static (int Line, int Column) PositionOf(string poly, string word) {
        var lines = poly.Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            var column = lines[i].IndexOf(word, StringComparison.Ordinal);
            if (column >= 0) return (i + 1, column + 1);
        }
        throw new InvalidOperationException($"'{word}' does not appear in the source.");
    }

    [Test]
    public async Task Parse_TwoFunctions_FirstBodyEndsInNumber_ParsesBoth() {
        // The entity seeds the primitive catalog (Number), as in every real document.
        static string Doc(string functions) => $$"""
            domain Shop

            Product: entity {
              Price: Number
            }

            {{functions}}
            """;

        var documents = new[] {
            Doc("""
                function A(x: Number): Number = x + 1
                function B(x: Number): Number = x
                """),
            Doc("""
                function A(): Number = 3
                function B(): Number = 4
                """),
            Doc("""function A(): Number = 3 function B(): Number = 4"""),
        };

        foreach (var poly in documents) {
            var functions = Apply(poly).Functions;
            await Assert.That(functions.Count).IsEqualTo(2).Because(poly);
            await Assert.That(functions[0].Name).IsEqualTo("A").Because(poly);
            await Assert.That(functions[1].Name).IsEqualTo("B").Because(poly);
        }
    }

    [Test]
    public async Task Parse_NumberThenUnknownWord_ReportsPositionedUnit() {
        const string poly = """
            domain Shop

            function F(): Number = 3 Foo
            """;
        var (line, column) = PositionOf(poly, "Foo");

        var error = Assert.Throws<GrammarException>(() => new PolyDslParser(poly).Parse())!;

        await Assert.That(error.Detail).Contains("Unknown duration unit 'Foo'");
        await Assert.That(error.Detail).Contains("Days");
        await Assert.That(error.Line).IsEqualTo(line);
        await Assert.That(error.Column).IsEqualTo(column);
    }

    [Test]
    public async Task Parse_FunctionWithoutName_FailsClosedAtParen() {
        const string poly = """
            domain Shop

            function (a: Number): Number = a
            """;

        var error = Assert.Throws<GrammarException>(() => new PolyDslParser(poly).Parse())!;

        await Assert.That(error.Detail)
            .IsEqualTo("Expected function name after 'function', got '('");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Column).IsEqualTo(10);
    }
}