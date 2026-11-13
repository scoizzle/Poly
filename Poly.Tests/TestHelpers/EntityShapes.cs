namespace Poly.Tests.TestHelpers;

/// <summary>
/// T-05b census, B-entity in the DSL: each FlipsB B-entity rule shape written as a whole document where the DSL can
/// express it, with the scenario every residual cell runs. The FlipsB B-entity rows themselves are API-built.
/// </summary>
internal static class EntityShapes {
    internal const string Fixture = """
        domain T
        Money: value {
          Amount: Number
        }
        Book: entity {
          Qty: Number default(1)
        }
        Part: entity {
          Qty: Number default(1)
        }
        Order: entity {
          N: Number default(1)
          Flag: Boolean default(true)
          part: Part
          ACTIONS
        }
        FUNCTIONS
        """;

    internal static readonly (string Shape, string Actions, string Functions, string ArgsJson)[] Shapes = [
        ("entity parameter into a Number property", "Go: action (p: Part) { assign N to p }", "", """{"p":{}}"""),
        ("entity parameter compared with a Number", "Go: action (p: Part) { if (p == N) { assign N to 2 } }", "", """{"p":{}}"""),
        ("entity parameter compared with an entity of another type", "Go: action (p: Part, b: Book) { if (p == b) { assign N to 2 } }", "", """{"p":{},"b":{}}"""),
        ("to-one navigation read as a value, compared with a Number", "Go: action { if (part == N) { assign N to 2 } }", "", "{}"),
        ("value-type parameter into a Number property", "Go: action (m: Money) { assign N to m }", "", """{"m":{"Amount":2}}"""),
        ("value-type parameter compared with a Number", "Go: action (m: Money) { if (m == N) { assign N to 2 } }", "", """{"m":{"Amount":2}}"""),
        ("Number into a Binary property", "Data: Binary optional\n  Go: action { assign Data to N }", "", "{}"),
        ("Text literal into a Binary property", "Data: Binary optional\n  Go: action { assign Data to \"x\" }", "", "{}"),
        ("entity parameter as a Number function argument", "Go: action (p: Part) { if (F(p) == 1) { assign N to 2 } }", "function F(v: Number): Number = 1", """{"p":{}}"""),
        ("entity-typed function parameter returned as Number (function never called)", "Go: action { assign N to 1 }", "function G(p: Part): Number = p", "{}"),
        ("entity-typed function parameter returned as Number (called)", "Go: action { assign N to G(part) }", "function G(p: Part): Number = p", "{}"),
        ("to-one navigation as a Number function argument", "Go: action { if (F(part) == 1) { assign N to 2 } }", "function F(v: Number): Number = 1", "{}"),
        ("to-one navigation into a Number property", "Go: action { assign N to part }", "", "{}"),
    ];

    internal static string Document((string Shape, string Actions, string Functions, string ArgsJson) s) =>
        Fixture.Replace("ACTIONS", s.Actions, StringComparison.Ordinal).Replace("FUNCTIONS", s.Functions, StringComparison.Ordinal);

    internal static string Rows() {
        var lines = new List<string> { "B-entity DSL shape = analysis | doors" };
        foreach (var s in Shapes) {
            var dsl = Document(s);
            var result = GateParity.Check(dsl, SimStep.Create("Order", alias: "o"), SimStep.Invoke("o", "Go", s.ArgsJson));
            var doors = !result.Analysis
                ? $"analysis rejects ({StrictGateCensus.Verdict(dsl)}: {result.AnalysisSummary.Split('\n')[0]})"
                : $"analysis ok | simulate={Door(result.Simulate, result.SimulateSummary)} csharp={Door(result.CSharp, result.CSharpSummary)} polyc={Door(result.PolycCheck, result.PolycSummary)}";
            lines.Add($"{s.Shape} = {doors}");
        }
        return string.Join('\n', lines);
    }

    private static string Door(bool? ok, string? summary) {
        var first = (summary ?? "").Split('\n')[0].Trim();
        return ok is true ? "ok" : $"REFUSES ({first[..Math.Min(140, first.Length)]})";
    }
}