using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
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

    // ── T-15: a literal is held exactly or is a positioned error ──────────────────────

    private const string MaxDecimal = "79228162514264337593543950335";

    /// <summary>What the expression reader does with <paramref name="text"/>: the literal's value, or the positioned error.</summary>
    private static (object? Value, string? Detail, int Line, int Column) ReadLiteral(string text) {
        try {
            return (((Literal)DslExpressionFragment.ParseExpressionFragment(text)).Value, null, 0, 0);
        }
        catch (GrammarException ex) {
            return (null, ex.Detail, ex.Line, ex.Column);
        }
    }

    [Test]
    [Arguments("1.00000000000000000000000000001", "Number '1.00000000000000000000000000001' has 29 digits after the point; the maximum is 28.")]
    [Arguments("0.00000000000000000000000000005", "Number '0.00000000000000000000000000005' has 29 digits after the point; the maximum is 28.")]
    [Arguments("79228162514264337593543950336", "Number '79228162514264337593543950336' is larger than the maximum 79228162514264337593543950335.")]
    [Arguments("99999999999999999999999999999999", "Number '99999999999999999999999999999999' is larger than the maximum 79228162514264337593543950335.")]
    [Arguments("9.9999999999999999999999999999", "Number '9.9999999999999999999999999999' is larger than the maximum 7.9228162514264337593543950335 for 28 digits after the point.")]
    [Arguments("79228162514264337593543950335.5", "Number '79228162514264337593543950335.5' is larger than the maximum 7922816251426433759354395033.5 for 1 digits after the point.")]
    public async Task Literal_NotHeldExactly_IsPositionedErrorNamingTheLimit(string text, string detail) {
        var read = ReadLiteral(text);
        await Assert.That(read.Detail).IsEqualTo(detail);
        await Assert.That((read.Line, read.Column)).IsEqualTo((1, 1));

        var inContext = ReadLiteral("Age > " + text);
        await Assert.That(inContext.Detail).IsEqualTo(detail);
        await Assert.That((inContext.Line, inContext.Column)).IsEqualTo((1, 7));
    }

    [Test]
    [Arguments("1.0000000000000000000000000000", 1L)]
    [Arguments("007", 7L)]
    [Arguments("9223372036854775807", 9223372036854775807L)]
    public async Task Literal_ExactIntegral_IsLong(string text, long expected) {
        await Assert.That(ReadLiteral(text).Value).IsEqualTo(expected);
    }

    [Test]
    [Arguments("9223372036854775808", "9223372036854775808")]
    [Arguments(MaxDecimal, MaxDecimal)]
    [Arguments("7.9228162514264337593543950335", "7.9228162514264337593543950335")]
    [Arguments("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [Arguments("1.50", "1.50")]
    public async Task Literal_ExactBeyondLongOrFractional_IsDecimal(string text, string expected) {
        await Assert.That(ReadLiteral(text).Value).IsEqualTo(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task LimitsInTheMessages_AreTheLimitsTheCheckEnforces() {
        // The number a message prints is the edge of what the reader accepts: it reads, and one step past it does not.
        var tooLarge = ReadLiteral("79228162514264337593543950336").Detail!;
        var printed = tooLarge[(tooLarge.IndexOf("maximum ", StringComparison.Ordinal) + "maximum ".Length)..].TrimEnd('.');
        await Assert.That(printed).IsEqualTo(decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await Assert.That(ReadLiteral(printed).Detail).IsNull();

        var scaled = ReadLiteral("9.9999999999999999999999999999").Detail!;
        var scaledMax = scaled.Split(' ')[7];
        await Assert.That(ReadLiteral(scaledMax).Detail).IsNull();
        await Assert.That(ReadLiteral(scaledMax[..^1] + "6").Detail).IsNotNull();

        var places = ReadLiteral("0.00000000000000000000000000001").Detail!;
        var maxPlaces = int.Parse(places[(places.LastIndexOf("is ", StringComparison.Ordinal) + 3)..].TrimEnd('.'), System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(ReadLiteral("0." + new string('0', maxPlaces - 1) + "1").Detail).IsNull();
        await Assert.That(ReadLiteral("0." + new string('0', maxPlaces) + "1").Detail).IsNotNull();
    }

    [Test]
    public async Task Literal_HundredThousandDigits_IsRefusedWithATruncatedShownText() {
        var integer = ReadLiteral("1" + new string('0', 100_000));
        await Assert.That(integer.Detail).IsEqualTo($"Number '{"1" + new string('0', 36)}...' is larger than the maximum {MaxDecimal}.");
        var fraction = ReadLiteral("0." + new string('0', 99_999) + "1");
        await Assert.That(fraction.Detail).IsEqualTo("Number '0.00000000000000000000000000000000000...' has 100000 digits after the point; the maximum is 28.");
        await Assert.That(ReadLiteral("1." + new string('0', 100_000)).Value).IsEqualTo(1L);
    }

    private static string InLateTemporalPolicy(string expression) =>
        ResolverBaselineTests.Resolve($"domain A\nuses temporal\nX: entity {{\n  Due: Date\n  Late: policy {{ {expression} > Due }}\n}}\n");

    [Test]
    [Arguments("1.5", "Duration amount '1.5' must be a whole number from 0 to 9223372036854775807, written without a decimal point.")]
    [Arguments("1.0", "Duration amount '1.0' must be a whole number from 0 to 9223372036854775807, written without a decimal point.")]
    [Arguments("99999999999999999999", "Duration amount '99999999999999999999' must be a whole number from 0 to 9223372036854775807.")]
    [Arguments("9223372036854775808", "Duration amount '9223372036854775808' must be a whole number from 0 to 9223372036854775807.")]
    public async Task DurationAmount_NotAWholeLong_IsPositionedError_InBothFoldPaths(string amount, string detail) {
        await Assert.That(ExprDifferentialTests.Result($"{amount} Days", digest: false)).IsEqualTo($"ERR {detail} @1:1");
        await Assert.That(InLateTemporalPolicy($"Now - {amount} Days")).IsEqualTo($"ERR {detail} @5:24");
    }

    [Test]
    public async Task DurationAmount_LimitInTheMessage_IsTheLimitEnforced() {
        var printed = long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(ExprDifferentialTests.Result($"{printed} Days", digest: false)).StartsWith("OK ");
        await Assert.That(InLateTemporalPolicy($"Now - {printed} Days")).StartsWith("OK ");
        await Assert.That(ExprDifferentialTests.Result("9223372036854775808 Days", digest: false)).Contains($"from 0 to {printed}.");
    }

    private static string OnTextProperty(string annotation) => ResolverBaselineTests.Resolve($"domain A\nX: entity {{\n  N: Text {annotation}\n}}\n");

    [Test]
    [Arguments("length(2147483648)", "ERR Length bound '2147483648' must be a whole number from 0 to 2147483647. @3:18")]
    [Arguments("length(1.5)", "ERR Length bound '1.5' must be a whole number from 0 to 2147483647, written without a decimal point. @3:18")]
    [Arguments("length(1.0)", "ERR Length bound '1.0' must be a whole number from 0 to 2147483647, written without a decimal point. @3:18")]
    [Arguments("length(3, 99999999999)", "ERR Length bound '99999999999' must be a whole number from 0 to 2147483647. @3:21")]
    [Arguments("length(3, 1.5)", "ERR Length bound '1.5' must be a whole number from 0 to 2147483647, written without a decimal point. @3:21")]
    public async Task LengthBound_NotAWholeInt_IsPositionedErrorNamingTheLimit(string annotation, string expected) {
        await Assert.That(OnTextProperty(annotation)).IsEqualTo(expected);
    }

    [Test]
    public async Task LengthBound_LimitInTheMessage_IsTheLimitEnforced() {
        var printed = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Assert.That(OnTextProperty($"length({printed})")).StartsWith("OK ");
        await Assert.That(OnTextProperty("length(2147483648)")).Contains($"from 0 to {printed}.");
    }

    [Test]
    [Arguments("column(\"c\", 123456789012345678901234567890)", "ERR Number '123456789012345678901234567890' is larger than the maximum 79228162514264337593543950335. @3:23")]
    [Arguments("column(\"c\", 0.0000000000000000000000000000000001)", "ERR Number '0.0000000000000000000000000000000001' has 34 digits after the point; the maximum is 28. @3:23")]
    [Arguments("label(123456789012345678901234567890)", "ERR Number '123456789012345678901234567890' is larger than the maximum 79228162514264337593543950335. @3:17")]
    public async Task AnnotationNumber_NotHeldExactly_IsPositionedErrorNamingTheLimit(string annotation, string expected) {
        await Assert.That(OnTextProperty(annotation)).IsEqualTo(expected);
        await Assert.That(OnTextProperty("column(\"c\", " + new string('9', 400) + ")")).Contains("is larger than the maximum 79228162514264337593543950335. @3:23");
    }

    [Test]
    public async Task AnnotationNumber_ExactLiterals_StillRun() {
        // The same rule as every literal: exact digits run; 79228162514264337593543950335 is the largest.
        await Assert.That(OnTextProperty("column(\"c\", 1.5)")).StartsWith("OK ");
        await Assert.That(OnTextProperty("column(\"c\", 79228162514264337593543950335)")).StartsWith("OK ");
        await Assert.That(OnTextProperty("column(\"c\", 79228162514264337593543950336)")).StartsWith("ERR Number ");
    }

    [Test]
    public async Task PrinterSites_UseNoNumericFormattingOfTheirOwn() {
        var root = GuideBlocks.RepoRoot();
        var files = Directory.GetFiles(Path.Combine(root, "Poly/DomainModeling/Language"), "DomainDslPrinter*.cs")
            .Select(f => Path.GetRelativePath(root, f))
            .Concat([
                "Poly/DomainModeling/Language/DslTokenWriter.cs",
                "Poly/DomainModeling/Ontology/Constraints/ConstraintKinds.cs",
                "Poly/DomainModeling/Libraries/Temporal/TemporalExpressionPrintBinders.cs",
                "Poly/DomainModeling/Meaning/CoreExpressionPrintBinders.cs",
            ])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var forbidden = new Regex(@"\.ToString\(\s*(CultureInfo|""[^""]*"")|\.ToString\(\)|\{l\.M(in|ax)Length\}", RegexOptions.CultureInvariant);
        var offenders = new List<string>();
        var otherConversions = new List<string>();
        foreach (var file in files) {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            for (var i = 0; i < lines.Length; i++) {
                // `describe:` rows build diagnostic text from integer lengths; they are not DSL printing.
                // Bare `.ToString()` survivors: the printer's StringBuilder (`_sb.ToString()`, `ToText`) and the enum
                // `sub.Quantifier.ToString()`; none is a number.
                var line = lines[i].Replace("_sb.ToString()", "", StringComparison.Ordinal).Replace("Quantifier.ToString()", "", StringComparison.Ordinal);
                if (forbidden.IsMatch(line) && !line.Contains("describe:", StringComparison.Ordinal))
                    offenders.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                if (lines[i].Contains("Convert.ToString(", StringComparison.Ordinal))
                    otherConversions.Add($"{Path.GetFileName(file)}: {lines[i].Trim()}");
            }
        }
        // Presence control: the pattern bites a bare numeric ToString.
        await Assert.That(forbidden.IsMatch("var t = amount.ToString();")).IsTrue();
        await Assert.That(string.Join("\n", offenders)).IsEqualTo("");
        // The survivors: the fallback for a value that is NOT a number (a date bound, an enum, ...), each one after an IsNumber branch.
        await Assert.That(string.Join("\n", otherConversions.Order(StringComparer.Ordinal))).IsEqualTo(string.Join("\n",
            "DomainDslPrinter.Constraints.cs: _ => Convert.ToString(bound, CultureInfo.InvariantCulture) ?? \"\",",
            "DomainDslPrinter.cs: _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? \"null\",",
            "DomainDslPrinter.cs: return Convert.ToString(literal.Value, CultureInfo.InvariantCulture) ?? \"null\";"));
        // Presence control: the scan reads real files that still hold the number sites.
        await Assert.That(File.ReadAllText(Path.Combine(root, "Poly/DomainModeling/Language/DomainDslPrinter.Constraints.cs"))).Contains("DslNumberText.Format");
    }
}