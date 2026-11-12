using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Poly.Tests.Polyc;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// OQ-4 / G-09: the ruled 256 expression nesting levels must parse from any caller thread,
/// and 257 must be a positioned error rather than a crash. A stack overflow cannot be caught,
/// so the "does not crash from a 1 MB caller thread" cases run in a child process: the probes
/// are payload only in the parent and act when the child runner sets <c>POLY_CHILD_PROBE</c>.
/// Boundary cases on a large stack stay in process.
/// </summary>
[NotInParallel("ChildProcessSlots")]
public class ParserLimitsTests {
    private const int Limit = DslLimits.MaxExpressionDepth;

    /// <summary>The stack the OQ-4 measurements called "1 MB": the thread a host or MCP worker gives a parse today.</summary>
    private const int SmallCallerStackBytes = 1024 * 1024;

    /// <summary>A stack no boundary case can overflow, for the in-process assertions.</summary>
    private const int LargeStackBytes = 16 * 1024 * 1024;

    internal static string Parens(int levels) =>
        new string('(', levels) + "Flag" + new string(')', levels);

    internal static string Nots(int levels) => string.Concat(Enumerable.Repeat("not ", levels)) + "Flag";

    /// <summary>
    /// Parens, <c>not</c> and <c>and</c> interleaved under a member-access <c>where</c>: a dotted
    /// path is not a fragment ("Item.Value" alone is a trailing-token error), so <c>Kids where …</c>
    /// is the member-access form. One level per wrapper, plus the <c>where</c> and its group.
    /// </summary>
    internal static string Mixed(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels - 2; i++) {
            body = (i % 3) switch {
                0 => "(" + body + ")",
                1 => "not " + body,
                _ => body + " and Flag",
            };
        }
        return "Kids where (" + body + ")";
    }

    /// <summary>One match per level: the value arm of each carries the next match.</summary>
    internal static string Matches(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = "match { Flag -> " + body + ", else -> Flag }";
        return body;
    }

    /// <summary>One call per level: the single argument of each carries the next call.</summary>
    internal static string Calls(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = "F(" + body + ")";
        return body;
    }

    /// <summary>A document whose one policy is <paramref name="nots"/> <c>not</c>s over <c>Flag</c>:
    /// exactly <paramref name="nots"/> nesting levels, so 256 is the success case at the limit. The
    /// policy grammar rejects a trailing <c>is true</c> before depth matters.</summary>
    internal static string PolicyDocument(int nots) =>
        "domain Deep\n\nA: entity {\n  Flag: Boolean\n  Ok: policy { " + Nots(nots) + "Flag }\n}\n";

    private static (DomainExpression? Parsed, Exception? Failure) ParseOn(string text, int stackBytes) {
        DomainExpression? parsed = null;
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                parsed = DslExpressionFragment.ParseExpressionFragment(text);
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, stackBytes);
        thread.Start();
        thread.Join();
        return (parsed, failure);
    }

    private static (DomainExpression? Parsed, Exception? Failure) ParseOnLarge(string text) =>
        ParseOn(text, LargeStackBytes);

    // ── [a] and [c]: 256 levels from a 1 MB caller thread does not crash ──────────────

    [Test]
    public async Task ChildProbe_256LevelsOn1MbCallerThread_Returns() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        foreach (var text in new[] {
                     Parens(Limit),
                     Nots(Limit),
                     Mixed(Limit),
                 }) {
            var (parsed, failure) = ParseOn(text, SmallCallerStackBytes);
            await Assert.That(failure).IsNull();
            await Assert.That(parsed).IsNotNull();
        }
    }

    [Test]
    public async Task RunTestInChild_256LevelsOn1MbCallerThread_ReturnsWithoutCrash() {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(ParserLimitsTests)}/{nameof(ChildProbe_256LevelsOn1MbCallerThread_Returns)}",
            new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = "g09-depth" });

        await Assert.That(result.IsAborted).IsFalse();
        // The probe asserts all three parses returned; a non-zero or aborted child is the failure.
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    // ── F1: the resolve seam gets the same stack, so no facade bypasses the limit ──────

    [Test]
    public async Task ChildProbe_PolyDslParserOn1MbCallerThread_Returns() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        var document = PolicyDocument(Limit);
        var changes = 0;
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                changes = new PolyDslParser(document).Parse().Count;
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, SmallCallerStackBytes);
        thread.Start();
        thread.Join();

        await Assert.That(failure).IsNull();
        await Assert.That(changes).IsGreaterThan(0);
    }

    [Test]
    public async Task RunTestInChild_PolyDslParserOn1MbCallerThread_ReturnsWithoutCrash() {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(ParserLimitsTests)}/{nameof(ChildProbe_PolyDslParserOn1MbCallerThread_Returns)}",
            new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = "g09-resolve" });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    // ── [b]: 257 levels is the positioned depth error, on the offending token ─────────

    [Test]
    public async Task Parse_257Parens_IsPositionedDepthErrorOnThe257thParen() {
        var (parsed, failure) = ParseOnLarge(Parens(Limit + 1));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        // The 257th '(' is zero-based offset 256, on line 1, column 257.
        await Assert.That(error.Offset).IsEqualTo(256);
        await Assert.That(error.Line).IsEqualTo(1);
        await Assert.That(error.Column).IsEqualTo(257);
        await Assert.That(error.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Parse_257Nots_IsPositionedDepthErrorOnThe257thNot() {
        var (parsed, failure) = ParseOnLarge(Nots(Limit + 1));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        // "not " is four characters, so the 257th `not` starts at column 4 * 256 + 1.
        await Assert.That(error.Column).IsEqualTo(1025);
        await Assert.That(error.Offset).IsEqualTo(1024);
    }

    // ── the in-limit side of each form, and the left-associative metric ───────────────

    [Test]
    public async Task Parse_256MixedLevels_ReturnsExpression() {
        var (parsed, failure) = ParseOnLarge(Mixed(Limit));

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task Parse_257MixedLevels_IsPositionedDepthError() {
        var (parsed, failure) = ParseOnLarge(Mixed(Limit + 1));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)failure!).Detail).IsEqualTo(DslLimits.DepthDetail);
    }

    /// <summary>A chain of N operands adds N - 1 levels, so 257 operands is the last legal chain.</summary>
    [Test]
    public async Task AndChain_257Operands_ReturnsExpression() {
        var (parsed, failure) = ParseOnLarge(string.Join(" and ", Enumerable.Repeat("Flag", Limit + 1)));

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task AndChain_258Operands_IsPositionedDepthErrorOnTheLastOperator() {
        var text = string.Join(" and ", Enumerable.Repeat("Flag", Limit + 2));
        var (parsed, failure) = ParseOnLarge(text);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        // The error sits on an `and` operator, not on an operand.
        await Assert.That(text.Substring(error.Offset, error.Length)).IsEqualTo("and");
    }

    /// <summary>Measured: a <c>match</c> costs more than one level (a call costs 1 and reaches 256),
    /// so this pins only the safe side; the exact match boundary is a G-10 item.</summary>
    [Test]
    public async Task Matches_128Levels_ReturnsExpression() {
        var (parsed, failure) = ParseOnLarge(Matches(Limit / 2));

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task Matches_257Levels_IsPositionedDepthError() {
        var (parsed, failure) = ParseOnLarge(Matches(Limit + 1));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)failure!).Detail).IsEqualTo(DslLimits.DepthDetail);
    }

    [Test]
    public async Task Calls_256Levels_ReturnsExpression() {
        var (parsed, failure) = ParseOnLarge(Calls(Limit));

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    // ── the matcher budget, and the where-chain, are positioned errors too ────────────

    /// <summary>The matcher is still recursive, so a chain far past the fold's reach has to stop at
    /// the rule budget with a position rather than at the stack with a crash.</summary>
    [Test]
    public async Task Parse_5000Nots_IsPositionedMatcherLimitError() {
        var (parsed, failure) = ParseOnLarge(Nots(5000));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).Contains("matching limit");
        await Assert.That(error.Line).IsGreaterThan(0);
    }

    /// <summary>A deep <c>where</c> chain is rejected as a nested <c>where</c>, with a position,
    /// rather than folding further.</summary>
    [Test]
    public async Task Parse_DeepWhereChain_IsPositionedError() {
        var text = string.Concat(Enumerable.Repeat("Kids where (", 2000)) + "Flag" + new string(')', 2000);
        var (parsed, failure) = ParseOnLarge(text);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsNotEmpty();
        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Column).IsGreaterThan(0);
    }

    // ── F2 / A4: depth is a parameter, so nothing survives a failed parse ─────────────

    /// <summary>
    /// A4 wanted the depth reset to be un-forgettable. Depth here is a parameter threaded through
    /// the fold, never a field, so the guard is that the thread is left clean: after a depth error
    /// the next parse on the same thread parses a shallow expression. Green before the guard
    /// existed, and kept so a later field-based refactor has to face it.
    /// </summary>
    [Test]
    public async Task ParseAfterDepthError_SameThreadParsesShallowExpression() {
        Exception? depth = null;
        DomainExpression? shallow = null;
        var thread = new Thread(() => {
            try {
                DslExpressionFragment.ParseExpressionFragment(Nots(Limit + 1));
            }
            catch (Exception ex) {
                depth = ex;
            }
            shallow = DslExpressionFragment.ParseExpressionFragment("Flag");
        }, LargeStackBytes);
        thread.Start();
        thread.Join();

        await Assert.That(depth).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)depth!).Detail).IsEqualTo(DslLimits.DepthDetail);
        await Assert.That(shallow).IsNotNull();
    }
}