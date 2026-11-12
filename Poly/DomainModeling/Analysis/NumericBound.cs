namespace Poly.DomainModeling.Analysis;

/// <summary>
/// The one read of a <c>range</c> bound as a number. The framework's numeric conversion reads a null
/// argument as 0, so an absent bound must never reach it: absent in, absent out. A bound that is
/// present but cannot be ordered against a number (the #367 fail-closed path) reads as absent here
/// too; callers that must tell the two apart use <c>HasUnorderableBound</c>.
/// </summary>
internal static class NumericBound {
    /// <summary>The bound as an exact decimal, or null when it is absent or not exactly representable
    /// (a fractional or above-2^53 <see cref="double"/>): an interval endpoint never rounds (OQ-6).</summary>
    internal static decimal? ToDecimalOrNull(object? value) {
        if (value is null) return null;
        return ExactNumber.TryFrom(value)?.ToDecimal();
    }

    internal static double? ToDoubleOrNull(object? value) {
        if (value is null) return null;
        try { return Convert.ToDouble(value); }
        catch { return null; }
    }
}
