using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology.Constraints;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// L-20b: one end-anchor semantic for <c>pattern(...)</c>. Analysis, Lower and the
/// contract boundary check all evaluate through <c>PatternSemantics</c>, so a
/// trailing unescaped <c>$</c> means absolute end of input everywhere, exactly as
/// the L-20 runtime guard does.
/// </summary>
public class PatternSemanticsTests {
    /// <summary>L-20b: analysis agrees with the guard, so a final newline is a violation.</summary>
    [Test]
    public async Task Evaluate_AnchoredPatternWithFinalNewline_IsViolated() {
        var verdict = ConstraintValidation.Evaluate(new PatternConstraint("^[A-Z]{3}$"), "ABC\n");

        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Violated);
    }

    /// <summary>L-20b fallback (pin): an unparsable pattern decides nothing, so it is never satisfied.</summary>
    [Test]
    public async Task Evaluate_InvalidPattern_IsViolated() {
        var verdict = ConstraintValidation.Evaluate(new PatternConstraint("(["), "abc");

        await Assert.That(verdict).IsEqualTo(ConstraintVerdict.Violated);
    }

    /// <summary>Moved from L-20's <c>ConstraintCheckShapeTests</c>, assertions unchanged: the one seam.</summary>
    [Test]
    public async Task EndAnchored_RewritesOnlyAnUnescapedTrailingDollar() {
        // An unescaped trailing '$' is .NET's end-of-input-or-before-final-newline
        // anchor; it becomes the absolute-end anchor '\z'.
        await Assert.That(PatternSemantics.EndAnchored("^[A-Z]+$")).IsEqualTo("^[A-Z]+\\z");
        // No end anchor is the author's search intent; it stays as written.
        await Assert.That(PatternSemantics.EndAnchored("^[A-Z]+")).IsEqualTo("^[A-Z]+");
        // An escaped '$' is a literal dollar, not an anchor; it stays as written.
        await Assert.That(PatternSemantics.EndAnchored("^[A-Z]+\\$")).IsEqualTo("^[A-Z]+\\$");
        // The escape parity counts backslashes: an even run leaves the '$' an anchor.
        await Assert.That(PatternSemantics.EndAnchored("^[A-Z]+\\\\$")).IsEqualTo("^[A-Z]+\\\\\\z");
        await Assert.That(PatternSemantics.EndAnchored("")).IsEqualTo("");
    }

    /// <summary>L-20b fallback: an unparsable pattern matches nothing instead of throwing.</summary>
    [Test]
    public async Task TryMatch_InvalidPattern_ReturnsFalse() {
        await Assert.That(PatternSemantics.TryMatch("abc", "([")).IsFalse();
    }

    /// <summary>L-20b: <c>TryMatch</c> carries the same absolute-end meaning as the runtime guard.</summary>
    [Test]
    public async Task TryMatch_AnchoredPatternWithFinalNewline_ReturnsFalse() {
        await Assert.That(PatternSemantics.TryMatch("ABC\n", "^[A-Z]{3}$")).IsFalse();
        await Assert.That(PatternSemantics.TryMatch("ABC", "^[A-Z]{3}$")).IsTrue();
    }
}