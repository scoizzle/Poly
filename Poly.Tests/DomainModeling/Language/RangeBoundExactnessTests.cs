using System.Reflection;
using Poly.Tests.TestHelpers;

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
        var poly = Entity("Number range(0, 9223372036854775808)");
        var error = ParseError(poly);

        await Assert.That(error.Line).IsEqualTo(LineOf(poly, "9223372036854775808"));
        await Assert.That(error.Column).IsEqualTo(ColumnOf(poly, "9223372036854775808"));
        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
    }

    [Test]
    public async Task NumberBound_TwentyDigits_IsPositionedParseError() {
        var poly = Entity("Number range(0, 99999999999999999999)");
        var error = ParseError(poly);

        await Assert.That(error.Column).IsEqualTo(ColumnOf(poly, "99999999999999999999"));
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
        var poly = Entity("Decimal range(0, 79228162514264337593543950336)");
        var error = ParseError(poly);

        await Assert.That(error.Column).IsEqualTo(ColumnOf(poly, "79228162514264337593543950336"));
        await Assert.That(error.Detail).Contains("out of range for Decimal");
    }

    [Test]
    public async Task DecimalBound_TrailingZeros_KeepDeclaredScale() {
        var (domain, _, _) = Evolve(Entity("Decimal range(0.00, 100.00)"));
        var range = Bind(domain);

        await Assert.That(new DomainDslPrinter().Print(domain)).Contains("range(0.00, 100.00)");
        await Assert.That((decimal)range.Maximum!).IsEqualTo(100.00m);
        await Assert.That((decimal.GetBits((decimal)range.Maximum!)[3] >> 16) & 0xFF).IsEqualTo(2);
    }

    [Test]
    public async Task DecimalBound_BeyondDecimalPrecision_IsPositionedParseError() {
        // 1e-29 needs scale 29; decimal.TryParse silently rounds it to 0 (D2).
        var poly = Entity("Decimal range(0, 0.00000000000000000000000000005)");
        var error = ParseError(poly);

        await Assert.That(error.Column).IsEqualTo(ColumnOf(poly, "0.00000000000000000000000000005"));
        await Assert.That(error.Detail).Contains("out of range for Decimal");
    }

    [Test]
    public async Task NumberBound_TwentyNineDigitsWithFraction_ReportsOverflow() {
        // 29 significant digits plus ".0" is still integral text; overflow, never "Fractional".
        var error = ParseError(Entity("Number range(0, 99999999999999999999999999999.0)"));

        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
        await Assert.That(error.Detail).DoesNotContain("Fractional");
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
        var poly = Entity("Text range(3, 2.6)");
        var range = new PolyDslParser(poly).Parse()
            .OfType<AddConstraintToPropertyChange>()
            .Select(c => c.Constraint)
            .OfType<RangeConstraint>()
            .Single();

        await Assert.That(range.Minimum).IsTypeOf<decimal>();
        await Assert.That(range.Maximum).IsTypeOf<decimal>();
        await Assert.That((decimal)range.Minimum!).IsEqualTo(3m);
        await Assert.That((decimal)range.Maximum!).IsEqualTo(2.6m);

        var result = Apply(poly);
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
    public async Task Assign_ConstantAboveExactBound_IsRejectedBeforeLowering() {
        // The probe reproduced on master (the sim accepted …807); the exact bound parse now makes
        // the analysis gate reject the literal before any guard is elided. The lowering guard's
        // double comparison stays as defense-in-depth (see EffectLoweringPass.Assign.cs).
        var result = ApplySession(AssignDsl, out _);
        var violation = result.Analysis.Diagnostics.Single(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(violation.Message).Contains("9223372036854775807");
        await Assert.That(violation.Message).Contains("range(0, 9223372036854775806)");
    }

    [Test]
    public async Task Assign_LongLiteralAboveDecimalBound_IsRejectedBeforeLowering() {
        // F2 forcing test (#643): a Decimal target with a long literal is the mixed-type path.
        // Through double both sides read 9223372036854775807.0/…806.0 and compare equal, so the
        // analysis gate used to pass and the lowering guard was the only defence. V-05a made the
        // mixed compare exact, so the gate now rejects the literal before any guard is elided.
        const string dsl = """
            domain Rb

            D: entity {
              N: Decimal range(0, 9223372036854775806) default(0)
              Bump: action { assign N to 9223372036854775807 }
            }
            """;
        var result = ApplySession(dsl, out _);

        await Assert.That(result.Succeeded).IsFalse();
        var violation = result.Analysis.Diagnostics.First(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation);
        await Assert.That(violation.Message).Contains("9223372036854775807");
        await Assert.That(violation.Message).Contains("range(0, 9223372036854775806)");
    }

    [Test]
    public async Task Assign_LongValueAboveDecimalBound_ParameterGuardAndPrintReject() {
        // The runtime guard must still keep the value out when analysis cannot see it (a parameter).
        const string dsl = """
            domain Rb

            D: entity {
              N: Decimal range(0, 9223372036854775806) default(0)
              Bump: action (v: Number) { assign N to v }
            }
            """;
        var (domain, analysis, session) = Evolve(dsl);
        var exec = ModuleExec.For(domain);
        var instance = exec.Create("D", new Dictionary<string, object?>());
        var above = new Dictionary<string, object?> { ["v"] = 9223372036854775807L };
        var simulated = exec.Invoke(instance.Instance!, "Bump", above);

        var source = new CSharpGenerator().Generate(session.Lower(domain, analysis));
        var assembly = WhenAnySimulatePrintAgreeTests.CompileGenerated(source, "DecimalBoundGuard");
        var printed = TryCreate(assembly, "D", ("db", (object?)new InMemoryStoragePort()));
        var printedBump = TryInvoke(printed.Instance!, "Bump", ("v", 9223372036854775807L));

        await Assert.That(simulated.Succeeded).IsFalse();
        await Assert.That(printedBump.Succeeded).IsFalse();
    }

    // ── A3: ExactNumber ops ──────────────────────────────────────────────

    [Test]
    public async Task ExactNumber_Multiply_SaturatesLongMaxSquared() {
        var max = ExactNumber.TryFrom(long.MaxValue);

        await Assert.That(max).IsNotNull();
        await Assert.That(max!.Value.TryMultiply(max.Value)).IsNull();
    }

    [Test]
    public async Task ExactNumber_Multiply_LongMaxByTwo_IsExact() {
        var product = ExactNumber.TryFrom(long.MaxValue)!.Value
            .TryMultiply(ExactNumber.TryFrom(2L)!.Value);

        await Assert.That(product).IsNotNull();
        await Assert.That(product!.Value.ToDecimal()).IsEqualTo(18446744073709551614m);
    }

    [Test]
    public async Task ExactNumber_Compare_SeparatesValuesEqualUnderDouble() {
        var large = ExactNumber.Compare(9223372036854775807L, 9223372036854775808m);
        var nearTwoPow53 = ExactNumber.Compare(9007199254740993L, 9007199254740992m);

        await Assert.That(large!.Value).IsEqualTo(-1);
        await Assert.That(nearTwoPow53!.Value).IsEqualTo(1);
    }

    [Test]
    public async Task ExactNumber_Compare_UnrepresentableDouble_IsNull() {
        await Assert.That(ExactNumber.Compare(9007199254740994d, 9007199254740992d)).IsNull();
        await Assert.That(ExactNumber.Compare(1.5d, 1m)).IsNull();
    }

    [Test]
    public async Task ExactNumber_TryFrom_Double_OnlyExactIntegersWithinTwoToThe53() {
        await Assert.That(ExactNumber.TryFrom(9007199254740992d)).IsNotNull();
        await Assert.That(ExactNumber.TryFrom(9007199254740994d)).IsNull();
        await Assert.That(ExactNumber.TryFrom(1.5d)).IsNull();
        await Assert.That(ExactNumber.TryFrom(double.PositiveInfinity)).IsNull();
    }

    [Test]
    public async Task ExactNumber_Multiply_InexactScale_IsNullAgainstExactProduct() {
        var tiny = ExactNumber.TryFrom(0.0000000000000000000000000001m)!;
        var tenth = ExactNumber.TryFrom(0.1m)!;
        // The exact product is 1e-29 (mantissa 1, scale 29); decimal cannot carry scale 29.
        var inexact = tiny.Value.TryMultiply(tenth.Value);
        // Scaling by 10 is exact and representable.
        var shifted = tiny.Value.TryMultiply(ExactNumber.TryFrom(10m)!.Value);

        await Assert.That(inexact).IsNull();
        await Assert.That(shifted).IsNotNull();
        await Assert.That(shifted!.Value.ToDecimal()).IsEqualTo(0.000000000000000000000000001m);
    }

    [Test]
    public async Task ExactNumber_AddEdges() {
        var max = ExactNumber.TryFrom(long.MaxValue)!.Value;
        var sum = max.TryAdd(max);
        var overflow = ExactNumber.TryFrom(decimal.MaxValue)!.Value.TryAdd(ExactNumber.TryFrom(1m)!.Value);
        var third = ExactNumber.TryFrom(0.1m)!.Value.TryAdd(ExactNumber.TryFrom(0.2m)!.Value);

        await Assert.That(sum).IsNotNull();
        await Assert.That(sum!.Value.ToDecimal()).IsEqualTo(18446744073709551614m);
        await Assert.That(overflow).IsNull();
        await Assert.That(third).IsNotNull();
        await Assert.That(third!.Value.ToDecimal()).IsEqualTo(0.3m);
    }

    [Test]
    public async Task ExactNumber_SubtractEdges() {
        var max = ExactNumber.TryFrom(long.MaxValue)!.Value;
        var min = ExactNumber.TryFrom(long.MinValue)!.Value;
        var difference = max.TrySubtract(min);
        var overflow = ExactNumber.TryFrom(decimal.MaxValue)!.Value.TrySubtract(ExactNumber.TryFrom(-1m)!.Value);
        var tenth = ExactNumber.TryFrom(0.3m)!.Value.TrySubtract(ExactNumber.TryFrom(0.2m)!.Value);

        await Assert.That(difference).IsNotNull();
        await Assert.That(difference!.Value.ToDecimal()).IsEqualTo(18446744073709551615m);
        await Assert.That(overflow).IsNull();
        await Assert.That(tenth).IsNotNull();
        await Assert.That(tenth!.Value.ToDecimal()).IsEqualTo(0.1m);
    }

    [Test]
    public async Task ExactNumber_Negate_DecimalMinValue_IsExact() {
        var negated = ExactNumber.TryFrom(decimal.MinValue)!.Value.TryNegate();

        await Assert.That(negated).IsNotNull();
        await Assert.That(negated!.Value.ToDecimal()).IsEqualTo(decimal.MaxValue);
    }

    [Test]
    public async Task ExactNumber_TryToLong_InAndOutOfRange() {
        var inRange = ExactNumber.TryFrom(9223372036854775807L)!.Value.TryToLong(out var max);
        var fractional = ExactNumber.TryFrom(1.5m)!.Value.TryToLong(out var fraction);
        var tooLarge = ExactNumber.TryFrom(decimal.MaxValue)!.Value.TryToLong(out var huge);

        await Assert.That(inRange).IsTrue();
        await Assert.That(max).IsEqualTo(long.MaxValue);
        await Assert.That(fractional).IsFalse();
        await Assert.That(fraction).IsEqualTo(0L);
        await Assert.That(tooLarge).IsFalse();
        await Assert.That(huge).IsEqualTo(0L);
    }

    [Test]
    public async Task ExactNumber_TryParseDecimalText_PinsTheText() {
        var twentyNineNines = ExactNumber.TryParseDecimalText("9.9999999999999999999999999999");
        var twentyEightDigits = ExactNumber.TryParseDecimalText("0.9999999999999999999999999999");
        var max = ExactNumber.TryParseDecimalText("79228162514264337593543950335");
        var oneFifty = ExactNumber.TryParseDecimalText("1.50");
        var zero = ExactNumber.TryParseDecimalText("0.0");
        var leading = ExactNumber.TryParseDecimalText("007.5");

        // 29 significant digits at scale 28 needs a 29-digit mantissa above 96 bits: positioned error.
        await Assert.That(twentyNineNines).IsNull();
        await Assert.That(twentyEightDigits).IsNotNull();
        await Assert.That(twentyEightDigits!.Value.ToDecimal()).IsEqualTo(0.9999999999999999999999999999m);
        await Assert.That(max!.Value.ToDecimal()).IsEqualTo(decimal.MaxValue);
        await Assert.That(oneFifty!.Value.ToDecimal()).IsEqualTo(1.50m);
        await Assert.That((decimal.GetBits(oneFifty.Value.ToDecimal())[3] >> 16) & 0xFF).IsEqualTo(2);
        await Assert.That(zero!.Value.ToDecimal()).IsEqualTo(0.0m);
        await Assert.That((decimal.GetBits(zero.Value.ToDecimal())[3] >> 16) & 0xFF).IsEqualTo(1);
        await Assert.That(leading!.Value.ToDecimal()).IsEqualTo(7.5m);
    }

    [Test]
    [Arguments("Int")]
    [Arguments("Int64")]
    public async Task IntegralAliasName_BoundParsesLong(string typeName) {
        // The DSL has no primitive alias for Int/Int64, so a value type named either is the only way
        // to reach the parser's integral-name arm; the bound type is what this pins.
        var poly = "domain Rb\n"
            + typeName + ": value { V: Number }\n"
            + "B: entity { X: " + typeName + " range(0, 9223372036854775806) }\n";
        var range = new PolyDslParser(poly).Parse()
            .OfType<AddConstraintToPropertyChange>()
            .Select(c => c.Constraint)
            .OfType<RangeConstraint>()
            .Single();

        await Assert.That(range.Maximum).IsTypeOf<long>();
        await Assert.That((long)range.Maximum!).IsEqualTo(9223372036854775806L);
    }

    [Test]
    public async Task NumberBound_NegativeOverflow_IsPositionedParseError() {
        var poly = Entity("Number range(-9223372036854775809, 0)");
        var error = ParseError(poly);

        await Assert.That(error.Column).IsEqualTo(ColumnOf(poly, "9223372036854775809"));
        await Assert.That(error.Detail).Contains("out of range for Number (64-bit)");
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
        var result = ApplySession(poly, out var session);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message)));
        return (result.Root!, result.Analysis, session);
    }

    private static EvolutionResult ApplySession(string poly, out DomainSession session) {
        session = DomainSession.ForSource(poly, [.. ExtensionCatalog.ProductAuthoring, "persistence"]);
        var changes = new PolyDslParser(poly, session).Parse();
        return new DomainEvolution(DomainTestFactory.Create("_", [], []))
            .ApplyAndAnalyze(changes, session: session);
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

    private static int ColumnOf(string text, string token) {
        var at = text.IndexOf(token, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException($"token not found: {token}");
        return at - text.LastIndexOf('\n', at);
    }

    private static int LineOf(string text, string token) {
        var at = text.IndexOf(token, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException($"token not found: {token}");
        return text.Take(at).Count(c => c == '\n') + 1;
    }

    private static (bool Succeeded, string? Message, object? Instance) TryCreate(
        Assembly assembly, string typeName, params (string Name, object? Value)[] named) {
        var type = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"missing type {typeName}");
        var create = type.GetMethods(PrintedMembers.Any)
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
        var succeeded = (bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!;
        var message = result.GetType().GetProperty("ErrorMessage", PrintedMembers.Any)!.GetValue(result) as string;
        var instance = succeeded
            ? result.GetType().GetProperty("Value", PrintedMembers.Any)!.GetValue(result)
            : null;
        return (succeeded, message, instance);
    }

    private static (bool Succeeded, string? Message) TryInvoke(
        object instance, string name, params (string Name, object? Value)[] named) {
        var method = instance.GetType().GetMethod(name, PrintedMembers.Any)
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
        var succeeded = (bool)result.GetType().GetProperty("IsSuccess", PrintedMembers.Any)!.GetValue(result)!;
        var message = result.GetType().GetProperty("ErrorMessage", PrintedMembers.Any)!.GetValue(result) as string;
        return (succeeded, message);
    }
}