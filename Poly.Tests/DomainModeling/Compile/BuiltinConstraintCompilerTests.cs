using Poly.Ast.Nodes;
using Poly.DomainModeling.Compile;
using Poly.DomainModeling.Libraries.Storage;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

namespace Poly.Tests.DomainModeling.Compile;

/// <summary>
/// S10: core claims the builtin check constraints. Storage still owns
/// <c>unique</c>. <c>optional</c> and <c>default</c> are markers, not checks. The
/// constraint set is derived from the <see cref="Constraint"/> records and the
/// writer's coverage table, so a new builtin that no compiler claims fails here.
/// </summary>
public class BuiltinConstraintCompilerTests {
    [Test]
    public async Task AllBuiltinConstraints_ClaimedByCompilers() {
        var session = Session();

        foreach (var constraint in BuiltinConstraints()) {
            var matches = session.ConstraintCompilers
                .Where(c => c.Applies(constraint))
                .ToList();
            var coverage = ConstraintCheckWriter.CoverageByConstraint[constraint.GetType()];

            if (ConstraintCheckWriter.KindsFor(constraint).Count > 0) {
                await Assert.That(matches.Count).IsEqualTo(1);
                continue;
            }

            await Assert.That(coverage.Owner).IsNotNull()
                .Because($"'{constraint.GetType().Name}' has no writer kinds and no owner.");
            if (coverage.Owner!.StartsWith("marker:", StringComparison.Ordinal)) {
                await Assert.That(matches.Count).IsEqualTo(0);
                continue;
            }

            var nonCore = matches
                .Where(c => !string.Equals(c.LibraryId, CoreLibrary.CoreId, StringComparison.Ordinal))
                .ToList();
            await Assert.That(matches.Count).IsEqualTo(1);
            await Assert.That(nonCore.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task BuiltinChecks_ClaimedByCoreCompiler() {
        var session = Session();

        foreach (var constraint in BuiltinConstraints()
                     .Where(c => ConstraintCheckWriter.KindsFor(c).Count > 0)) {
            var matches = session.ConstraintCompilers
                .Where(c => c.Applies(constraint))
                .ToList();
            await Assert.That(matches.Count).IsEqualTo(1);
            await Assert.That(matches[0]).IsTypeOf<BuiltinConstraintCompiler>();
            await Assert.That(matches[0].LibraryId).IsEqualTo(CoreLibrary.CoreId);
        }
    }

    [Test]
    public async Task Unique_StaysWithStorageCompiler() {
        var session = Session();

        var matches = session.ConstraintCompilers
            .Where(c => c.Applies(new UniqueConstraint()))
            .ToList();

        await Assert.That(matches.Count).IsEqualTo(1);
        await Assert.That(matches[0]).IsTypeOf<UniqueConstraintCompiler>();
        await Assert.That(matches[0].LibraryId).IsEqualTo("persistence");
    }

    [Test]
    public async Task Compile_IsClaimOnly_FailsClosed() {
        var property = new Property("P", new DomainTypeReference("Text"), [new RequiredConstraint()]);
        var entity = new Entity("E", [property], [], [], []);
        IConstraintCompiler compiler = new BuiltinConstraintCompiler();
        var request = new ConstraintCompileRequest(
            entity,
            property,
            new RequiredConstraint(),
            new Dictionary<string, Parameter>(StringComparer.Ordinal),
            storage: null);

        await Assert.That(() => compiler.Compile(request)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task Persistence_RegistersItsOwnLoadAnalyzeLower() {
        var session = Session();

        await Assert.That(session.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Load, "persistence"))).IsEqualTo(1);
        await Assert.That(session.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Analyze, "persistence"))).IsEqualTo(1);
        await Assert.That(session.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Lower, "persistence"))).IsEqualTo(1);
        await Assert.That(session.Contributions
                .Count(c => c == new StageContribution(PipelineStage.Lower, CoreLibrary.CoreId))).IsEqualTo(1);
    }

    private static DomainSession Session() =>
        SessionBuilder.CreateEmpty().LoadCore().Load(new PersistenceEmitLibrary()).Build();

    /// <summary>Every concrete constraint record in the assembly, in stable order.</summary>
    private static IEnumerable<Constraint> BuiltinConstraints() =>
        typeof(Constraint).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(Constraint)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(Sample);

    private static Constraint Sample(Type type) {
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => SampleValue(parameter.ParameterType))
            .ToArray();
        return (Constraint)constructor.Invoke(arguments);
    }

    private static object SampleValue(Type type) =>
        type == typeof(int) ? 1
        : type == typeof(long) ? 1L
        : type == typeof(double) ? 1.0
        : type == typeof(decimal) ? 1m
        : type == typeof(bool) ? true
        : type == typeof(string) ? "x"
        : type == typeof(object) ? "x"
        : type == typeof(DomainExpression) ? DomainExpression.Literal("x")
        : throw new InvalidOperationException(
            $"Add a sample value for constraint parameter type '{type.Name}'.");
}