using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.Interpretation;

/// <summary>
/// The "printed ⊆ simulatable" audit (async/await part 1 §Enforcement, reframed by part 2).
/// <see cref="LanguageSurfaceTests"/> inventories a <see cref="LanguageSurfaceTests.Surface"/>
/// per concrete node. This class adds the print form for every kind the law can fail on — a
/// <see cref="LanguageSurfaceTests.Surface.CompileReject"/> kind — so a kind the printer emits
/// as live code cannot ship without a VM execution.
/// </summary>
public class PrintedSubsetVmTests {
    public enum PrintForm {
        Live,
        Erased,
        Rejected,
        Name,
        Declaration,
    }

    /// <summary>
    /// One entry per <see cref="LanguageSurfaceTests.Surface.CompileReject"/> kind — the only
    /// side the law can fail on, since an Executable kind is simulatable by definition — plus
    /// samples that pin the Erased / Rejected / Declaration / Live forms the audit leans on. Part 1
    /// §Enforcement asked for one entry per concrete node; part 2 §"What this removes from part 1"
    /// narrows it to this set.
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
        [typeof(PopCount)] = PrintForm.Rejected,
        [typeof(MethodDefinitionNode)] = PrintForm.Declaration,
        [typeof(Await)] = PrintForm.Live,
    };

    [Test]
    public async Task EveryCompileRejectKind_HasPrintForm() {
        var rejected = LanguageSurfaceTests.Kinds
            .Where(kv => kv.Value == LanguageSurfaceTests.Surface.CompileReject)
            .Select(kv => kv.Key)
            .OrderBy(t => t.Name)
            .ToArray();
        var missing = rejected.Where(t => !Forms.ContainsKey(t)).Select(t => t.Name).ToArray();
        var extra = Forms.Keys.Where(t => !LanguageSurfaceTests.Kinds.ContainsKey(t))
            .Select(t => t.Name).ToArray();
        await Assert.That(missing).IsEmpty();
        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task LiveCompileReject_Fails() {
        var violations = LanguageSurfaceTests.Kinds
            .Where(kv => kv.Value == LanguageSurfaceTests.Surface.CompileReject
                && Forms.TryGetValue(kv.Key, out var form) && form == PrintForm.Live)
            .Select(kv => kv.Key.Name)
            .ToArray();
        await Assert.That(violations).IsEmpty()
            .Because("printed ⊆ simulatable: a live-printed kind must execute on the VM");
    }

    [Test]
    public async Task Await_IsLiveAndExecutable() {
        await Assert.That(LanguageSurfaceTests.Kinds[typeof(Await)])
            .IsEqualTo(LanguageSurfaceTests.Surface.Executable);
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
        await Assert.That(() => CSharpLanguage.Write(new PopCount(new Constant(7L))))
            .Throws<InvalidOperationException>()
            .WithMessageContaining(nameof(PopCount));
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