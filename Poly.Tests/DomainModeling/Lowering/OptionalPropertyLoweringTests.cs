using Poly.Ast.Nodes;
using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>
/// Printed and simulated C# agree on requiredness: a bare property is non-nullable,
/// required, and a required ctor parameter; an <c>optional</c> property is nullable,
/// omittable, and stores null when omitted.
/// </summary>
public class OptionalPropertyLoweringTests {
    private static (Domain Domain, AnalysisResult Analysis) ParseAndAnalyze(string poly) {
        var changes = new PolyDslParser(poly).Parse();
        var result = new DomainEvolution(DomainTestFactory.Create("_", [], [])).Apply(changes);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ",
                result.Analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        var analysis = DomainModelAnalyzer.Analyze(result.Root!);
        if (analysis.HasErrors)
            throw new InvalidOperationException(string.Join("; ",
                analysis.Diagnostics.Where(d => d.Severity == Poly.Analysis.DiagnosticSeverity.Error)
                    .Select(d => d.Message)));
        return (result.Root!, analysis);
    }

    private static TypeDefinitionNode EntityType(Domain domain, AnalysisResult analysis, string name) =>
        new DomainToCSharpExporter().Export(domain, analysis).Single(t => t.Name == name);

    private static PropertyDefinitionNode Property(TypeDefinitionNode type, string name) =>
        type.Properties!.Single(p => p.Name == name);

    private static Parameter CtorParameter(TypeDefinitionNode type, string name) =>
        type.Constructors!.SelectMany(c => c.Parameters ?? []).Single(p => p.Name == name);

    private static DomainEntityInstance Create(Domain domain, Entity entity, params (string Name, object? Value)[] values) {
        var dict = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return DomainEntityInstance.Create(entity, dict, domain: domain);
    }

    private static Entity EntityOf(Domain domain, string name) =>
        domain.Types.OfType<Entity>().Single(e => e.Name == name);

    [Test]
    public async Task Export_BareTextProperty_IsNonNullableAndRequired() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain D
            Token: entity { Name: Text }
            """);
        var token = EntityType(domain, analysis, "Token");

        var name = Property(token, "Name");
        await Assert.That(name.MemberType)
            .IsTypeOf<PrimitiveTypeReference>();
        await Assert.That(((PrimitiveTypeReference)name.MemberType!).PrimitiveId)
            .IsEqualTo(Prim.String);
        await Assert.That(name.Constraints!.Any(c => c is Constant { Value: "required" })).IsTrue();

        var nameParam = CtorParameter(token, "name");
        await Assert.That(nameParam.DefaultValue).IsNull();
    }

    [Test]
    public async Task Export_OptionalTextProperty_IsNullableAndOmittable() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain D
            Token: entity { Note: Text optional }
            """);
        var token = EntityType(domain, analysis, "Token");

        var note = Property(token, "Note");
        await Assert.That(note.MemberType).IsTypeOf<OptionalTypeReference>();
        await Assert.That(note.Constraints is null || note.Constraints.Count == 0).IsTrue();

        var noteParam = CtorParameter(token, "note");
        await Assert.That(noteParam.TypeReference).IsTypeOf<OptionalTypeReference>();
        await Assert.That((noteParam.DefaultValue as Constant)?.Value).IsNull();

        var csharp = new CSharpGenerator().Generate([token]);
        await Assert.That(csharp).Contains("string? note = null");
    }

    [Test]
    public async Task Export_OptionalNumberProperty_IsNullable() {
        var (domain, analysis) = ParseAndAnalyze("""
            domain D
            Counter: entity { Limit: Number optional }
            """);
        var counter = EntityType(domain, analysis, "Counter");

        var limit = Property(counter, "Limit");
        await Assert.That(limit.MemberType).IsTypeOf<OptionalTypeReference>();
        await Assert.That(((PrimitiveTypeReference)((OptionalTypeReference)limit.MemberType!).InnerType).PrimitiveId)
            .IsEqualTo(Prim.Int64);
    }

    [Test]
    public async Task Create_OmittedOptional_StoresNull() {
        var (domain, _) = ParseAndAnalyze("""
            domain D
            Token: entity { Note: Text optional }
            """);
        var instance = Create(domain, EntityOf(domain, "Token"));
        await Assert.That(instance.GetProperty<object?>("Note")).IsNull();
    }

    [Test]
    public async Task InvokeAction_CreateInOmittingOptional_StoresNull() {
        var (domain, _) = ParseAndAnalyze("""
            domain D
            Item: entity { Name: Text optional }
            Box: entity {
              items: many Item
              Add: action { create in items { } }
            }
            """);
        var box = DomainEntityInstance.Create(EntityOf(domain, "Box"), domain: domain);
        var store = new DomainInstanceStore();
        store.Add(box);

        var result = box.InvokeAction("Add");

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(box.CreatedChildren.Single().GetProperty<object?>("Name")).IsNull();
    }

    [Test]
    public async Task Create_OmittedOptionalNumber_StoresNull() {
        var (domain, _) = ParseAndAnalyze("""
            domain D
            Counter: entity { Limit: Number optional }
            """);
        var instance = Create(domain, EntityOf(domain, "Counter"));
        await Assert.That(instance.GetProperty<object?>("Limit")).IsNull();
    }

    [Test]
    public async Task Create_OmittedBare_Fails() {
        var (domain, _) = ParseAndAnalyze("""
            domain D
            Token: entity { Name: Text }
            """);
        var thrown = Assert.Throws<ConstraintFailureException>(() =>
            Create(domain, EntityOf(domain, "Token")));
        await Assert.That(thrown!.Message).Contains("'Name' is required.");
    }

    [Test]
    public async Task Create_OmittedOptionalWithRange_NoFailure() {
        var (domain, _) = ParseAndAnalyze("""
            domain D
            Person: entity { Age: Number optional range(0, 150) }
            """);
        var person = EntityOf(domain, "Person");

        var omitted = Create(domain, person);
        await Assert.That(omitted.GetProperty<object?>("Age")).IsNull();

        var zero = Create(domain, person, ("Age", 0L));
        await Assert.That(zero.GetProperty<object?>("Age")).IsEqualTo(0L);

        var thrown = Assert.Throws<ConstraintFailureException>(() =>
            Create(domain, person, ("Age", 151L)));
        await Assert.That(thrown!.Message).Contains("'Age' must be <= 150.");
    }
}
