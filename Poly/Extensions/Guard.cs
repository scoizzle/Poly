namespace Poly.Extensions;

public static class Guard {

    public static string ThrowIfNullOrEmpty(string value, [CallerArgumentExpression("value")] string paramName = "") {
        ArgumentException.ThrowIfNullOrEmpty(value, paramName);
        return value;
    }

    public static string ThrowIfNullOrWhiteSpace(string value, [CallerArgumentExpression("value")] string paramName = "") {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}