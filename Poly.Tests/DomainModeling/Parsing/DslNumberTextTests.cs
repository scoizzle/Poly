using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Tests.Docs;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// G-03: <c>DslNumberText.Format</c> turns a numeric value into plain DSL digits, never an exponent, and the REAL DSL reader
/// (parse a <c>range(text, text)</c> bound; no .NET parse in the assertion) reads the text back to the same value.
/// </summary>
public sealed class DslNumberTextTests {
    private static readonly Regex PlainDigits = new(@"^-?[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

    /// <summary>(label, value, the decimal the reader must read, or null when the reader refuses the text).</summary>
    private static IEnumerable<(string, object, decimal?)> Rows() {
        yield return ("long.MinValue", long.MinValue, -9223372036854775808m);
        yield return ("long.MaxValue", long.MaxValue, 9223372036854775807m);
        yield return ("long 0", 0L, 0m);
        yield return ("int 7", 7, 7m);
        yield return ("decimal.MaxValue", decimal.MaxValue, decimal.MaxValue);
        yield return ("decimal.MinValue", decimal.MinValue, decimal.MinValue);
        yield return ("decimal 1e15", 1e15m, 1000000000000000m);
        yield return ("decimal 1e-7", 0.0000001m, 0.0000001m);
        yield return ("decimal 1.50", 1.50m, 1.5m);
        yield return ("double 1e15", 1e15d, 1000000000000000m);
        yield return ("double 1e21", 1e21d, 1000000000000000000000m);
        yield return ("double 1e-7", 1e-7d, 0.0000001m);
        yield return ("double 0.1", 0.1d, 0.1m);
        yield return ("double 0.1+0.2", 0.1d + 0.2d, 0.30000000000000004m);
        yield return ("double 123456789.12345679", 123456789.12345679d, 123456789.12345679m);
        yield return ("double -0.0", -0.0d, 0m);
        yield return ("double -2.5", -2.5d, -2.5m);
        yield return ("double 1e-28", 1e-28d, 0.0000000000000000000000000001m);
        yield return ("float 1e15", 1e15f, 1000000000000000m);
        yield return ("double 5e-324 (reader refuses)", 5e-324d, null);
        yield return ("double 1e-29 (reader refuses)", 1e-29d, null);
    }

    internal static object? ReadBack(string text, string type = "Decimal") {
        var poly = $"domain T\nA: entity {{\n  P: {type} range({text}, {text})\n}}\n";
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());
        if (!result.Succeeded)
            throw new InvalidOperationException(result.FailureSummary);
        return result.Root!.Types.OfType<Entity>().Single().Properties.Single().Constraints.OfType<RangeConstraint>().Single().Minimum;
    }

    [Test]
    public async Task Format_NeverEmitsExponent() {
        var seen = 0;
        foreach (var (label, value, _) in Rows()) {
            var text = DslNumberText.Format(value);
            await Assert.That(text.Contains('E') || text.Contains('e')).IsFalse().Because($"{label} printed '{text}'");
            await Assert.That(PlainDigits.IsMatch(text)).IsTrue().Because($"{label} printed '{text}'");
            seen++;
        }
        await Assert.That(seen).IsEqualTo(21);
    }

    [Test]
    public async Task Format_ReadByTheDslReader_GivesTheSameValue() {
        var checkedRows = 0;
        foreach (var (label, value, expected) in Rows()) {
            if (expected is not { } want)
                continue;
            var read = ReadBack(DslNumberText.Format(value));
            await Assert.That(Convert.ToDecimal(read)).IsEqualTo(want).Because(label);
            checkedRows++;
        }
        await Assert.That(checkedRows).IsEqualTo(19);
    }

    [Test]
    public async Task Format_ReaderRefusedRows_StillReadAsRefused() {
        // Pins the reader's refusal (T-15 owns it): a double below decimal's scale is printed exactly, and the reader says so.
        foreach (var (label, value, expected) in Rows().Where(r => r.Item3 is null)) {
            var text = DslNumberText.Format(value);
            await Assert.That(() => ReadBack(text)).ThrowsException().Because(label);
        }
    }

    [Test]
    public async Task Format_IntegralDouble_HasNoFractionMarker() {
        await Assert.That(DslNumberText.Format(10d)).IsEqualTo("10");
        await Assert.That(DslNumberText.Format(1e21d)).IsEqualTo("1000000000000000000000");
        await Assert.That(DslNumberText.Format(-0.0d)).IsEqualTo("0");
        await Assert.That(DslNumberText.Format(1.50m)).IsEqualTo("1.50");
    }

