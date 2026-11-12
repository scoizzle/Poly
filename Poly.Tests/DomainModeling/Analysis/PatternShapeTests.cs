using System.Diagnostics;
using System.Text.RegularExpressions;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #583 / S-10: <see cref="PatternShape"/> computes a sound UTF-16 length envelope for a
/// regex, and <see cref="ConstraintQualityAnalyzer"/> rejects a Text property whose
/// pattern envelope provably excludes its <c>length(...)</c>.
/// The oracle pins soundness against the runtime (L-20's <c>ConstraintCheckWriter.AnchorEnd</c>),
/// not against a second hand model.
/// </summary>
public class PatternShapeTests {
    private static EvolutionResult Parse(string dsl) {
        var changes = new PolyDslParser(dsl).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], [])).ApplyAndAnalyze(changes);
    }

    private static List<(string Message, string? Code)> Errors(EvolutionResult r) => r.Analysis.Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => (d.Message, d.Code))
        .ToList();

    [Test]
    public async Task LengthFiveToTen_WithPatternOneToThree_IsDMCS001() {
        var r = Parse("""
            domain Test
            S: entity {
              Code: Text length(5, 10) pattern("^[A-Z]{1,3}$")
            }
            """);

        var errors = Errors(r);
        await Assert.That(r.Succeeded).IsFalse();
        var contradiction = errors.Where(e => e.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability
            && e.Message.Contains("unsatisfiable")
            && e.Message.Contains("Code")
            && e.Message.Contains("length(5, 10)")
            && e.Message.Contains("pattern")).ToList();
        await Assert.That(contradiction.Count).IsEqualTo(1);
    }

    [Test]
    public async Task LengthFiveToTen_WithPatternOneToThree_ReportsOnTheProperty() {
        var r = Parse("""
            domain Test
            S: entity {
              Code: Text length(5, 10) pattern("^[A-Z]{1,3}$")
            }
            """);

        var diagnostic = r.Analysis.Diagnostics
            .Single(d => d.Severity == DiagnosticSeverity.Error
                && d.Message.Contains("unsatisfiable"));
        await Assert.That(diagnostic.Node).IsTypeOf<Property>();
        await Assert.That(((Property)diagnostic.Node).Name).IsEqualTo("Code");
    }

    [Test]
    public async Task LengthFiveToTen_WithUnboundablePattern_IsClean() {
        var r = Parse("""
            domain Test
            S: entity {
              Code: Text length(5, 10) pattern("^[A-Z]+$")
            }
            """);

        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(Errors(r).Any(e => e.Message.Contains("unsatisfiable"))).IsFalse();
    }

    [Test]
    public async Task LengthThreeToTen_WithPatternOneToThree_IsClean() {
        var r = Parse("""
            domain Test
            S: entity {
              Code: Text length(3, 10) pattern("^[A-Z]{1,3}$")
            }
            """);

        await Assert.That(r.Succeeded).IsTrue();
        await Assert.That(Errors(r).Any(e => e.Message.Contains("unsatisfiable"))).IsFalse();
    }

    [Test]
    public async Task InvalidPattern_IsNotReportedAsALengthContradiction() {
        // An invalid regex is V-13's seam (#513); S-10 must not double-report it.
        var r = Parse("""
            domain Test
            S: entity {
              Code: Text length(5, 10) pattern("([")
            }
            """);

        await Assert.That(Errors(r).Any(e => e.Message.Contains("unsatisfiable"))).IsFalse();
    }

    [Test]
    public async Task Of_UnboundableRegex_ReturnsNoMaxLength() {
        var additive = PatternShape.Of("^[A-Z]+$");
        await Assert.That(additive.IsValid).IsTrue();
        await Assert.That(additive.MinLength).IsEqualTo(1);
        await Assert.That(additive.MaxLength).IsNull();
        await Assert.That(additive.Anchored).IsTrue();

        var unanchored = PatternShape.Of("[A-Z]{1,3}");
        await Assert.That(unanchored.MaxLength).IsNull();
        await Assert.That(unanchored.Anchored).IsFalse();
    }

    [Test]
    public async Task Of_InvalidRegex_IsValidFalse() {
        var shape = PatternShape.Of("([");
        await Assert.That(shape.IsValid).IsFalse();
        await Assert.That(shape.Error).IsNotNull();
        await Assert.That(shape.MinLength).IsEqualTo(0);
        await Assert.That(shape.MaxLength).IsNull();
    }

    [Test]
    public async Task Of_TrailingDollar_IsAbsoluteEndBound() {
        // L-20: a trailing unescaped '$' is '\z', so the bound is 3, not 4 (a final newline).
        var shape = PatternShape.Of("^[A-Z]{1,3}$");
        await Assert.That(shape.MinLength).IsEqualTo(1);
        await Assert.That(shape.MaxLength).IsEqualTo(3);
        await Assert.That(shape.Anchored).IsTrue();
    }

    [Test]
    public async Task Of_UnboundedQuantifier_IsAnchoredWithNoMax() {
        var shape = PatternShape.Of("^a+$");
        await Assert.That(shape.Anchored).IsTrue();
        await Assert.That(shape.MinLength).IsEqualTo(1);
        await Assert.That(shape.MaxLength).IsNull();
    }

    [Test]
    public async Task Of_EscapedOrMultilineTrailingDollar_IsNotEndAnchored() {
        var escaped = PatternShape.Of("^[A-Z]+\\$");
        await Assert.That(escaped.MaxLength).IsNull();
        await Assert.That(escaped.Anchored).IsFalse();

        var multiline = PatternShape.Of("(?m)^[A-Z]+$");
        await Assert.That(multiline.MaxLength).IsNull();
        await Assert.That(multiline.Anchored).IsFalse();
    }

    [Test]
    public async Task Of_DepthOverSixtyFour_IsUnboundable() {
        static string Nested(int depth) =>
            new string('(', depth) + "a" + new string(')', depth);

        var control = PatternShape.Of("^" + Nested(40) + "$");
        await Assert.That(control.MinLength).IsEqualTo(1);
        await Assert.That(control.MaxLength).IsEqualTo(1);

        // Anchored on purpose: an unanchored deep pattern is unboundable even without the
        // depth cap, so only the anchored form proves the cap fires (removing it would
        // yield Min 1 / Max 1 / Anchored true here).
        var deep = PatternShape.Of("^" + Nested(65) + "$");
        await Assert.That(deep.MinLength).IsEqualTo(0);
        await Assert.That(deep.MaxLength).IsNull();
        await Assert.That(deep.Anchored).IsFalse();
    }

    [Test]
    public async Task Of_LeadingAnchorQuantified_IsUnboundable() {
        // A5: .NET accepts '^?'; the scanner must nevertheless bail rather than model it.
        var shape = PatternShape.Of("^?a");
        await Assert.That(shape.MaxLength).IsNull();
        await Assert.That(shape.MinLength).IsEqualTo(0);
    }

    [Test]
    public async Task Of_StrayBrace_IsLiteral() {
        // A5: '{,3}' is literal in .NET, so '^a{,3}$' matches "a{,3}".
        var shape = PatternShape.Of("^a{,3}$");
        await Assert.That(shape.IsValid).IsTrue();
        await Assert.That(shape.MinLength).IsEqualTo(5);
        await Assert.That(shape.MaxLength).IsEqualTo(5);
    }

    [Test]
    public async Task Of_ReversedBraceQuantifier_IsInvalid() {
        // A4: an inverted brace range is never a bound; .NET rejects the regex, and the
        // scanner's guard keeps the arm from inventing one if that ever changes.
        var shape = PatternShape.Of("^a{3,1}$");
        await Assert.That(shape.IsValid).IsFalse();
        await Assert.That(shape.MinLength).IsEqualTo(0);
        await Assert.That(shape.MaxLength).IsNull();
        await Assert.That(shape.Anchored).IsFalse();
    }

    private sealed record OracleRow(
        string Pattern,
        string Alphabet,
        int MaxLen,
        int ExpectedMin,
        int? ExpectedMax,
        bool ExpectedAnchored,
        bool Tight = false);

    private static readonly OracleRow[] OracleRows = [
        // A1: everything outside the modelled escape list bails, in and out of classes.
        new("[]a]", "]a1", 3, 0, null, false),
        new("[a-z-[aeiou]]{3}", "abc1-", 4, 0, null, false),
        new("\\x41", "Aa1", 2, 0, null, false),
        new("\\p{L}", "a1$", 3, 0, null, false),
        new("\\cA", "a1", 3, 0, null, false),
        new("\\0", "a1", 3, 0, null, false),
        new("\\a", "a1", 3, 0, null, false),
        // Groups and alternation.
        new("(a|)", "a1$", 3, 0, null, false),
        new("^(ab|c){2}$", "abc\n", 5, 2, 4, true, Tight: true),
        new("\\{3\\}", "{}3a1", 4, 3, null, false),
        // Modelled escapes and classes.
        new("^\\d{5}$", "1x\n", 6, 5, 5, true, Tight: true),
        new("^A$", "Aa1\n", 2, 1, 1, true),
        // A4: the alphabet carries the trailing '\n' and the cap runs one unit past the
        // match, so a stale "$ allows a final newline" reading would surface as a longer
        // observed match instead of hiding outside the enumeration.
        new("^a\\nb$", "a\nb\n", 4, 3, 3, true),
        new("^\\w$", "a_1\n", 3, 1, 1, true),
        new("^[A-Z]{1,3}$", "A\n", 4, 1, 3, true),
        new("^a{,3}$", "a{,}3\n", 6, 5, 5, true),
        // A4: the \A, (?:...|...) and \z arms composed end to end.
        new("\\A(?:a\\.|.b)?x\\z", "a.bx\n", 4, 1, 3, true, Tight: true),
        // A8: an astral scalar (U+10437) is two UTF-16 units — the envelope's unit. The
        // lone halves let the enumeration build the pair; '^.$' must not match it,
        // '^..$' must, and '\w' must not match either half.
        new("^.$", "a\uD801\uDC37\n", 3, 1, 1, true),
        new("^..$", "a\uD801\uDC37\n", 3, 2, 2, true, Tight: true),
        new("^\\w$", "a\uD801\uDC37\n", 3, 1, 1, true),
        // A2: the end anchor needs both readings to agree.
        new("^\\c\\$", "\u001c\n", 3, 0, null, false),
        // A3: a quantified anchor bails.
        new("^{2}a$", "a{}2\n", 4, 0, null, false),
        new("^a\\z{2}", "a{}2\n", 4, 0, null, false),
        // Assertions and traps.
        new("^a\\bb$", "ab1\n", 4, 0, null, false),
        new("^\\Z$", "ab1\n", 4, 0, null, false),
        new("^\\1$", "ab1\n", 4, 0, null, false),
        new("^^a", "a^", 3, 0, null, false),
        new("^?a", "a?", 3, 0, null, false),
        // A6: min saturates, an overflowing max becomes null.
        new("^(a{1000000}){1000000}$", "a1", 3, int.MaxValue, null, true),
    ];

    [Test]
    public async Task PatternShape_Bounds_AreSoundForEveryMatchingString() {
        var stopwatch = Stopwatch.StartNew();
        var matchesChecked = 0;

        foreach (var row in OracleRows) {
            var shape = PatternShape.Of(row.Pattern);
            await Assert.That(shape.MinLength).IsEqualTo(row.ExpectedMin);
            await Assert.That(shape.MaxLength).IsEqualTo(row.ExpectedMax);
            await Assert.That(shape.Anchored).IsEqualTo(row.ExpectedAnchored);

            var guarded = ConstraintCheckWriter.AnchorEnd(row.Pattern);
            var observedMin = int.MaxValue;
            var observedMax = -1;

            foreach (var candidate in AllStrings(row.Alphabet, row.MaxLen)) {
                if (!shape.IsValid) continue; // A5: an invalid regex throws from IsMatch
                if (!Regex.IsMatch(candidate, guarded)) continue;
                matchesChecked++;
                await Assert.That(shape.MinLength).IsLessThanOrEqualTo(candidate.Length);
                await Assert.That(candidate.Length).IsLessThanOrEqualTo(shape.MaxLength ?? int.MaxValue);
                observedMin = Math.Min(observedMin, candidate.Length);
                observedMax = Math.Max(observedMax, candidate.Length);
            }

            if (shape.IsValid && shape.MaxLength is not null) {
                // No vacuous row: a bounded envelope must have been observed on a real match.
                await Assert.That(observedMax).IsGreaterThanOrEqualTo(0);
            }

            if (row.Tight) {
                // A7: on exact-envelope rows the observed extremes equal the claimed bounds.
                await Assert.That(observedMin).IsEqualTo(shape.MinLength);
                await Assert.That(observedMax).IsEqualTo(shape.MaxLength!.Value);
            }
        }

        stopwatch.Stop();
        Console.WriteLine(
            $"PatternShape oracle: {OracleRows.Length} rows, {matchesChecked} matching strings, " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
    }

    private static IEnumerable<string> AllStrings(string alphabet, int maxLen) {
        yield return string.Empty;
        for (var length = 1; length <= maxLen; length++) {
            var indices = new int[length];
            while (true) {
                var chars = new char[length];
                for (var i = 0; i < length; i++) chars[i] = alphabet[indices[i]];
                yield return new string(chars);
                var k = length - 1;
                while (k >= 0 && ++indices[k] == alphabet.Length) {
                    indices[k] = 0;
                    k--;
                }
                if (k < 0) break;
            }
        }
    }
}