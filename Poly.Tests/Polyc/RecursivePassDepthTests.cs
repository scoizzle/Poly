using Poly.Compile;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Grammar;
using Poly.Interpretation;
using Poly.Packs.Product;
using Poly.Tests.DomainModeling.Parsing;
using Poly.Tests.TestHelpers;

namespace Poly.Tests.Polyc;

/// <summary>
/// G-10 / OQ-4 and lock amendment 13: the ruled 256 nesting levels have to survive every pass that walks an expression
/// tree, on the 1 MB stack a host or worker gives a thread. A stack overflow cannot be caught, so the measurement runs
/// in a child process; the probe is payload only in the parent and acts when the child runner sets
/// <c>POLY_CHILD_PROBE</c>. Row G-25 adds the statement limit (64) and path words to the stack grid; see
/// <see cref="ChildProbe_StatementAndExpressionShapes_OnFixedStack"/>. The parse stack hop (<c>DslLimits.RunOnParseStack</c>)
/// is still on <c>PolyCompiler</c> and <c>DomainResolver.Resolve</c>, so the analyze and compile doors run on its 16 MB thread.
///
/// <para>There is no pass registry in the product, so the passes driven at 256 are a fixed list, each named by its
/// facade: parse = <c>DslExpressionFragment.ParseOnThisThread</c>; analysis = <c>DomainSession.Analyze</c>; lowering =
/// <c>DomainExpressionLoweringPass.Lower</c>; DSL print = <c>DomainDslPrinter.Print</c>. The VM legs
/// (<c>PolyCompiler.Produce</c>, policy evaluation) are
/// <see cref="VmCompile_LongAndChain_AtParserLimit_CompilesAndEvaluates"/>.</para>
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

    private const int StatementLimit = DslLimits.MaxStatementDepth;

    /// <summary>A path of <paramref name="words"/> words over a self-referential navigation, so every hop resolves.</summary>
    internal static string PathDocument(int words) =>
        "domain D\nE: entity {\n  Flag: Boolean\n  next: one E\n  Ok: policy { " + string.Concat(Enumerable.Repeat("next ", words - 1)) + "Flag }\n}\n";

    private static string ExpressionDocument(string shape, int n) {
        const string head = "domain D\nL: entity { N: Text }\nE: entity {\n  Flag: Boolean\n  Note: Text\n  Lines: many L\n";
        return shape switch {
            "not" => head + "  Ok: policy { " + string.Concat(Enumerable.Repeat("not ", n)) + "Flag }\n}\n",
            "paren" => head + "  Ok: policy { " + new string('(', n) + "Flag" + new string(')', n) + " }\n}\n",
            _ => throw new InvalidOperationException(shape),
        };
    }

    private static string CellDocument(string shape, int depth) => shape switch {
        "not" or "paren" => ExpressionDocument(shape, depth),
        "path" => PathDocument(depth),
        _ => StatementLimitsTests.StatementDocument(shape, depth),
    };

    /// <summary>
    /// What a cell must give: success up to the limit, else one DSL0001 positioned on the first token past it (the
    /// 65th statement head, or the 258th path word) with the limit's detail. Expression shapes stay at G-10's 256.
    /// </summary>
    private static string ExpectedOutcome(string shape, int depth) {
        switch (shape) {
            case "not" or "paren":
                return "ok";
            case "path": {
                    if (depth - 1 <= Limit)
                        return "ok";
                    var document = PathDocument(depth);
                    var offset = document.IndexOf("Ok: policy { ", StringComparison.Ordinal) + "Ok: policy { ".Length + 5 * (Limit + 1);
                    var (line, column) = StatementLimitsTests.Position(document, offset);
                    return $"DSL0001 {line}:{column} {DslLimits.DepthDetail}";
                }
            default: {
                    if (depth <= StatementLimit)
                        return "ok";
                    var document = StatementLimitsTests.StatementDocument(shape, depth);
                    var (line, column) = StatementLimitsTests.Position(document, StatementLimitsTests.HeadOffset(document, StatementLimit + 1));
                    return $"DSL0001 {line}:{column} {DslLimits.StatementDepthDetail}";
                }
        }
    }

    private static readonly string[] StatementShapes = ["nestif", "elseif", "forif", "combined"];

    /// <summary>
    /// The probe grid, shape x depth: G-10's expression shapes at 256; every statement shape at the statement limit and
    /// one past it (<c>combined</c> is the statement limit with G-10's 256 nested <c>not</c>s in the innermost
    /// condition); paths at 256, 257 and 258 words; then the far cells, deep past both limits, which a reader without a
    /// counted limit overflows. Far cells come last so a child that aborts still records everything before them.
    /// </summary>
    private static IEnumerable<(string Shape, int Depth)> StackCells() {
        yield return ("not", Limit);
        yield return ("paren", Limit);
        foreach (var shape in StatementShapes) {
            yield return (shape, StatementLimit);
            yield return (shape, StatementLimit + 1);
        }
        foreach (var words in new[] { Limit, Limit + 1, Limit + 2 })
            yield return ("path", words);
        yield return ("path", 100_000);
        foreach (var shape in new[] { "nestif", "elseif", "forif" })
            yield return (shape, 20_000);
    }

    private static readonly string[] Doors = ["parse", "analyze", "compile", "print"];

    /// <summary>Expression shapes keep G-10's three doors; the print door is new with G-25.</summary>
    private static IEnumerable<string> DoorsOf(string shape) => shape is "not" or "paren" ? Doors[..3] : Doors;

    private static string Outcome(CompileResult result) {
        var errors = result.Diagnostics.Where(d => d.IsError).ToList();
        return errors.Count == 1
            ? $"{errors[0].Code} {errors[0].Line}:{errors[0].Column} {errors[0].Message}"
            : "errors: " + string.Join(" | ", errors.Select(e => e.Code + " " + e.Message));
    }

    /// <summary>One door on one document, on the calling thread: "ok", or the one error it reported.</summary>
    private static string RunDoor(string door, string document) {
        try {
            if (door == "parse") {
                _ = new PolyDslParser(document).Parse();
                return "ok";
            }
            var compiler = new PolyCompiler(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds));
            var result = door == "compile" ? compiler.Compile(document) : compiler.Analyze(document);
            if (!result.Succeeded)
                return Outcome(result);
            if (door == "print")
                _ = new DomainDslPrinter(result.Session!).Print(result.Domain!);
            return "ok";
        }
        catch (GrammarException ex) {
            return $"DSL0001 {ex.Line}:{ex.Column} {ex.Detail}";
        }
    }

    private static string CellKey(string shape, int depth, string door) => $"{shape}\t{depth}\t{door}";

    /// <summary>
    /// Lock amendment 12 (G-10) and G-25's grid: every cell of <see cref="StackCells"/> through every door of
    /// <see cref="DoorsOf"/>, each on a fresh thread of a fixed stack (POLY_PROBE_STACK_KB). Each outcome is appended to
    /// POLY_PROBE_OUT as it lands, so the parent compares the whole table and an abort still shows which cell died.
    /// </summary>
    [Test]
    public async Task ChildProbe_StatementAndExpressionShapes_OnFixedStack() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") is null)
            return;

        var stackKb = int.Parse(Environment.GetEnvironmentVariable("POLY_PROBE_STACK_KB")!, System.Globalization.CultureInfo.InvariantCulture);
        var output = Environment.GetEnvironmentVariable("POLY_PROBE_OUT")!;
        foreach (var (shape, depth) in StackCells()) {
            var document = CellDocument(shape, depth);
            foreach (var door in DoorsOf(shape)) {
                var outcome = OnStack(stackKb * 1024, () => RunDoor(door, document));
                File.AppendAllText(output, CellKey(shape, depth, door) + "\t" + outcome + "\n");
            }
        }
        await Assert.That(File.Exists(output)).IsTrue();
    }

    [Test]
    [Arguments(1024)]
    [Arguments(512)]
    public async Task StatementAndExpressionShapes_SurviveFixedStacks_ThroughEveryDoor(int stackKb) {
        var output = Path.Combine(Path.GetTempPath(), $"poly-g25-grid-{Guid.NewGuid():N}.tsv");
        try {
            var result = await PolycProcess.RunTestInChildAsync(
                $"/*/*/{nameof(RecursivePassDepthTests)}/{nameof(ChildProbe_StatementAndExpressionShapes_OnFixedStack)}",
                new Dictionary<string, string?> {
                    ["POLY_CHILD_PROBE"] = "g25-grid",
                    ["POLY_PROBE_STACK_KB"] = stackKb.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["POLY_PROBE_OUT"] = output,
                });

            var actual = File.Exists(output)
                ? File.ReadAllLines(output).Select(l => l.Split('\t')).ToDictionary(f => CellKey(f[0], int.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), f[2]), f => f[3])
                : [];
            var mismatches = new List<string>();
            foreach (var (shape, depth) in StackCells()) {
                foreach (var door in DoorsOf(shape)) {
                    var expected = ExpectedOutcome(shape, depth);
                    var key = CellKey(shape, depth, door);
                    var got = actual.TryGetValue(key, out var value) ? value : "(no outcome: the child died before this cell)";
                    if (got != expected)
                        mismatches.Add($"{key.Replace('\t', ' ')}: expected '{expected}', got '{got}'");
                }
            }

            await Assert.That(string.Join("\n", mismatches)).IsEqualTo("").Because($"{stackKb} KB, child exit {result.ExitCode}");
            await Assert.That(result.IsAborted).IsFalse();
            await Assert.That(result.ExitCode).IsEqualTo(0);
        }
        finally {
            File.Delete(output);
        }
    }

    // ── G-25b's bucket: stage chains, and the doors the hop does not cover ─────────────────────────────────────

    /// <summary>
    /// Lock amendment 2: <paramref name="stages"/> stages. <c>entrychain</c>: each stage's <c>entry</c> moves on to the
    /// next by <c>transition to</c>, so lowering inlines every entry into the one before it. <c>exitchain</c>: every stage
    /// has an <c>exit</c> block, and an entity-level action hops, which lowers to one nested arm per stage.
    /// </summary>
    internal static string StageChainDocument(string shape, int stages) {
        var text = new System.Text.StringBuilder("domain D\nE: entity {\n  Note: Text\n");
        switch (shape) {
            case "entrychain":
                for (var k = 0; k < stages; k++)
                    text.Append("  S").Append(k).Append(": stage { entry { transition to S").Append(k + 1).Append(" } Go: action { assign Note to \"x\" } }\n");
                text.Append("  S").Append(stages).Append(": stage { }\n");
                break;
            case "exitchain":
                text.Append("  Hop: action { transition to S0 }\n");
                for (var k = 0; k < stages; k++)
                    text.Append("  S").Append(k).Append(": stage { exit { assign Note to \"x\" } }\n");
                break;
            default:
                throw new InvalidOperationException(shape);
        }
        return text.Append("}\n").ToString();
    }

    private static PolyCompiler Compiler() =>
        new(ProductCatalog.Catalog, new SessionOptions(ProductCatalog.Catalog, ProductCatalog.ImplicitIds));

    /// <summary>One door of the G-25b bucket on the calling thread: "ok", the one error it reported, or the exception it threw.</summary>
    private static string RunBucketDoor(string door, string document) {
        try {
            switch (door) {
                case "analyze":
                case "compile": {
                        var result = door == "compile" ? Compiler().Compile(document) : Compiler().Analyze(document);
                        return result.Succeeded ? "ok" : Outcome(result);
                    }
                default: {
                        // Analyze goes through the hop as today; the door then runs on THIS thread, the caller's stack.
                        var result = Compiler().Analyze(document);
                        if (!result.Succeeded)
                            return "analyze: " + Outcome(result);
                        switch (door) {
                            case "produce":
                                var produced = PolyCompiler.Produce(result.Session!, result.Domain!, result.Analysis!);
                                return produced.Succeeded ? "ok" : Outcome(produced);
                            case "lower":
                                _ = result.Session!.Lower(result.Domain!, result.Analysis!);
                                return "ok";
                            case "print":
                                _ = new DomainDslPrinter(result.Session!).Print(result.Domain!);
                                return "ok";
                            default:
                                throw new InvalidOperationException(door);
                        }
                    }
            }
        }
        catch (Exception ex) {
            return $"throw {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Runs one bucket cell in the child on a fixed stack and writes its outcome to POLY_PROBE_OUT.</summary>
    [Test]
    public async Task ChildProbe_BucketCell_OnFixedStack() {
        if (Environment.GetEnvironmentVariable("POLY_CHILD_PROBE") != "g25b-bucket")
            return;

        var stackKb = int.Parse(Environment.GetEnvironmentVariable("POLY_PROBE_STACK_KB")!, System.Globalization.CultureInfo.InvariantCulture);
        var depth = int.Parse(Environment.GetEnvironmentVariable("POLY_PROBE_DEPTH")!, System.Globalization.CultureInfo.InvariantCulture);
        var shape = Environment.GetEnvironmentVariable("POLY_PROBE_SHAPE")!;
        var door = Environment.GetEnvironmentVariable("POLY_PROBE_DOOR")!;
        var document = shape == "not" ? ExpressionDocument(shape, depth) : StageChainDocument(shape, depth);
        var outcome = OnStack(stackKb * 1024, () => RunBucketDoor(door, document));
        File.WriteAllText(Environment.GetEnvironmentVariable("POLY_PROBE_OUT")!, outcome);
        await Assert.That(outcome).IsNotNull();
    }

    /// <summary>
    /// One bucket cell in its own child (an abort takes only that child): "abort &lt;exit code&gt;" when the child died
    /// of a stack overflow, else the outcome the child wrote. The child runs under the harness's 1 GB heap cap.
    /// </summary>
    private static async Task<string> BucketCell(string shape, int depth, string door, int stackKb) {
        var output = Path.Combine(Path.GetTempPath(), $"poly-g25b-{Guid.NewGuid():N}.txt");
        try {
            var result = await PolycProcess.RunTestInChildAsync(
                $"/*/*/{nameof(RecursivePassDepthTests)}/{nameof(ChildProbe_BucketCell_OnFixedStack)}",
                new Dictionary<string, string?> {
                    ["POLY_CHILD_PROBE"] = "g25b-bucket",
                    ["POLY_PROBE_STACK_KB"] = stackKb.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["POLY_PROBE_DEPTH"] = depth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["POLY_PROBE_SHAPE"] = shape,
                    ["POLY_PROBE_DOOR"] = door,
                    ["POLY_PROBE_OUT"] = output,
                });
            if (result.IsAborted)
                return result.Stderr.Contains("Stack overflow", StringComparison.Ordinal)
                    ? $"abort {result.ExitCode} (stack overflow)"
                    : $"abort {result.ExitCode}";
            return File.Exists(output) ? File.ReadAllText(output) : $"exit {result.ExitCode} with no outcome";
        }
        finally {
            File.Delete(output);
        }
    }

    /// <summary>
    /// Lock amendment 2, green through the hop: 500 and 2,000 chained stages analyze and compile. With the hop deleted
    /// (step 0, 512 KB) Compile of 500 aborts in both shapes; these cells catch a regression of today's behaviour.
    /// </summary>
    [Test]
    [Arguments("entrychain", 500)]
    [Arguments("entrychain", 2000)]
    [Arguments("exitchain", 500)]
    [Arguments("exitchain", 2000)]
    public async Task StageChain_ThroughTheHop_AnalyzesAndCompiles(string shape, int stages) {
        foreach (var door in new[] { "analyze", "compile" })
            await Assert.That(await BucketCell(shape, stages, door, 512)).IsEqualTo("ok").Because($"{shape} {stages} {door}");
    }

    /// <summary>
    /// RECORDED CURRENT RESULT, owner G-25b (not a pass): 10,000 entry-chained stages run the evolution out of the
    /// child's 1 GB heap, reported as DSL0004. Kept out of the default run ([Explicit]): the cell spends the whole
    /// 1 GB heap cap by construction. Run it by name.
    /// </summary>
    [Test]
    [Explicit]
    public async Task StageChain_10000EntryChained_RecordedCurrentResult_OwnedByG25b() =>
        await Assert.That(await BucketCell("entrychain", 10_000, "analyze", 512)).IsEqualTo(RecordedStageChain10000);

    private const string RecordedStageChain10000 = "DSL0004 1:1 Evolution failed: Exception of type 'System.OutOfMemoryException' was thrown.";

    /// <summary>
    /// RECORDED CURRENT RESULTS, owner G-25b (not passes): a policy of 256 nested <c>not</c>s (G-10's limit) analyzes
    /// through the hop, then each door runs on the caller's own stack, which the hop does not cover:
    /// <c>PolyCompiler.Produce</c> (what <c>OracleTool</c> calls), a direct <c>DomainSession.Lower</c>, and
    /// <c>DomainDslPrinter.Print</c>. Each cell is its own child, so an abort is recorded as the child's exit code.
    /// Recorded 2026-11-13: <c>Produce</c> on 512 KB aborts (stack overflow in <c>ConstantFoldingPass.FoldUnaryBoolean</c> /
    /// <c>TryFold</c> / <c>GetConstantValue</c>, under <c>DomainSession.Emit</c> → <c>VmAnalysisOf</c>); every other cell completes.
    /// </summary>
    [Test]
    [Arguments("produce", 512, "abort 134 (stack overflow)")]
    [Arguments("produce", 1024, "ok")]
    [Arguments("lower", 512, "ok")]
    [Arguments("lower", 1024, "ok")]
    [Arguments("print", 512, "ok")]
    [Arguments("print", 1024, "ok")]
    public async Task UncoveredDoor_256Nots_OnTheCallersStack_RecordedCurrentResult_OwnedByG25b(string door, int stackKb, string recorded) =>
        await Assert.That(await BucketCell("not", Limit, door, stackKb)).IsEqualTo(recorded);

    /// <summary>
    /// The VM legs (<c>PolyCompiler.Produce</c> and policy evaluation) at the parser's longest legal <c>and</c> chain:
    /// 257 operands, 256 levels. #823 (row X-12) fixed the exponential <c>Interpreter.Compile</c> that held this pin
    /// at 16 operands; G-25 raises it to the limit. It stays in-process on a 16 MB thread (lock amendment 8): it pins
    /// correctness at the limit, and small-stack VM coverage lives only in the child-process grid.
    /// </summary>
    [Test]
    public async Task VmCompile_LongAndChain_AtParserLimit_CompilesAndEvaluates() {
        var body = string.Join(" and ", Enumerable.Repeat("Flag", Limit + 1));
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