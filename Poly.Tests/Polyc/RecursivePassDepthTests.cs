using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Parsing;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Polyc;

/// <summary>
/// G-10 / OQ-4 and lock amendment 13: the ruled 256 nesting levels have to survive every pass that walks an expression
/// tree, on the 1 MB stack a host or worker gives a thread. A stack overflow cannot be caught, so the measurement runs
/// in a child process; the probe is payload only in the parent and acts when the child runner sets
/// <c>POLY_CHILD_PROBE</c>.
///
/// <para>There is no pass registry in the product, so the passes driven at 256 are a fixed list, each named by its
/// facade: parse = <c>DslExpressionFragment.ParseOnThisThread</c>; analysis = <c>DomainSession.Analyze</c>; lowering =
/// <c>DomainExpressionLoweringPass.Lower</c>; DSL print = <c>DomainDslPrinter.Print</c>. The VM legs
/// (<c>Interpreter.Compile</c>, <c>PolyCompiler.Produce</c>, policy evaluation) are a NAMED KNOWN GAP, see
/// <see cref="VmCompile_LongAndChain_IsPinnedAt16Operands_Issue823"/>.</para>
/// </summary>
[NotInParallel("ChildProcessSlots")]
public class RecursivePassDepthTests {
    private const int Limit = DslLimits.MaxExpressionDepth;

    private const int SmallStackBytes = 1024 * 1024;

    private const int LargeStackBytes = 16 * 1024 * 1024;

    /// <summary>A subject whose one property the policy reads.</summary>
    public sealed record FlagSubject(bool Flag);

    /// <summary>One policy per case: the longest legal <c>and</c> chain (257 operands, 256 levels) and 256 nested <c>not</c>s.</summary>
    private static IEnumerable<(string Name, string Body)> Cases() => [
        ("and-chain-257-operands", string.Join(" and ", Enumerable.Repeat("Flag", Limit + 1))),
        ("not-nested-256", ParserLimitsTests.Nots(Limit)),
    ];

    private static string Document(string body) =>
        "domain Deep\n\nA: entity {\n  Flag: Boolean\n  Ok: policy { " + body + " }\n}\n";

    private static (PolyCompiler Compiler, CompileResult Compiled) Analyzed(string body) {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds));
        var compiled = compiler.Analyze(Document(body));
        if (!compiled.Succeeded || compiled.Domain is null || compiled.Session is null)
            throw new InvalidOperationException("The document did not analyze: " + string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        return (compiler, compiled);
    }

    private static T OnStack<T>(int stackBytes, Func<T> body) {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                result = body();
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, stackBytes);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException($"A pass failed: {failure.Message}", failure);
        return result!;
    }

    private sealed record PassOutcome(string Printed, bool Lowered, bool Parsed);

    /// <summary>Parse, analysis, lowering and print of one 256-level policy, each on a thread of <paramref name="stackBytes"/>.</summary>
    private static PassOutcome RunPasses(string body, int stackBytes) {
        var (_, compiled) = Analyzed(body);
        return OnStack(stackBytes, () => {
            var parsed = DslExpressionFragment.ParseOnThisThread(body);
            var analysis = compiled.Session!.Analyze(compiled.Domain!);
            if (analysis.HasErrors)
                throw new InvalidOperationException("analysis: " + string.Join("; ", analysis.Diagnostics.Select(d => d.Message)));

            var entityParam = new Parameter("entity", TypeReference.To<FlagSubject>());
            var pass = new DomainExpressionLoweringPass(LoweringFixtures.MinimalContext(entityParam));
            var lowered = pass.Lower(parsed, entityParam);
            var printed = new DomainDslPrinter(compiled.Session).Print(compiled.Domain!);
            return new PassOutcome(printed, lowered is not null, parsed is not null);
        });
    }

    [Test]
    public async Task ChildProbe_EveryPass_Survives256_OnA1MbStack() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        foreach (var (_, body) in Cases()) {
            var outcome = RunPasses(body, SmallStackBytes);
            await Assert.That(outcome.Parsed).IsTrue();
            await Assert.That(outcome.Lowered).IsTrue();
        }
    }

    [Test]
    public async Task EveryPass_Survives256_OnA1MbStack() {
        var result = await PolycProcess.RunTestInChildAsync(
            $"/*/*/{nameof(RecursivePassDepthTests)}/{nameof(ChildProbe_EveryPass_Survives256_OnA1MbStack)}",
            new Dictionary<string, string?> { ["POLY_CHILD_PROBE"] = "g10-passes" });

        await Assert.That(result.IsAborted).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    /// <summary>The same passes on a large stack, asserting what comes out: depth 256 reaches every pass and the print keeps every level.</summary>
    [Test]
    public async Task AtLimit_FlowsThroughAllPasses() {
        foreach (var (name, body) in Cases()) {
            var outcome = RunPasses(body, LargeStackBytes);

            await Assert.That(outcome.Lowered).IsTrue();
            await Assert.That(outcome.Printed).Contains("Ok: policy");
            if (name == "not-nested-256")
                await Assert.That(outcome.Printed.Split("not ").Length - 1).IsEqualTo(Limit);
            else
                await Assert.That(outcome.Printed.Split(" and ").Length - 1).IsEqualTo(Limit);
        }
    }

    /// <summary>
    /// NAMED KNOWN GAP (lock amendment 13): <c>Interpreter.Compile</c> is exponential in the length of a left-nested
    /// <c>and</c> chain on master (about 2x per operand), so the VM legs, <c>PolyCompiler.Produce</c> and policy
    /// evaluation, are only shown at 16 operands, where they compile and evaluate correctly. Tracked in issue #823,
    /// row X-12, which extends this to 256 operands and deletes this pin.
    /// </summary>
    [Test]
    public async Task VmCompile_LongAndChain_IsPinnedAt16Operands_Issue823() {
        var body = string.Join(" and ", Enumerable.Repeat("Flag", 16));
        var (_, compiled) = Analyzed(body);

        var outcome = OnStack(LargeStackBytes, () => {
            var analysis = compiled.Session!.Analyze(compiled.Domain!);
            var produced = PolyCompiler.Produce(compiled.Session, compiled.Domain!, analysis);
            var policy = compiled.Domain!.Types.OfType<Entity>().Single().Policies.Single();
            return (produced.Succeeded, produced.Files.Count, policy.Evaluate(new FlagSubject(true)), policy.Evaluate(new FlagSubject(false)));
        });

        await Assert.That(outcome.Succeeded).IsTrue();
        await Assert.That(outcome.Count).IsGreaterThan(0);
        await Assert.That(outcome.Item3).IsTrue();
        await Assert.That(outcome.Item4).IsFalse();
    }
}