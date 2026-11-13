using System.Globalization;

using Poly.Ast.Nodes;
using Poly.Interpretation;
using Poly.Introspection.CommonLanguageRuntime;

namespace Poly.Tests.Interpretation.Analysis;

/// <summary>
/// T-17 fix round: a date offset (<c>Date ± number</c>) inside another node. The provider defines no operator for it, so the
/// resolver and the compatibility rewrite share one interim rule (<c>DateOffsetRule</c>, T-10 removes it): the offset is
/// stamped with the date type the rewrite produces, so a parent sees what the rewrite will produce and is checked as on
/// master. Each shape is pinned with master's verdict and value (<see cref="Expected"/> was generated on origin/master
/// c4a670b9 before the fix, and the shapes that differed were red on the T-17 head).
/// </summary>
public class DateOffsetNestingTests {
    public static class Calls {
        public static DateTime TakesDate(DateTime value) => value;
        public static int TakesInt(int value) => value;
    }

    private static Parameter P(string name, Type type) => new(name, new ClrTypeReference(type));

    private static Node K(object value) => new Constant(value);

    private static Node Call(string method, Node argument) => new Invoke(new Member(new ClrTypeReference(typeof(Calls)), method), argument);

    private static string Format(object? value) => value switch {
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
    };

    private static string Run(Node node, object?[] args) {
        var analysis = Interpreter.Analyze(node, ClrTypeDefinitionRegistry.Shared);
        var errors = string.Join(" / ", analysis.Diagnostics.Where(static d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error).Select(static d => d.Message));
        if (errors.Length > 0)
            return "ERROR: " + errors;
        try {
            var program = Interpreter.Compile(node, analysis);
            using var execution = Interpreter.Execute(program, args);
            var type = program.RootClrType;
            object? value = type == typeof(bool) ? execution.GetValue<bool>()
                : type == typeof(DateTime) ? execution.GetValue<DateTime>()
                : type == typeof(DateOnly) ? execution.GetValue<DateOnly>()
                : type == typeof(DateTimeOffset) ? execution.GetValue<DateTimeOffset>()
                : type == typeof(TimeSpan) ? execution.GetValue<TimeSpan>()
                : type == typeof(DateTime?) ? execution.GetValue<DateTime?>()
                : type == typeof(int) ? execution.GetValue<int>() : execution.GetValue<object>();
            return $"OK {type?.Name} {Format(value)}";
        }
        catch (Exception ex) {
            return $"THROWS {ex.GetType().Name}";
        }
    }

    public static IEnumerable<(string Name, Func<string> Run)> Cases() {
        foreach (var (name, type, value) in new (string, Type, object)[] {
            ("DateTime", typeof(DateTime), new DateTime(2026, 1, 1)), ("DateOnly", typeof(DateOnly), new DateOnly(2026, 1, 1)), ("TimeOnly", typeof(TimeOnly), new TimeOnly(10, 0)),
            ("DateTimeOffset", typeof(DateTimeOffset), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), ("TimeSpan", typeof(TimeSpan), TimeSpan.FromDays(1)),
            ("DateTime?", typeof(DateTime?), new DateTime(2026, 1, 1)) }) {
            Node d() => P("d", type);
            yield return ($"{name} (d+1)+2", () => Run(new Add(new Add(d(), K(1)), K(2)), [value]));
            yield return ($"{name} (d+1)>5", () => Run(new GreaterThan(new Add(d(), K(1)), K(5)), [value]));
            yield return ($"{name} (d-1)-2", () => Run(new Subtract(new Subtract(d(), K(1)), K(2)), [value]));
            yield return ($"{name} (d+1)-2", () => Run(new Subtract(new Add(d(), K(1)), K(2)), [value]));
            yield return ($"{name} 2+(d+1)", () => Run(new Add(K(2), new Add(d(), K(1))), [value]));
            yield return ($"{name} (d+1)==d2", () => Run(new Equal(new Add(d(), K(1)), P("d2", type)), [value, value]));
            yield return ($"{name} d+1", () => Run(new Add(d(), K(1)), [value]));
            yield return ($"{name} c?d+1:d", () => Run(new Conditional(P("c", typeof(bool)), new Add(d(), K(1)), d()), [true, value]));
            yield return ($"{name} TakesDate(d+1)", () => Run(Call("TakesDate", new Add(d(), K(1))), [value]));
            yield return ($"{name} TakesInt(d+1)", () => Run(Call("TakesInt", new Add(d(), K(1))), [value]));
            yield return ($"{name} d+1m", () => Run(new Add(d(), K(1m)), [value]));
            yield return ($"{name} d-1m", () => Run(new Subtract(d(), K(1m)), [value]));
            yield return ($"{name} (d+1)+2m", () => Run(new Add(new Add(d(), K(1)), K(2m)), [value]));
            yield return ($"{name} (d+1)-2m", () => Run(new Subtract(new Add(d(), K(1)), K(2m)), [value]));
        }
        var day = new DateTime(2026, 1, 1);
        yield return ("DateTime (d+n)+2, n: int?", () => Run(new Add(new Add(P("d", typeof(DateTime)), P("n", typeof(int?))), K(2)), [day, 1]));
        yield return ("DateTime (d+n)>5, n: int?", () => Run(new GreaterThan(new Add(P("d", typeof(DateTime)), P("n", typeof(int?))), K(5)), [day, 1]));
        yield return ("DateTime d+n, n: int?", () => Run(new Add(P("d", typeof(DateTime)), P("n", typeof(int?))), [day, 1]));
    }

