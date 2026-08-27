namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Accepted duration unit spellings: PascalCase singular/plural, plus <c>ms</c> for milliseconds.
/// </summary>
public static class DurationForm {
    internal static bool TryGetUnit(string text, out DurationUnit unit) {
        switch (text) {
            case "ms":
            case "Millisecond":
            case "Milliseconds":
                unit = DurationUnit.Milliseconds;
                return true;
            case "Second":
            case "Seconds":
                unit = DurationUnit.Seconds;
                return true;
            case "Minute":
            case "Minutes":
                unit = DurationUnit.Minutes;
                return true;
            case "Hour":
            case "Hours":
                unit = DurationUnit.Hours;
                return true;
            case "Day":
            case "Days":
                unit = DurationUnit.Days;
                return true;
            case "Week":
            case "Weeks":
                unit = DurationUnit.Weeks;
                return true;
            case "Month":
            case "Months":
                unit = DurationUnit.Months;
                return true;
            case "Year":
            case "Years":
                unit = DurationUnit.Years;
                return true;
            default:
                unit = default;
                return false;
        }
    }

    internal static DateOperationKind ToKind(DurationUnit unit) => unit switch {
        DurationUnit.Milliseconds => DateOperationKind.AddMilliseconds,
        DurationUnit.Seconds => DateOperationKind.AddSeconds,
        DurationUnit.Minutes => DateOperationKind.AddMinutes,
        DurationUnit.Hours => DateOperationKind.AddHours,
        DurationUnit.Days => DateOperationKind.AddDays,
        DurationUnit.Weeks => DateOperationKind.AddWeeks,
        DurationUnit.Months => DateOperationKind.AddMonths,
        DurationUnit.Years => DateOperationKind.AddYears,
        _ => throw new NotSupportedException($"Duration unit '{unit}' cannot fold to a date operation."),
    };

    internal static string Spell(DateOperationKind kind) => kind switch {
        DateOperationKind.AddMilliseconds => "Milliseconds",
        DateOperationKind.AddSeconds => "Seconds",
        DateOperationKind.AddMinutes => "Minutes",
        DateOperationKind.AddHours => "Hours",
        DateOperationKind.AddDays => "Days",
        DateOperationKind.AddWeeks => "Weeks",
        DateOperationKind.AddMonths => "Months",
        DateOperationKind.AddYears => "Years",
        _ => throw new InvalidOperationException(
            $"Cannot print DateOperation: kind '{kind}' has no DSL spelling."),
    };

    internal static bool IsClockResolution(DateOperationKind kind) => kind is
        DateOperationKind.AddMilliseconds or DateOperationKind.AddSeconds
        or DateOperationKind.AddMinutes or DateOperationKind.AddHours;

    internal static bool IsCalendarResolution(DateOperationKind kind) => kind is
        DateOperationKind.AddDays or DateOperationKind.AddWeeks
        or DateOperationKind.AddMonths or DateOperationKind.AddYears;
}