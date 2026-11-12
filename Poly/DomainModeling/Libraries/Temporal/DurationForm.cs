namespace Poly.DomainModeling.Libraries.Temporal;

/// <summary>
/// Folds duration units and spells date operations. The accepted spellings live in
/// <see cref="DurationUnits"/>: <c>Number</c> + a unit word is a duration, any other word is not.
/// </summary>
public static class DurationForm {
    internal static bool TryGetUnit(string text, out DurationUnit unit) =>
        DurationUnits.TryGetUnit(text, out unit);

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

/// <summary>
/// The duration unit table: the one place that decides whether a word is a unit. Closed and
/// static — a library that wants another unit adds a row here; nothing registers at parse time.
/// <see cref="DurationForm"/> folds with it and <c>DslGrammar</c> gates the duration pattern on
/// it, so <c>3 Days</c> is a duration and <c>3 function</c> is not.
/// </summary>
internal static class DurationUnits {
    /// <summary>Accepted spellings, their unit, and the canonical (plural) spelling for each unit.</summary>
    private static readonly (string Spelling, DurationUnit Unit, string Canonical)[] Rows = [
        ("ms", DurationUnit.Milliseconds, "Milliseconds"),
        ("Millisecond", DurationUnit.Milliseconds, "Milliseconds"),
        ("Milliseconds", DurationUnit.Milliseconds, "Milliseconds"),
        ("Second", DurationUnit.Seconds, "Seconds"),
        ("Seconds", DurationUnit.Seconds, "Seconds"),
        ("Minute", DurationUnit.Minutes, "Minutes"),
        ("Minutes", DurationUnit.Minutes, "Minutes"),
        ("Hour", DurationUnit.Hours, "Hours"),
        ("Hours", DurationUnit.Hours, "Hours"),
        ("Day", DurationUnit.Days, "Days"),
        ("Days", DurationUnit.Days, "Days"),
        ("Week", DurationUnit.Weeks, "Weeks"),
        ("Weeks", DurationUnit.Weeks, "Weeks"),
        ("Month", DurationUnit.Months, "Months"),
        ("Months", DurationUnit.Months, "Months"),
        ("Year", DurationUnit.Years, "Years"),
        ("Years", DurationUnit.Years, "Years"),
    ];

    /// <summary>Every accepted spelling, in table order.</summary>
    internal static IReadOnlyList<string> Spellings { get; } = [.. Rows.Select(r => r.Spelling)];

    /// <summary>One canonical (plural) spelling per unit, in table order.</summary>
    internal static IReadOnlyList<string> CanonicalSpellings { get; } =
        [.. Rows.Select(r => r.Canonical).Distinct(StringComparer.Ordinal)];

    /// <summary>The legal units, for a positioned parse error.</summary>
    internal static string LegalUnits { get; } =
        string.Join(", ", CanonicalSpellings)
        + " (milliseconds also as 'ms'; singular and plural accepted, exact PascalCase)";

    internal static bool IsUnit(ReadOnlySpan<char> text) => TryGetUnit(text, out _);

    internal static bool TryGetUnit(ReadOnlySpan<char> text, out DurationUnit unit) {
        foreach (var row in Rows) {
            if (text.SequenceEqual(row.Spelling)) {
                unit = row.Unit;
                return true;
            }
        }
        unit = default;
        return false;
    }
}