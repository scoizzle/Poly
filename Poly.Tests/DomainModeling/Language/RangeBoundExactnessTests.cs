using System.Reflection;

using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Grammar;
using Poly.Interpretation.CSharp;
using Poly.Runtime;
using Poly.Tests.DomainModeling.Compile;

namespace Poly.Tests.DomainModeling.Language;

/// <summary>
/// #643 / T-02: a declared range bound is exact. A Number/Int/Int64 bound is a <see cref="long"/>,
/// a Decimal bound is a <see cref="decimal"/>, and every non-Number-family bound is a decimal — none
/// is a double. The declared text survives simulate, DSL export and C# export, and a bound outside
/// its declared type's range is a positioned parse error (#515) rather than a silent widening.
/// </summary>
public class RangeBoundExactnessTests {
    private const string Dsl = """
        domain Rb

        Bnd: entity {
          P: Number range(0, 9007199254740993) default(0)
          Q: Number range(0, 9223372036854775806) default(0)
          Lo: Number range(-9223372036854775807, 0) default(0)
        }
        """;

    // ── Red on master: the bounds are doubles, so 2^53+ bounds round ─────

    [Test]
    public async Task Simulate_DeclaredNumberBounds_AreExact() {
        var (domain, _, _) = Evolve(Dsl);
        var exec = ModuleExec.For(domain);

        var p = exec.Create("Bnd", new Dictionary<string, object?> { ["P"] = 9007199254740993L });
        var q = exec.Create("Bnd", new Dictionary<string, object?> { ["Q"] = 9223372036854775807L });
        var lo = exec.Create("Bnd", new Dictionary<string, object?> { ["Lo"] = -9223372036854775808L });

        await Assert.That(p.Succeeded).IsTrue();
        await Assert.That(q.Succeeded).IsFalse();
        await Assert.That(lo.Succeeded).IsFalse();
    }

    [Test]
    public async Task DslExport_RoundTrips_AndPrintsExactLongBounds() {
        var (domain, _, _) = Evolve(Dsl);

        var printed = new DomainDslPrinter().Print(domain);

        await Assert.That(printed).Contains("range(0, 9223372036854775806)");
        await Assert.That(printed).DoesNotContain("E+");
        await Assert.That(new DomainDslPrinter().Print(Reparse(printed))).IsEqualTo(printed);
    }

    [Test]
    public async Task CSharpExport_KeepsExactBounds_AndRejectsOutOfRange() {
        var (domain, analysis, session) = Evolve(Dsl);
        var source = new CSharpGenerator().Generate(session.Lower(domain, analysis));

        await Assert.That(source).Contains("9223372036854775806");

        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(source, "RangeExactParity");
        var port = new InMemoryStoragePort();
        var p = TryCreate(assembly, "Bnd", ("P", (object?)9007199254740993L), ("db", (object?)port));
        var q = TryCreate(assembly, "Bnd", ("Q", (object?)9223372036854775807L), ("db", (object?)port));

        await Assert.That(p.Succeeded).IsTrue();
        await Assert.That(q.Succeeded).IsFalse();
    }

    // ── A4: a bound outside the declared type is a positioned error ──────

    [Test]
    public async Task NumberBound_AboveLongMax_IsPositionedParseError() {
        var error = ParseError(Entity("Number range(0, 9223372036854775808)"));

        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
    }

    [Test]
    public async Task NumberBound_TwentyDigits_IsPositionedParseError() {
        var error = ParseError(Entity("Number range(0, 99999999999999999999)"));

        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
    }

    [Test]
    public async Task NumberBound_TwentyNineDigits_ReportsOverflowNotFractional() {
        var error = ParseError(Entity("Number range(0, 99999999999999999999999999999)"));

        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
        await Assert.That(error.Detail).DoesNotContain("Fractional");
    }

    [Test]
    public async Task DecimalBound_AboveDecimalMax_IsPositionedGrammarError_NotOverflowException() {
        var error = ParseError(Entity("Decimal range(0, 79228162514264337593543950336)"));

        await Assert.That(error.Line).IsGreaterThan(0);
        await Assert.That(error.Detail).Contains("out of range for Decimal");
    }

    [Test]
    public async Task NumberBound_LongMinValue_ParsesAsLong() {
        var (domain, _, _) = Evolve(Entity("Number range(-9223372036854775808, 0)"));
        var range = Bind(domain);

        await Assert.That(range.Minimum).IsTypeOf<long>();
        await Assert.That((long)range.Minimum!).IsEqualTo(long.MinValue);
    }

    [Test]
    public async Task NumberBound_IntegralValuedFractionalText_ParsesAsLong() {
        var (domain, _, _) = Evolve(Entity("Number range(0, 100.0)"));
        var range = Bind(domain);

        await Assert.That(range.Maximum).IsTypeOf<long>();
        await Assert.That((long)range.Maximum!).IsEqualTo(100L);
    }

