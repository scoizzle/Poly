namespace Poly.DomainModeling.Language;

/// <summary>
/// The parser's shipped limits. OQ-4 ruled the
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
}