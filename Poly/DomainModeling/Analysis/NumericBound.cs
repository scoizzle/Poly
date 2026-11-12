namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The one read of a <c>range</c> bound as a number. The framework's numeric conversion reads a null
/// argument as 0, so an absent bound must never reach it: absent in, absent out. A bound that is
/// present but cannot be ordered against a number (the #367 fail-closed path) reads as absent here
/// too; callers that must tell the two apart use <c>HasUnorderableBound</c>.
/// </summary>
internal static class NumericBound {
    internal static double? ToDoubleOrNull(object? value) {
        if (value is null) return null;
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }
}