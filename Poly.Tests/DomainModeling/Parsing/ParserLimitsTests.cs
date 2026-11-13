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

    /// <summary>A <c>match</c> argument costs exactly one level, like a call argument.</summary>
    [Test]
    public async Task Matches_256Levels_ReturnsExpression() {
        var (parsed, failure) = ParseOnLarge(Matches(Limit));

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
    public async Task Calls_257Levels_IsPositionedDepthError() {
        var (parsed, failure) = ParseOnLarge(Calls(Limit + 1));

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

    /// <summary>
    /// G-10 supersedes G-09's <c>Parse_5000Nots_IsPositionedMatcherLimitError</c>, which asserted the
    /// matcher's "matching limit" budget. That budget is gone: the driver counts depth itself, so a chain
    /// far past the limit stops at the 257th <c>not</c> with the same detail as 257 of them (stricter:
    /// the exact column, not just a positive line).
    /// </summary>
    [Test]
    public async Task Parse_5000Nots_IsRejectedAtDepth256_PositionedAtThe257thNot() {
        var (parsed, failure) = ParseOnLarge(Nots(5000));

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        await Assert.That(error.Offset).IsEqualTo(1024);
        await Assert.That(error.Column).IsEqualTo(1025);
    }

    /// <summary>A deep <c>where</c> chain is a positioned depth error: each <c>where</c> body costs
    /// one level, so the guard stops it before the nested-<c>where</c> rule would.</summary>
    [Test]
    public async Task Parse_DeepWhereChain_IsPositionedError() {
        var text = string.Concat(Enumerable.Repeat("Kids where (", 2000)) + "Flag" + new string(')', 2000);
        var (parsed, failure) = ParseOnLarge(text);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Column).IsGreaterThan(0);
    }

    // ── G-10: nothing recurses over expression depth, so even 64 KB is enough ─────────

    /// <summary>A stack far below the 1 MB OQ-4 measured: only an iterative parse survives 256 levels on it.</summary>
    private const int TinyStackBytes = 64 * 1024;

    private static (DomainExpression? Parsed, Exception? Failure) ParseOnTinyStack(string text) {
        DomainExpression? parsed = null;
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                parsed = DslExpressionFragment.ParseOnThisThread(text);
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, TinyStackBytes);
        thread.Start();
        thread.Join();
        return (parsed, failure);
    }

    /// <summary>The calibration: an ordinary expression parses on the 64 KB harness thread, so a failure
    /// below means recursion over depth, not a harness that cannot parse at all.</summary>
    [Test]
    public async Task Parse_ShallowExpression_On64KbThread_Succeeds() {
        var (parsed, failure) = ParseOnTinyStack("Flag and (Price > 3 or Kids where Qty < 2)");

        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();
    }

    [Test]
    public async Task ChildProbe_NestedToLimitMinus1_On64KbThread_Returns() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        foreach (var text in new[] {
                     Parens(Limit - 1),
                     Nots(Limit - 1),
                     Mixed(Limit - 1),
                     Calls(Limit - 1),
                     Matches(Limit - 1),
                     RightNestedWhere(Limit / 3 - 1),
                     string.Join(" and ", Enumerable.Repeat("Flag", Limit - 1)),
                 }) {
            var (parsed, failure) = ParseOnTinyStack(text);
            await Assert.That(failure).IsNull();
            await Assert.That(parsed).IsNotNull();
        }
    }

    [Test]
    public async Task ChildProbe_FarBeyondLimit_On64KbThread_IsPositionedDepthError() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        foreach (var text in new[] {
                     string.Join(" and ", Enumerable.Repeat("Flag", 5000)),
                     Nots(5000),
                     Parens(5000),
                     Calls(5000),
                     Matches(2000),
                     RightNestedWhere(2000),
                 }) {
            var (parsed, failure) = ParseOnTinyStack(text);
            await Assert.That(parsed).IsNull();
            await Assert.That(failure).IsTypeOf<GrammarException>();
            await Assert.That(((GrammarException)failure!).Detail).IsEqualTo(DslLimits.DepthDetail);
        }
    }

    internal static string RightNestedWhere(int levels) {
        var body = "Flag";
        for (var i = 0; i < levels; i++)
            body = $"Kids where (Flag and {body})";
        return body;
    }

    [Test]
    public async Task Parse_NestedToLimitMinus1_OnA64KbStackThread_Succeeds() {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(ParserLimitsTests)}/{nameof(ChildProbe_NestedToLimitMinus1_On64KbThread_Returns)}",
            new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = "g10-tiny-ok" });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    /// <summary>The row's <c>Parse_5000AndChain_IsRejectedAtDepth256_NoStackOverflow</c>, with the other
    /// deeply nested forms in the same child run.</summary>
    [Test]
    public async Task Parse_5000AndChain_IsRejectedAtDepth256_NoStackOverflow() {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(ParserLimitsTests)}/{nameof(ChildProbe_FarBeyondLimit_On64KbThread_IsPositionedDepthError)}",
            new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = "g10-tiny-reject" });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    // ── G-10 boundary pins: every operator, right-nested and flat, and compare operands ──

    /// <summary>Right-nested chain whose depth is exactly <paramref name="depth"/> levels: each nesting costs a
    /// group and a fold (2 levels), and an odd depth wraps the whole in one more group.</summary>
    internal static string RightNestedAtDepth(string op, int depth) {
        var nested = "Flag";
        for (var i = 0; i < depth / 2; i++)
            nested = $"Flag {op} ({nested})";
        return depth % 2 == 0 ? nested : "(" + nested + ")";
    }

    public static IEnumerable<string> ChainOperators() => ["and", "or", "+", "*"];

    [Test]
    [MethodDataSource(nameof(ChainOperators))]
    public async Task RightNestedChain_BoundaryAt255And256Accepted_257Rejected(string op) {
        foreach (var depth in new[] { Limit - 1, Limit }) {
            var (parsed, failure) = ParseOnLarge(RightNestedAtDepth(op, depth));
            await Assert.That(failure).IsNull();
            await Assert.That(parsed).IsNotNull();
        }

        var (over, rejected) = ParseOnLarge(RightNestedAtDepth(op, Limit + 1));
        await Assert.That(over).IsNull();
        await Assert.That(rejected).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)rejected!).Detail).IsEqualTo(DslLimits.DepthDetail);
    }

    [Test]
    [MethodDataSource(nameof(ChainOperators))]
    public async Task FlatChain_257OperandsAccepted_258Rejected(string op) {
        var (parsed, failure) = ParseOnLarge(string.Join($" {op} ", Enumerable.Repeat("Flag", Limit + 1)));
        await Assert.That(failure).IsNull();
        await Assert.That(parsed).IsNotNull();

        var (over, rejected) = ParseOnLarge(string.Join($" {op} ", Enumerable.Repeat("Flag", Limit + 2)));
        await Assert.That(over).IsNull();
        await Assert.That(((GrammarException)rejected!).Detail).IsEqualTo(DslLimits.DepthDetail);
    }

    /// <summary>F5: a chain longer than the limit stops at the first operator that crosses it, the 257th, not the last.</summary>
    [Test]
    [MethodDataSource(nameof(ChainOperators))]
    public async Task LongChain_IsPositionedAtThe257thOperator(string op) {
        var text = string.Join($" {op} ", Enumerable.Repeat("Flag", 600));
        var (parsed, failure) = ParseOnLarge(text);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo(DslLimits.DepthDetail);
        // "Flag op " is 5 + op.Length + 2 characters wide; operator k starts at (k - 1) * width + 5 + 1.
        var width = "Flag ".Length + op.Length + 1;
        await Assert.That(error.Offset).IsEqualTo(Limit * width + "Flag ".Length);
        await Assert.That(text.Substring(error.Offset, error.Length)).IsEqualTo(op);
    }

    /// <summary>A comparison adds one level over its deeper operand, so a 256-operand sum on either side sits at the limit.</summary>
    [Test]
    public async Task CompareOperands_BoundaryAt256Accepted_257Rejected() {
        foreach (var make in new Func<int, string>[] {
                     n => string.Join(" + ", Enumerable.Repeat("Flag", n)) + " > Flag",
                     n => "Flag > " + string.Join(" + ", Enumerable.Repeat("Flag", n)),
                 }) {
            foreach (var operands in new[] { Limit - 1, Limit }) {
                var (parsed, failure) = ParseOnLarge(make(operands));
                await Assert.That(failure).IsNull();
                await Assert.That(parsed).IsNotNull();
            }

            var (over, rejected) = ParseOnLarge(make(Limit + 1));
            await Assert.That(over).IsNull();
            await Assert.That(((GrammarException)rejected!).Detail).IsEqualTo(DslLimits.DepthDetail);
        }
    }

    // ── G-10: the where flag and malformed shapes the driver has to keep ───────────────

    /// <summary>
    /// A group, a call argument and a match arm each start a fresh expression, so <c>where</c> inside any of them
    /// is not "nested": <c>Parse_DeepWhereChain_IsPositionedError</c> depends on it. A directly nested <c>where</c> still is.
    /// </summary>
    [Test]
    public async Task Where_FlagResetsInGroupCallAndMatch_ButNotInPlainBody() {
        foreach (var text in new[] {
                     "Kids where (Kids where Flag)",
                     "Kids where F(Kids where Flag)",
                     "Kids where match { Kids where Flag -> 1, else -> 2 } > 0",
                     "Kids where Qty > match { Flag -> Kids where Flag, else -> 3 }",
                 }) {
            var (parsed, failure) = ParseOnLarge(text);
            await Assert.That(failure).IsNull();
            await Assert.That(parsed).IsNotNull();
        }

        var (nested, rejected) = ParseOnLarge("Kids where Kids where Flag");
        await Assert.That(nested).IsNull();
        await Assert.That(rejected).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)rejected!).Detail).Contains("Nested 'where'");
    }

    /// <summary>
    /// The malformed shapes the old longest-match fell back on and a committing driver reports directly.
    /// Every one is a positioned parse error, never a crash and never an accepted wrong tree.
    /// </summary>
    [Test]
    [Arguments("(Flag")]
    [Arguments("F(Flag")]
    [Arguments("match { Flag -> 1, else -> 2")]
    [Arguments("Kids where")]
    [Arguments("any Kids where")]
    [Arguments("Flag and")]
    [Arguments("Flag >")]
    [Arguments("Order Price >")]
    [Arguments("Order Price is not")]
    public async Task Unterminated_IsPositionedParseError(string text) {
        var (parsed, failure) = ParseOnLarge(text);

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        await Assert.That(((GrammarException)failure!).Line).IsGreaterThan(0);
    }

    /// <summary>G-04's sign-off follow-up (names G-10): a group closes at its own <c>)</c>, so a token before it is the error, at that token.</summary>
    [Test]
    public async Task Parse_ParenGroupWithTrailingToken_IsRejected() {
        var (parsed, failure) = ParseOnLarge("(Price > 3 4)");

        await Assert.That(parsed).IsNull();
        await Assert.That(failure).IsTypeOf<GrammarException>();
        var error = (GrammarException)failure!;
        await Assert.That(error.Detail).IsEqualTo("Unexpected '4' in group");
        await Assert.That(error.Line).IsEqualTo(1);
        await Assert.That(error.Column).IsEqualTo(12);
    }

    // ── F2 / A4: depth is a parameter, so nothing survives a failed parse ─────────────

    /// <summary>
    /// A4 wanted the depth reset to be un-forgettable. Both calls below run on the same thread
    /// (the expression fragment parse takes no stack hop), so this proves the thread is left clean:
    /// after a depth error the next parse on that thread parses a shallow expression. The durable guard is
    /// <see cref="A4_ParserCarriesNoMutableIntInstanceField"/>: depth is a parameter, and a field
    /// would be the stale state A4 warned about.
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

    /// <summary>A4's real pin: the parser stores no mutable int, so no depth can go stale between
    /// parses on one instance (DomainResolver reuses one parser for every expression).</summary>
    [Test]
    public async Task A4_ParserCarriesNoMutableIntInstanceField() {
        var fields = typeof(DslExpressionParser)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)
            .Where(f => !f.IsInitOnly && !f.IsLiteral)
            .Select(f => $"{f.FieldType.Name} {f.Name}")
            .ToArray();

        await Assert.That(fields).IsEmpty();
    }
}