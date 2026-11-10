using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Fixture corpus for the action-parameter implicit-constraint envelope (#367 C-2). Each fixture
/// pins one shape the pack's original derivation handled; the goldens under
/// <c>Golden/ActionParameterEnvelope</c> were captured from the pre-relocation pack.
/// </summary>
internal static class ActionParameterEnvelopeFixtures {
    internal static readonly (string Name, string Poly)[] All = [
        ("unconditional_assign", """
            domain T
            Book: entity {
              Stock: Number range(0, 1000)
              Title: Text length(2, 50)
              Code: Text pattern("^[A-Z]{2}-[0-9]{3}$")
              Restock: action (amount: Number) { assign Stock to amount }
              Rename: action (value: Text) { assign Title to value }
              SetCode: action (value: Text) { assign Code to value }
            }
            """),
        ("conditional_assign", """
            domain T
            Book: entity {
              Stock: Number range(0, 1000)
              Pages: Number range(5, 50)
              Restock: action (amount: Number) {
                if (Stock > 0) { assign Stock to amount }
              }
              Either: action (amount: Number) {
                if (Stock > 0) { assign Stock to amount } else { assign Pages to amount }
              }
            }
            """),
        ("create_in_initialiser", """
            domain T
            Order: entity {
              lines: many OrderLine
              AddLine: action (q: Number) { create in lines { Qty: q } }
            }
            OrderLine: entity {
              Qty: Number range(10, 20)
              order: Order
            }
            """),
        ("optional_and_required", """
            domain T
            Note: entity {
              Body: Text
              Memo: Text optional
              SetBody: action (v: Text) { assign Body to v }
              SetMemo: action (v: Text) { assign Memo to v }
            }
            """),
        ("verified_range", """
            domain T
            Gauge: entity {
              Level: Number range(0, 100)
              Peak: Number range(0, 100)
              SetLevel: action (v: Number) { assign Level to v }
              SetPeak: action (v: Number) { assign Peak to v }
              Surge: action { assign Peak to Peak + 10 }
            }
            """),
        ("shifted_offset", """
            domain T
            Book: entity {
              Stock: Number range(0, 1000)
              Shift: action (p: Number) { assign Stock to p + 3 }
            }
            """),
        ("invoke_chain", """
            domain T
            Meter: entity {
              Qty: Number range(0, 100)
              Bump: action (amount: Number) { assign Qty to amount }
              Ship: action (amt: Number) { invoke Bump(amount: amt) }
            }
            """),
        ("multi_target_merge", """
            domain T
            Pair: entity {
              Lo: Number range(0, 100)
              Hi: Number range(50, 200)
              Code: Text pattern("^[A-Z]+$")
              Tag: Text pattern("^[0-9]+$")
              Both: action (v: Number) {
                assign Lo to v
                assign Hi to v
              }
              Conflict: action (s: Text) {
                assign Code to s
                assign Tag to s
              }
            }
            """),
    ];

    internal static Domain ParseDomain(string poly) {
        var nl = poly.IndexOf('\n');
        poly = poly[..(nl + 1)] + "uses persistence\n" + poly[(nl + 1)..];
        var ctx = ExtensionCatalog.Core.Authoring;
        var changes = new PolyDslParser(poly, ctx).Parse();
        var emptyDomain = DomainTestFactory.Create("_", [], []);
        var result = new DomainEvolution(emptyDomain).ApplyAndAnalyze(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException("Domain evolution failed: " + string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)));
        return result.Root!;
    }

    /// <summary>Stable text for a constraint list — value and CLR type of range bounds, in order.</summary>
    internal static string Format(IEnumerable<Constraint> constraints) =>
        string.Join(";", constraints.Select(c => c switch {
            RangeConstraint r => $"Range({r.Minimum ?? "null"}:{r.Minimum?.GetType().Name},{r.Maximum ?? "null"}:{r.Maximum?.GetType().Name})",
            LengthConstraint l => $"Length({l.MinLength},{l.MaxLength})",
            PatternConstraint p => $"Pattern({p.Pattern})",
            EqualityConstraint e => $"Equality({e.ExpectedValue})",
            _ => c.GetType().Name,
        }));

    internal static string GoldenDirectory([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.Combine(Path.GetDirectoryName(file)!, "Golden", "ActionParameterEnvelope");
}