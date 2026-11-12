

using Poly.DomainModeling.Ontology;
using Poly.Grammar;

using Poly.Tests.Polyc;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// OQ-4 / G-09: the ruled 256 expression nesting levels must parse from any caller thread,
/// and 257 must be a positioned error rather than a crash. A stack overflow cannot be caught,
/// so the "[a]/[c] does not crash from a 1 MB caller thread" cases run in a child process:
/// the probe is payload only in the parent and acts when the child runner sets
/// <c>POLY_CHILD_PROBE</c>. Boundary cases on a large stack stay in process.
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
    /// path is not a fragment on master ("Item.Value" alone is a trailing-token error), so
    /// <c>Kids where …</c> is the member-access form. Each surface counts one nesting level.
    /// </summary>
    internal static string Mixed(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels - 4; i++) {
            body = (i % 3) switch {
                0 => "(" + body + ")",
                1 => "not " + body,
                _ => body + " and Flag",
            };
        }
        return "Kids where (" + body + ")";
    }

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

    // ── [b]: 257 levels is the positioned depth error, not an overflow ────────────────

    [Test]
    public async Task Parse_257Parens_IsPositionedDepthError() {
        var (parsed, failure) = ParseOn(Parens(Limit + 1), LargeStackBytes);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).Contains(DslLimits.DepthDetail);
        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Column).IsGreaterThan(0);
        await Assert.That(error.Offset).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Parse_257Nots_IsPositionedDepthError() {
        var (parsed, failure) = ParseOn(Nots(Limit + 1), LargeStackBytes);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).Contains(DslLimits.DepthDetail);
        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Column).IsGreaterThan(0);
    }

    // ── the in-limit side of each form, and the left-associative metric ───────────────

    [Test]
    public async Task Parse_256MixedLevels_ReturnsExpression() {
        var (parsed, failure) = ParseOn(Mixed(Limit), LargeStackBytes);

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task AndChain_256Operands_ReturnsExpression() {
        var (parsed, failure) = ParseOn(string.Join(" and ", Enumerable.Repeat("Flag", Limit)), LargeStackBytes);

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task AndChain_257Operands_IsPositionedDepthError() {
        var (parsed, failure) = ParseOn(string.Join(" and ", Enumerable.Repeat("Flag", Limit + 1)), LargeStackBytes);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)failure!).Detail).IsEqualTo(DslLimits.DepthDetail);
    }
}