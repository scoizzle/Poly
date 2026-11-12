using System.Runtime.ExceptionServices;

namespace Poly.DomainModeling.Language;

/// <summary>
/// The parser's shipped limits, and the stack every parse runs on. OQ-4 ruled the
/// expression nesting limit at 256; the parse recursion that enforces it is deep enough
/// that a 1 MB caller thread overflows below that number, so the parse runs on
/// <see cref="ParseStackBytes"/> instead. A stack overflow cannot be caught in .NET,
/// so the hop is the only fail-closed answer until the matcher recursion is iterative.
/// </summary>
public static class DslLimits {
    /// <summary>
    /// Expression nesting levels: a leaf is level 1, and a paren group, <c>not</c>, a
    /// comparison operand, a <c>where</c> body and each chain operand add a level.
    /// </summary>
    public const int MaxExpressionDepth = 256;

    /// <summary>The positioned detail the fold reports when a document exceeds the limit.</summary>
    public const string DepthDetail = "expression nesting exceeds the limit of 256";

    /// <summary>
    /// Grammar rule levels one parse may recurse through. Measured (G-09 PR body): 256 nested
    /// <c>not</c>s cost 772 rule levels; a 1 MB thread dies at ~540, so a rule level costs
    /// ~1.9 KB. 4096 levels therefore need ~7.8 MB of the 16 MB parse stack: it clears the
    /// ruled 256 (772) by 5.3x and stays inside the stack by ~2x.
    /// </summary>
    internal const int MaxRuleDepth = 4_096;

    /// <summary>
    /// Stack for the thread a parse runs on. Measured: a 1 MB thread dies at 176 paren levels
    /// and 180 <c>not</c> levels (~5.8 KB per level), 4 MB survives the ruled 256, and 16 MB
    /// keeps the whole <see cref="MaxRuleDepth"/> budget with ~2x margin left over.
    /// </summary>
    internal const int ParseStackBytes = 16 * 1024 * 1024;

    [ThreadStatic] private static int _onParseStack;

    /// <summary>
    /// Runs <paramref name="body"/> on a <see cref="ParseStackBytes"/> thread, so every caller
    /// gets the same stack regardless of the thread it calls from. Re-entrant calls run inline:
    /// a parse started from a parse already has the stack.
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