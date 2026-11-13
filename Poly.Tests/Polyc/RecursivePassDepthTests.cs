using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Parsing;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Polyc;

/// <summary>
/// G-10 / OQ-4: the ruled 256 nesting levels have to survive every pass that walks an expression tree, on the
/// 1 MB stack a host or worker gives a thread. A stack overflow cannot be caught, so the measurement runs in a
/// child process; the probe is payload only in the parent and acts when the child runner sets
/// <c>POLY_CHILD_PROBE</c>. If a pass overflows, the finding goes back to the owner: the limit is never silently lowered.
///
/// <para>There is no pass registry in the product, so the passes driven here are a fixed list, each named by its facade:
/// analysis = <c>DomainSession.Analyze</c>; lowering and the C# export = <c>PolyCompiler.Produce</c>; DSL print =
/// <c>DomainDslPrinter.Print</c>; simulate = the policy lowered to the VM and executed (<c>PolicyEvaluator.Evaluate</c>).</para>
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

    private sealed record PassOutcome(string Printed, bool Simulated, int ExportedFiles);

    /// <summary>Parse and analyze however the compiler likes, then run every pass on a thread of <paramref name="stackBytes"/>.</summary>
    private static PassOutcome RunEveryPass(string body, int stackBytes) {
        var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds));
        var compiled = compiler.Analyze(Document(body));
        if (!compiled.Succeeded || compiled.Domain is null || compiled.Session is null)
            throw new InvalidOperationException("The 256-level document did not analyze: " + string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));

        PassOutcome? outcome = null;
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var analysis = compiled.Session.Analyze(compiled.Domain);
                if (analysis.HasErrors)
                    throw new InvalidOperationException("analysis: " + string.Join("; ", analysis.Diagnostics.Select(d => d.Message)));

                var produced = PolyCompiler.Produce(compiled.Session, compiled.Domain, analysis);
                if (!produced.Succeeded)
                    throw new InvalidOperationException("lower/export: " + string.Join("; ", produced.Diagnostics.Where(d => d.IsError).Select(d => d.Message)));

                var printed = new DomainDslPrinter(compiled.Session).Print(compiled.Domain);
                var policy = compiled.Domain.Types.OfType<Entity>().Single().Policies.Single();
                var simulated = policy.Evaluate(new FlagSubject(true));
                outcome = new PassOutcome(printed, simulated, produced.Files.Count);
            }
            catch (Exception ex) {
                failure = ex;
            }
        }, stackBytes);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException($"A pass failed at depth {Limit}: {failure.Message}", failure);
        return outcome!;
    }

    [Test]
    public async Task ChildProbe_EveryPass_Survives256_OnA1MbStack() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        foreach (var (_, body) in Cases()) {
            var outcome = RunEveryPass(body, SmallStackBytes);
            await Assert.That(outcome.Simulated).IsTrue();
            await Assert.That(outcome.ExportedFiles).IsGreaterThan(0);
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

    /// <summary>The same passes on a large stack, asserting what comes out: depth 256 reaches every pass and the answer is right.</summary>
    [Test]
    public async Task AtLimit_FlowsThroughAllPasses() {
        foreach (var (name, body) in Cases()) {
            var outcome = RunEveryPass(body, LargeStackBytes);

            await Assert.That(outcome.Simulated).IsTrue();
            await Assert.That(outcome.ExportedFiles).IsGreaterThan(0);
            await Assert.That(outcome.Printed).Contains("Ok: policy");
            if (name == "not-nested-256")
                await Assert.That(outcome.Printed.Split("not ").Length - 1).IsEqualTo(Limit);
            else
                await Assert.That(outcome.Printed.Split(" and ").Length - 1).IsEqualTo(Limit);
        }
    }
}