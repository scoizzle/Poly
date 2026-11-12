namespace Poly.DomainModeling.Language;

/// <summary>STUB for the G-09 red run: the constants, and no stack hop.</summary>
public static class DslLimits {
    public const int MaxExpressionDepth = 256;
    public const string DepthDetail = "expression nesting exceeds the limit of 256";

    public static T RunOnParseStack<T>(Func<T> body) => body();
}