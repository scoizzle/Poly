using System.Diagnostics;
using System.Text;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>T-15 scaffolding: records what every census input does. Deleted in the slice's final commit.</summary>
public sealed class NumericLiteralCharacterisationTests {
    private static string Ent(string line) => $"domain A\nX: entity {{\n  {line}\n}}\n";

    private static string Temporal(string policy) => $"domain A\nuses temporal\nX: entity {{\n  Due: Date\n  Late: policy {{ {policy} }}\n}}\n";

    private static IEnumerable<(string Door, string Input)> Census() {
        var nines400 = new string('9', 400);
        foreach (var literal in new[] {
            "12", "1e3", "\u0661\u0662", "1.0000000000000000000000000000", "1.5", "0.12345678901234567890123456789",
            "1.00000000000000000000000000001", "0.00000000000000000000000000005", "9.9999999999999999999999999999",
            "7.9228162514264337593543950335", "7.9228162514264337593543950336", "79228162514264337593543950335",
            "79228162514264337593543950336", "99999999999999999999999999999", "99999999999999999999999999999999",
            "9223372036854775807", "9223372036854775808",
        }) {
            foreach (var type in new[] { "Number", "Decimal", "Text" })
                yield return ("R", Ent($"N: {type} default({literal})"));
            yield return ("R", Temporal($"Due > {literal}"));
            yield return ("R", Ent($"N: Text column(\"c\", {literal})"));
        }

        yield return ("R", Ent("N: Text column(\"c\", " + nines400 + ")"));
        yield return ("R", Ent("N: Text column(\"c\", 9007199254740993)"));
        yield return ("R", Ent("N: Text column(\"c\", 123456789012345678901234567890)"));
        yield return ("R", Ent("N: Text column(\"c\", 0.1)"));
        yield return ("R", Ent("N: Text column(\"c\", \u0663)"));
        yield return ("R", "domain A\nX: entity {\n  P: Number\n  Q: policy { P > \u0663 }\n}\n");
        yield return ("R", "domain A\nX: entity {\n  P: Number\n  Q: policy { P > 1\u0663 }\n}\n");
        yield return ("R", "domain A\nX: entity {\n  P\u0663: Number\n}\n");
        yield return ("R", "domain A\n// \u0663\nX: entity {\n  N: Text default(\"\u0663\")\n}\n");

        foreach (var len in new[] { "3", "2147483647", "2147483648", "1.5", "1.0", "\u0663", "007", "3, 99999999999", "1.0, 5", "3, 1.5", "00000000000000000000000000000003" })
            yield return ("R", Ent($"N: Text length({len})"));

        foreach (var bound in new[] { "0.0000000000000000000000000000000001", "9.9999999999999999999999999999", "1.0", "100.0", "99999999999999999999999999999", "\u0661\u0662", "9223372036854775808" }) {
            yield return ("R", Ent($"N: Number range({bound}, )"));
            yield return ("R", Ent($"N: Decimal range(, {bound})"));
        }

        foreach (var amount in new[] { "12", "1.5", "1.0", "99999999999999999999", "9223372036854775807", "9223372036854775808", "\u0663" }) {
            yield return ("R", Temporal($"Now - {amount} Days > Due"));
            yield return ("F", $"{amount} Days");
        }

        foreach (var f in new[] { "1.0000000000000000000000000000", "0.00000000000000000000000000005", "99999999999999999999999999999", "1.00000000000000000000000000001", "\u0663", "007" })
            yield return ("F", f);
    }

    [Test]
    public async Task Dump() {
        var sb = new StringBuilder();
        foreach (var (door, input) in Census()) {
            var result = door == "R" ? ResolverBaselineTests.Resolve(input) : ExprDifferentialTests.Result(input, digest: false);
            if (result.StartsWith("OK ", StringComparison.Ordinal) && result.Length > 400)
                result = result[..400] + "...";
            sb.Append(door).Append('\t').Append(input.Replace("\n", "\\n")).Append('\t').Append(result).Append('\n');
        }

        foreach (var shape in new[] { "int", "frac" }) {
            var digits = shape == "int" ? "1" + new string('0', 100_000) : "1." + new string('0', 99_999) + "1";
            foreach (var site in new[] { "default", "range" }) {
                var line = site == "default" ? $"N: Decimal default({digits})" : $"N: Decimal range(, {digits})";
                var sw = Stopwatch.StartNew();
                var result = ResolverBaselineTests.Resolve(Ent(line));
                sb.Append("TIME\t").Append(site).Append(' ').Append(shape).Append("\t").Append(sw.ElapsedMilliseconds).Append("ms\t").Append(result.Length > 200 ? result[..200] : result).Append('\n');
            }
        }

        if (Environment.GetEnvironmentVariable("POLY_CHARACTERISE_OUT") is { } path)
            File.WriteAllText(path, sb.ToString());
        await Assert.That(sb.Length).IsGreaterThan(0);
    }
}