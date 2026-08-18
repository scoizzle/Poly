namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>Accepted duration unit spellings (singular and plural), exact PascalCase.</summary>
public static class DurationForm {
    internal static bool TryGetUnit(string text, out DurationUnit unit) {
        switch (text) {
            case "Day":
            case "Days":
                unit = DurationUnit.Days;
                return true;
            case "Month":
            case "Months":
                unit = DurationUnit.Months;
                return true;
            default:
                unit = default;
                return false;
        }
    }
}