    [Test]
    [Arguments(double.NaN, "NaN")]
    [Arguments(double.PositiveInfinity, "Infinity")]
    [Arguments(double.NegativeInfinity, "-Infinity")]
    [Arguments(double.MaxValue, "1.7976931348623157E+308")]
    [Arguments(double.MinValue, "-1.7976931348623157E+308")]
    [Arguments(1e29d, "1E+29")]
    public async Task Format_ValueTheReaderCannotHold_FailsClosedNamingTheValue(double value, string named) {
        var thrown = await Assert.That(() => DslNumberText.Format(value)).Throws<InvalidOperationException>();
        await Assert.That(thrown!.Message).Contains(named);
    }

    [Test]
    public async Task Format_FloatNaN_FailsClosed() {
        await Assert.That(() => DslNumberText.Format(float.NaN)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Format_NotANumber_Throws() {
        await Assert.That(() => DslNumberText.Format("x")).Throws<ArgumentException>();
        await Assert.That(() => DslNumberText.Format(null)).Throws<ArgumentNullException>();
        await Assert.That(() => DslNumberText.Format(true)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("0", "Integer")]
    [Arguments("42", "Integer")]
    [Arguments("9223372036854775807", "Integer")]
    [Arguments("100.0", "Integer")]
    [Arguments("1.5", "Fractional")]
    [Arguments("0.0000000000000000000000000001", "Fractional")]
    [Arguments("1.5000000000000000000000000000000", "Fractional")]
    [Arguments("9223372036854775808", "IntegerOverflow")]
    [Arguments("99999999999999999999999", "IntegerOverflow")]
    [Arguments("1000000000000000000000000000000000000.0", "IntegerOverflow")]
    [Arguments("0.00000000000000000000000000001", "FractionalOverflow")]
    [Arguments("7.9228162514264337593543950336", "FractionalOverflow")]
    [Arguments("99999999999999999999999999999999.5", "FractionalOverflow")]
    public async Task Classify_MapsDigitTextToStoredType(string text, string expected) {
        await Assert.That(DslNumberText.Classify(text).ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("-1")]
    [Arguments("1e5")]
    [Arguments("1.")]
    [Arguments(".5")]
    [Arguments("1.2.3")]
    [Arguments("abc")]
    public async Task Classify_NotDigitText_Throws(string text) {
        await Assert.That(() => DslNumberText.Classify(text)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Classify_AgreesWithTheReader_ForIntegerAndFractionalText() {
        // The kind says which stored type the reader gives a bound: Integer text -> long on a Number, Fractional text -> decimal.
        await Assert.That(DslNumberText.Classify("42")).IsEqualTo(NumberLiteralKind.Integer);
        await Assert.That(ReadBack("42", "Number")).IsTypeOf<long>();
        await Assert.That(DslNumberText.Classify("1.5")).IsEqualTo(NumberLiteralKind.Fractional);
        await Assert.That(ReadBack("1.5")).IsTypeOf<decimal>();
    }

    [Test]
    public async Task PrinterSites_UseNoNumericFormattingOfTheirOwn() {
        var root = GuideBlocks.RepoRoot();
        var files = new[] {
            "Poly/DomainModeling/Language/DomainDslPrinter.cs",
            "Poly/DomainModeling/Language/DomainDslPrinter.Expressions.cs",
            "Poly/DomainModeling/Language/DomainDslPrinter.Constraints.cs",
            "Poly/DomainModeling/Language/DslTokenWriter.cs",
            "Poly/DomainModeling/Ontology/Constraints/ConstraintKinds.cs",
            "Poly/DomainModeling/Libraries/Temporal/TemporalExpressionPrintBinders.cs",
        };
        var forbidden = new Regex(@"\.ToString\(\s*(CultureInfo|""[^""]*"")|\{l\.M(in|ax)Length\}", RegexOptions.CultureInvariant);
        var offenders = new List<string>();
        var otherConversions = new List<string>();
        foreach (var file in files) {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            for (var i = 0; i < lines.Length; i++) {
                // `describe:` rows build diagnostic text from integer lengths; they are not DSL printing.
                if (forbidden.IsMatch(lines[i]) && !lines[i].Contains("describe:", StringComparison.Ordinal))
                    offenders.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                if (lines[i].Contains("Convert.ToString(", StringComparison.Ordinal))
                    otherConversions.Add($"{Path.GetFileName(file)}: {lines[i].Trim()}");
            }
        }
        await Assert.That(string.Join("\n", offenders)).IsEqualTo("");
        // The survivors: the fallback for a value that is NOT a number (a date bound, an enum, ...), each one after an IsNumber branch.
        await Assert.That(string.Join("\n", otherConversions)).IsEqualTo(string.Join("\n",
            "DomainDslPrinter.cs: return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? \"null\";",
            "DomainDslPrinter.cs: _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? \"null\",",
            "DomainDslPrinter.Constraints.cs: _ => Convert.ToString(bound, CultureInfo.InvariantCulture) ?? \"\","));
        // Presence control: the scan reads real files that still hold the number sites.
        await Assert.That(File.ReadAllText(Path.Combine(root, files[2]))).Contains("DslNumberText.Format");
    }
}