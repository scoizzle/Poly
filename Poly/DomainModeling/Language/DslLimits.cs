using System.Runtime.ExceptionServices;

namespace Poly.DomainModeling.Language;

/// <summary>
/// The parser's shipped limits. OQ-4 ruled the
/// expression nesting limit at 256. The expression driver is iterative, so that limit is a counted
/// depth with a positioned error that does not depend on the host stack. The owner ruled the statement
/// nesting limit at 64 (row G-25); the structure parser counts it the same way.
/// </summary>
public static class DslLimits {
    /// <summary>
    /// Expression nesting levels. The top-level expression is level 0; a paren group, <c>not</c>,
    /// a comparison operand, a <c>where</c> body, and a call or <c>match</c> argument each add 1;
    /// a left-associative chain of N operands adds N - 1, and so does a path of N words
    /// (<c>a b c</c> nests one navigation per word after the first). Anything whose level exceeds
    /// this is a positioned parse error.
    /// </summary>
    public const int MaxExpressionDepth = 256;

    /// <summary>The positioned detail the fold reports when a document exceeds the limit.</summary>
    public const string DepthDetail = "expression nesting exceeds the limit of 256";

    /// <summary>
    /// Statement nesting levels: the number of enclosing <c>if</c> and <c>for</c> statements, the statement
    /// itself included, so a top-level <c>if</c> is level 1 and each <c>else if</c> rung is one level below the
    /// rung before it. The owner ruled 64 as a ceiling (#678, row G-25). A statement whose level exceeds this
    /// is a positioned parse error on its head keyword.
    /// </summary>
    public const int MaxStatementDepth = 64;

    /// <summary>The positioned detail the structure parser reports when a document exceeds the statement limit.</summary>
    public const string StatementDepthDetail = "statement nesting exceeds the limit of 64";

    /// <summary>
    /// Stack for the thread the compiler and the structure parser run on. The expression parser no longer needs it
    /// (its depth is a counted limit); statement nesting (<c>if</c>, <c>else if</c> ladders) has no counted limit
    /// yet, and every pass after the parse (analysis, lowering, VM compile, print) recurses over it. Measured
    /// (G-10 fix round 1): without this hop a 1 MB thread aborts at about 600 nested <c>if</c> or <c>else if</c>
    /// rungs, where the 16 MB thread survives thousands. Row G-25 bounds statement nesting and removes it.
    /// </summary>
    internal const int ParseStackBytes = 16 * 1024 * 1024;

    [ThreadStatic] private static int _onParseStack;

    /// <summary>
    /// Runs <paramref name="body"/> on a <see cref="ParseStackBytes"/> thread, so every caller gets the same stack
    /// regardless of the thread it calls from. Re-entrant calls run inline.
    /// </summary>
    public static T RunOnParseStack<T>(Func<T> body) {
        ArgumentNullException.ThrowIfNull(body);
        if (_onParseStack > 0)
            return body();

        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => {
            _onParseStack++;
            try {
                result = body();
            }
            catch (Exception ex) {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        }, ParseStackBytes) {
            IsBackground = true,
            Name = "poly-dsl-parse",
        };
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}