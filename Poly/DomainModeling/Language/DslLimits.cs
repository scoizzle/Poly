using System.Runtime.ExceptionServices;

namespace Poly.DomainModeling.Language;

/// <summary>
/// The parser's shipped limits, and the stack every parse runs on. OQ-4 ruled the
/// expression nesting limit at 256. The expression driver is iterative, so that limit is a counted
/// depth with a positioned error that does not depend on the host stack.
/// </summary>
public static class DslLimits {
    /// <summary>
    /// Expression nesting levels. The top-level expression is level 0; a paren group, <c>not</c>,
    /// a comparison operand, a <c>where</c> body, and a call or <c>match</c> argument each add 1;
    /// a left-associative chain of N operands adds N - 1. Anything whose level exceeds this is a
    /// positioned parse error.
    /// </summary>
    public const int MaxExpressionDepth = 256;

    /// <summary>The positioned detail the fold reports when a document exceeds the limit.</summary>
    public const string DepthDetail = "expression nesting exceeds the limit of 256";

    /// <summary>
    /// Stack for the thread a parse runs on. Measured: a 1 MB thread dies at 176 paren levels
    /// and 180 <c>not</c> levels (~5.8 KB per level), 4 MB survives the ruled 256, and 16 MB
    /// keeps the whole ruled limit with margin left over.
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