    // ── A1: non-Number-family bounds are both decimal ────────────────────

    [Test]
    public async Task NonNumberFamilyBounds_AreBothDecimal_AndOrderIsStillChecked() {
        var result = Apply(Entity("Double range(3, 2.6)"));
        var range = Bind(result.Root!);

        await Assert.That(range.Minimum).IsTypeOf<decimal>();
        await Assert.That(range.Maximum).IsTypeOf<decimal>();
        await Assert.That((decimal)range.Minimum!).IsEqualTo(3m);
        await Assert.That((decimal)range.Maximum!).IsEqualTo(2.6m);
        await Assert.That(result.Analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.ConstraintSatisfiability
            && d.Message.Contains("minimum exceeds maximum"))).IsTrue();
    }

    // ── F2: the assign guard compares exactly, not through double ────────

    private const string AssignDsl = """
        domain Rb

        Q: entity {
          N: Number range(0, 9223372036854775806) default(0)
          Bump: action { assign N to 9223372036854775807 }
        }
        """;

    [Test]
    public async Task Assign_ConstantAboveExactBound_IsRejected_NotGuardElided() {
        var (domain, analysis, session) = Evolve(AssignDsl);
        var exec = ModuleExec.For(domain);
        var q = exec.Create("Q", new Dictionary<string, object?>());
        var simulated = exec.Invoke(q.Instance!, "Bump", new Dictionary<string, object?>());

        var source = new CSharpGenerator().Generate(session.Lower(domain, analysis));
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(source, "RangeAssignParity");
        var port = new InMemoryStoragePort();
        var printed = TryCreate(assembly, "Q", ("db", (object?)port));
        var printedBump = TryInvoke(printed.Instance!, "Bump", ("db", (object?)port));

        await Assert.That(simulated.Succeeded).IsFalse();
        await Assert.That(printedBump.Succeeded).IsFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string Entity(string declaration) =>
        $"domain Rb\nB: entity {{\n  X: {declaration}\n}}\n";

    private static EvolutionResult Apply(string poly) {
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(new PolyDslParser(poly).Parse());
        return result;
    }

    private static (Domain Domain, AnalysisResult Analysis, DomainSession Session) Evolve(string poly) {
        var session = DomainSession.ForSource(poly, [.. ExtensionCatalog.ProductAuthoring, "persistence"]);
        var changes = new PolyDslParser(poly, session).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    private static Domain Reparse(string poly) {
        var result = Apply(poly);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return result.Root!;
    }

    private static RangeConstraint Bind(Domain domain) =>
        domain.Types.OfType<Entity>().First().Properties.First()
            .Constraints.OfType<RangeConstraint>().Single();

    private static GrammarException ParseError(string poly) {
        try {
            new PolyDslParser(poly).Parse();
        }
        catch (GrammarException ex) {
            return ex;
        }
        throw new InvalidOperationException("expected a positioned parse error");
    }

    private static (bool Succeeded, string? Message, object? Instance) TryCreate(
        Assembly assembly, string typeName, params (string Name, object? Value)[] named) {
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"missing type {typeName}");
        var create = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Create")
            .OrderByDescending(m => m.GetParameters().Length)
            .First();
        var parameters = create.GetParameters();
        var byName = named.ToDictionary(n => n.Name, n => n.Value, StringComparer.OrdinalIgnoreCase);
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var parameter = parameters[i];
            if (byName.TryGetValue(parameter.Name!, out var value))
                args[i] = value;
            else if (parameter.HasDefaultValue)
                args[i] = parameter.DefaultValue;
            else if (parameter.ParameterType == typeof(string))
                args[i] = "";
            else if (parameter.ParameterType == typeof(long))
                args[i] = 0L;
            else
                args[i] = null;
        }
        var result = create.Invoke(null, args)!;
        var succeeded = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        var message = result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string;
        var instance = result.GetType().GetProperty("Value")!.GetValue(result);
        return (succeeded, message, instance);
    }

    private static (bool Succeeded, string? Message) TryInvoke(
        object instance, string name, params (string Name, object? Value)[] named) {
        var method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"missing action {name}");
        var parameters = method.GetParameters();
        var byName = named.ToDictionary(n => n.Name, n => n.Value, StringComparer.OrdinalIgnoreCase);
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var parameter = parameters[i];
            if (byName.TryGetValue(parameter.Name!, out var value))
                args[i] = value;
            else if (parameter.HasDefaultValue)
                args[i] = parameter.DefaultValue;
            else
                args[i] = null;
        }
        var result = method.Invoke(instance, args)!;
        var succeeded = (bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!;
        var message = result.GetType().GetProperty("ErrorMessage")!.GetValue(result) as string;
        return (succeeded, message);
    }
}
