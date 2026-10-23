using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The "printed ⊆ simulatable" audit (async/await part 1 §Enforcement, reframed by part 2).
/// <see cref="NodeFacts"/> records which kinds the VM rejects on purpose. This class adds the
/// print form for every such kind, so a kind the printer emits as live code cannot ship without
/// a VM execution. <see cref="PrintForm.Expanded"/> covers the kinds whose print keeps the meaning
/// and loses the kind: their law is an execution (<see cref="ExpandedKinds_PrintReReadsToAnExecutableEquivalent"/>),
/// not a round trip.
/// </summary>
public class PrintedSubsetVmTests {
    public enum PrintForm {
        Live,
        Expanded,
        Erased,
        Rejected,
        Name,
        Declaration,
    }

    /// <summary>
    /// One entry per kind <see cref="NodeFacts"/> gives a reject reason — the only side the law
    /// can fail on, since a Vm-claimed kind is simulatable by definition — plus samples that pin
    /// the Erased / Expanded / Rejected / Declaration / Live forms the audit leans on. Part 1 §Enforcement
    /// asked for one entry per concrete node; part 2 §"What this removes from part 1" narrows it
    /// to this set.
    /// </summary>
    public static readonly Dictionary<Type, PrintForm> Forms = new() {
        [typeof(ParameterReference)] = PrintForm.Name,
        [typeof(NamedTypeReference)] = PrintForm.Name,
        [typeof(TypeReference)] = PrintForm.Name,
        [typeof(ClrTypeReference)] = PrintForm.Name,
        [typeof(PrimitiveTypeReference)] = PrintForm.Name,
        [typeof(TypeDefinitionReference)] = PrintForm.Name,
        [typeof(SuspendNode)] = PrintForm.Erased,
        [typeof(StridedSetBits)] = PrintForm.Rejected,
        [typeof(PopCount)] = PrintForm.Expanded,
        [typeof(MethodDefinitionNode)] = PrintForm.Declaration,
        [typeof(Await)] = PrintForm.Live,
    };

    [Test]
    public async Task EveryCompileRejectKind_HasPrintForm() {
        var rejected = NodeFacts.All
            .Where(kv => kv.Value.RejectReason is not null)
            .Select(kv => kv.Key)
            .OrderBy(t => t.Name)
            .ToArray();
        var missing = rejected.Where(t => !Forms.ContainsKey(t)).Select(t => t.Name).ToArray();
        var extra = Forms.Keys.Where(t => !NodeFacts.All.ContainsKey(t))
            .Select(t => t.Name).ToArray();
        await Assert.That(missing).IsEmpty();
        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task LiveCompileReject_Fails() {
        var violations = NodeFacts.All
            .Where(kv => kv.Value.RejectReason is not null
                && Forms.TryGetValue(kv.Key, out var form) && form == PrintForm.Live)
            .Select(kv => kv.Key.Name)
            .ToArray();
        await Assert.That(violations).IsEmpty()
            .Because("printed ⊆ simulatable: a live-printed kind must execute on the VM");
    }

    [Test]
    public async Task Await_IsLiveAndExecutable() {
        await Assert.That(NodeFacts.All[typeof(Await)].Producers.HasFlag(Producers.Vm)).IsTrue();
        await Assert.That(NodeFacts.All[typeof(Await)].RejectReason).IsNull();
        await Assert.That(Forms[typeof(Await)]).IsEqualTo(PrintForm.Live);
        await Assert.That(CSharpLanguage.Write(new Await(new Constant(1L)))).Contains("await");
    }

    [Test]
    public async Task ErasedKinds_PrintAsAChildOrAComment() {
        await Assert.That(CSharpLanguage.Write(new SuspendNode(new Constant(7L))))
            .IsEqualTo(CSharpLanguage.Write(new Constant(7L)));
    }

    [Test]
    public async Task RejectedKinds_Throw() {
        await Assert.That(() => CSharpLanguage.Write(new StridedSetBits(
                new Constant(0L), new Constant(0L), new Constant(1L), new Constant(63L))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(StridedSetBits));
    }

    /// <summary>
    /// A sample per <see cref="PrintForm.Expanded"/> kind: a self-contained program whose print
    /// re-reads to an executable equivalent. An Expanded kind's print keeps the meaning and loses
    /// the kind, so the law is an execution, not a round-trip of the node.
    /// </summary>
    public static readonly Dictionary<Type, Node> ExpandedSamples = new() {
        [typeof(PopCount)] = new PopCount(new Constant(7L)),
    };

    [Test]
    public async Task ExpandedKinds_PrintReReadsToAnExecutableEquivalent() {
        var expanded = Forms.Where(form => form.Value == PrintForm.Expanded).Select(form => form.Key).ToArray();
        await Assert.That(ExpandedSamples.Keys.OrderBy(type => type.Name))
            .IsEquivalentTo(expanded.OrderBy(type => type.Name))
            .Because("every Expanded kind needs a sample, and a sample needs its claim");

        foreach (var (kind, sample) in ExpandedSamples) {
            // A statement form (`_ = …;`) is not readable back; the value form is a return.
            var printed = CSharpLanguage.Write(new Return(sample));
            var unit = CSharpLanguage.Read("{ " + printed + " }");
            var analysis = Interpreter.Analyze(unit);
            await Assert.That(analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                .IsEmpty().Because($"{kind.Name} prints text that must analyze cleanly: {printed}");

            using var reread = Interpreter.Execute(Interpreter.Compile(CSharpScript.ProgramBlock(unit), analysis));
            using var original = Interpreter.Execute(Interpreter.Compile(sample));
            await Assert.That(reread.Result.Value).IsEqualTo(original.Result.Value)
                .Because($"an Expanded kind's print re-reads to an executable equivalent ({kind.Name})");
        }
    }

    [Test]
    public async Task ModuleBodies_ContainNoAwaitToday() {
        // Part 2 reframes part 1's module-body rule to "a module body's Await executes or is a
        // shape reject, no allow-list". No lowering pass emits Await today, so the honest
        // assertion is that a lowered module contains none. A lowering slice that emits one must
        // replace this with a compile (executes) or a shape-reject-reason check.
        var (domain, analysis) = ParseAndAnalyze(AuditDsl);
        await Assert.That(analysis.HasErrors).IsFalse();
        var awaits = new List<Await>();
        foreach (var type in DomainProgramProjection.ToSyntax(domain, analysis))
            Collect(type, awaits);
        await Assert.That(awaits).IsEmpty()
            .Because("a lowering that emits Await must extend this audit");
    }

    private static void Collect(Node node, List<Await> awaits) {
        if (node is Await await)
            awaits.Add(await);
        foreach (var child in node.Children)
            if (child is not null)
                Collect(child, awaits);
    }

    private const string AuditDsl = """
        domain Audit
        Widget: entity {
          Score: Number default(1)
          Bump: action {
            assign Score to Score + 1
          }
        }
        """;

    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded) {
            var errors = string.Join("; ", result.Analysis.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.Message));
            throw new InvalidOperationException($"Evolution failed: {errors}");
        }
        return (result.Root!, DomainModelAnalyzer.Analyze(result.Root!));
    }
}