    [Test]
    public async Task DateOffsetShapes_HaveMastersVerdictAndValue() {
        var generate = Environment.GetEnvironmentVariable("DATEPIN_OUT");
        var actual = Cases().Select(static c => $"{c.Name}\t{c.Run()}").ToArray();
        if (generate is not null) {
            File.WriteAllLines(generate, actual);
            return;
        }
        await Assert.That(actual).IsEquivalentTo(Expected);
    }

    private static readonly string[] Expected = [
        "DateTime (d+1)+2\tOK DateTime 2026-01-04 00:00",
        "DateTime (d+1)>5\tERROR: comparison between incompatible types 'DateTime' and 'Int32': no GreaterThan operator for these types",
        "DateTime (d-1)-2\tOK DateTime 2025-12-29 00:00",
        "DateTime (d+1)-2\tOK DateTime 2025-12-31 00:00",
        "DateTime 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'DateTime'): no Add operator for these types",
        "DateTime (d+1)==d2\tOK Boolean false",
        "DateTime d+1\tOK DateTime 2026-01-02 00:00",
        "DateTime c?d+1:d\tOK DateTime 2026-01-02 00:00",
        "DateTime TakesDate(d+1)\tOK DateTime 2026-01-02 00:00",
        "DateTime TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "DateTime d+1m\tERROR: arithmetic operand is not numeric (got 'DateTime' and 'Decimal'): no Add operator for these types",
        "DateTime d-1m\tERROR: arithmetic operand is not numeric (got 'DateTime' and 'Decimal'): no Subtract operator for these types",
        "DateTime (d+1)+2m\tERROR: arithmetic operand is not numeric (got 'DateTime' and 'Decimal'): no Add operator for these types",
        "DateTime (d+1)-2m\tERROR: arithmetic operand is not numeric (got 'DateTime' and 'Decimal'): no Subtract operator for these types",
        "DateOnly (d+1)+2\tOK DateOnly 2026-01-04",
        "DateOnly (d+1)>5\tERROR: comparison between incompatible types 'DateOnly' and 'Int32': no GreaterThan operator for these types",
        "DateOnly (d-1)-2\tOK DateOnly 2025-12-29",
        "DateOnly (d+1)-2\tOK DateOnly 2025-12-31",
        "DateOnly 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'DateOnly'): no Add operator for these types",
        "DateOnly (d+1)==d2\tOK Boolean false",
        "DateOnly d+1\tOK DateOnly 2026-01-02",
        "DateOnly c?d+1:d\tOK DateOnly 2026-01-02",
        "DateOnly TakesDate(d+1)\tERROR: no matching member 'TakesDate' on 'Calls' for invoke with 1 argument(s)",
        "DateOnly TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "DateOnly d+1m\tERROR: arithmetic operand is not numeric (got 'DateOnly' and 'Decimal'): no Add operator for these types",
        "DateOnly d-1m\tERROR: arithmetic operand is not numeric (got 'DateOnly' and 'Decimal'): no Subtract operator for these types",
        "DateOnly (d+1)+2m\tERROR: arithmetic operand is not numeric (got 'DateOnly' and 'Decimal'): no Add operator for these types",
        "DateOnly (d+1)-2m\tERROR: arithmetic operand is not numeric (got 'DateOnly' and 'Decimal'): no Subtract operator for these types",
        "TimeOnly (d+1)+2\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly') / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly (d+1)>5\tERROR: comparison between incompatible types 'TimeOnly' and 'Int32': no GreaterThan operator for these types / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly (d-1)-2\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly') / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly (d+1)-2\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly') / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'TimeOnly'): no Add operator for these types / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly (d+1)==d2\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly d+1\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly c?d+1:d\tERROR: temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly TakesDate(d+1)\tERROR: no matching member 'TakesDate' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly d+1m\tERROR: arithmetic operand is not numeric (got 'TimeOnly' and 'Decimal'): no Add operator for these types",
        "TimeOnly d-1m\tERROR: arithmetic operand is not numeric (got 'TimeOnly' and 'Decimal'): no Subtract operator for these types",
        "TimeOnly (d+1)+2m\tERROR: arithmetic operand is not numeric (got 'TimeOnly' and 'Decimal'): no Add operator for these types / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "TimeOnly (d+1)-2m\tERROR: arithmetic operand is not numeric (got 'TimeOnly' and 'Decimal'): no Subtract operator for these types / temporal offset requires a date type with AddDays (got 'TimeOnly')",
        "DateTimeOffset (d+1)+2\tOK DateTimeOffset 2026-01-04 00:00 +00:00",
        "DateTimeOffset (d+1)>5\tERROR: comparison between incompatible types 'DateTimeOffset' and 'Int32': no GreaterThan operator for these types",
        "DateTimeOffset (d-1)-2\tOK DateTimeOffset 2025-12-29 00:00 +00:00",
        "DateTimeOffset (d+1)-2\tOK DateTimeOffset 2025-12-31 00:00 +00:00",
        "DateTimeOffset 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'DateTimeOffset'): no Add operator for these types",
        "DateTimeOffset (d+1)==d2\tOK Boolean false",
        "DateTimeOffset d+1\tOK DateTimeOffset 2026-01-02 00:00 +00:00",
        "DateTimeOffset c?d+1:d\tOK DateTimeOffset 2026-01-02 00:00 +00:00",
        "DateTimeOffset TakesDate(d+1)\tERROR: no matching member 'TakesDate' on 'Calls' for invoke with 1 argument(s)",
        "DateTimeOffset TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s)",
        "DateTimeOffset d+1m\tERROR: arithmetic operand is not numeric (got 'DateTimeOffset' and 'Decimal'): no Add operator for these types",
        "DateTimeOffset d-1m\tERROR: arithmetic operand is not numeric (got 'DateTimeOffset' and 'Decimal'): no Subtract operator for these types",
        "DateTimeOffset (d+1)+2m\tERROR: arithmetic operand is not numeric (got 'DateTimeOffset' and 'Decimal'): no Add operator for these types",
        "DateTimeOffset (d+1)-2m\tERROR: arithmetic operand is not numeric (got 'DateTimeOffset' and 'Decimal'): no Subtract operator for these types",
        "TimeSpan (d+1)+2\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan') / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan (d+1)>5\tERROR: comparison between incompatible types 'TimeSpan' and 'Int32': no GreaterThan operator for these types / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan (d-1)-2\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan') / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan (d+1)-2\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan') / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'TimeSpan'): no Add operator for these types / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan (d+1)==d2\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan d+1\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan c?d+1:d\tERROR: temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan TakesDate(d+1)\tERROR: no matching member 'TakesDate' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan d+1m\tERROR: arithmetic operand is not numeric (got 'TimeSpan' and 'Decimal'): no Add operator for these types",
        "TimeSpan d-1m\tERROR: arithmetic operand is not numeric (got 'TimeSpan' and 'Decimal'): no Subtract operator for these types",
        "TimeSpan (d+1)+2m\tERROR: arithmetic operand is not numeric (got 'TimeSpan' and 'Decimal'): no Add operator for these types / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "TimeSpan (d+1)-2m\tERROR: arithmetic operand is not numeric (got 'TimeSpan' and 'Decimal'): no Subtract operator for these types / temporal offset requires a date type with AddDays (got 'TimeSpan')",
        "DateTime? (d+1)+2\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1') / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d+1)>5\tERROR: comparison between incompatible types 'Nullable`1' and 'Int32' / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d-1)-2\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1') / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d+1)-2\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1') / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? 2+(d+1)\tERROR: arithmetic operand is not numeric (got 'Int32' and 'Nullable`1'): no Add operator for these types / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d+1)==d2\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? d+1\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? c?d+1:d\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? TakesDate(d+1)\tERROR: no matching member 'TakesDate' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? TakesInt(d+1)\tERROR: no matching member 'TakesInt' on 'Calls' for invoke with 1 argument(s) / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? d+1m\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? d-1m\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d+1)+2m\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1') / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime? (d+1)-2m\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1') / temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime (d+n)+2, n: int?\tERROR: temporal offset requires a date type with AddDays (got 'Nullable`1')",
        "DateTime (d+n)>5, n: int?\tERROR: comparison between incompatible types 'Nullable`1' and 'Int32'",
        "DateTime d+n, n: int?\tOK DateTime 2026-01-02 00:00"
    ];
}