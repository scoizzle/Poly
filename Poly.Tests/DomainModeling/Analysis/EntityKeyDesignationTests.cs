using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Language;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;
using Poly.DomainModeling.Queries;
using Poly.Interpretation.CSharp;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// #405 slice 2: a key designation is a compile-time fact on <see cref="Entity.KeyProperties"/>,
/// surfaced on <see cref="EntityStructureMetadata"/> with <c>Id</c> as an alias. Every domain here is
/// built through the AST/API — there is no <c>key(...)</c> surface in this slice. Names are scoped to
/// their entity; a malformed designation fails closed with a named diagnostic.
/// </summary>
public sealed class EntityKeyDesignationTests {
    private static Property Text(string name, params Constraint[] constraints) =>
        new(name, new DomainTypeReference("Text"), constraints);

    private static Relationship Nav(
        string name, string source, string target,
        RelationshipCardinality cardinality = RelationshipCardinality.ManyToOne) =>
        new(name, new DomainTypeReference(source), new DomainTypeReference(target), cardinality, []);

    private static Entity Keyed(string name, string[] key, params Property[] properties) =>
        new Entity(name, properties, [], [], []) { KeyProperties = key };

    private static AnalysisResult Analyze(params DomainType[] types) =>
        DomainModelAnalyzer.Analyze(ValidDomain.Create("Test", types));

    private static AnalysisResult Analyze(IReadOnlyList<DomainType> types, IReadOnlyList<Relationship> navs) =>
        DomainModelAnalyzer.Analyze(ValidDomain.Create("Test", types, navs));

    private static IReadOnlyList<Poly.Analysis.Diagnostic> Errors(AnalysisResult analysis, string code) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Code == code)
            .ToList();

    private static EntityStructureMetadata Structure(AnalysisResult analysis, Domain domain, string entity) =>
        analysis.GetMetadata<EntityStructureMetadata>(domain.Types.OfType<Entity>().Single(e => e.Name == entity))!;

    [Test]
    public async Task KeyDesignation_IsPublishedOnMetadata_AndIdIsAnAliasOfIt() {
        var domain = ValidDomain.Create("Test", [Keyed("Order", ["Number", "Region"], Text("Number"), Text("Region"))]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        var structure = Structure(analysis, domain, "Order");
        await Assert.That(structure.KeyProperties).IsEquivalentTo(new[] { "Number", "Region" });
        await Assert.That(structure.Id).IsEquivalentTo(new[] { "Number", "Region" });
        await Assert.That(structure.HasKey).IsTrue();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task UnkeyedEntity_HasNoIdentity() {
        var domain = ValidDomain.Create("Test", [new Entity("Note", [Text("Body")], [], [], [])]);
        var structure = Structure(DomainModelAnalyzer.Analyze(domain), domain, "Note");

        await Assert.That(structure.HasKey).IsFalse();
        await Assert.That(structure.Id.Count).IsEqualTo(0);
    }

    [Test]
    public async Task KeyedEntity_PrintsAndExportsByteIdenticallyToUnkeyedTwin() {
        var keyed = ValidDomain.Create("Test", [Keyed("Order", ["Number"], Text("Number"))]);
        var unkeyed = ValidDomain.Create("Test", [new Entity("Order", [Text("Number")], [], [], [])]);

        await Assert.That(new DomainDslPrinter().Print(keyed)).IsEqualTo(new DomainDslPrinter().Print(unkeyed));
        await Assert.That(JsonLdExporter.Export(keyed)).IsEqualTo(JsonLdExporter.Export(unkeyed));

        var keyedAnalysis = DomainModelAnalyzer.Analyze(keyed);
        var unkeyedAnalysis = DomainModelAnalyzer.Analyze(unkeyed);
        await Assert.That(keyedAnalysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        var keyedCs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(keyed, keyedAnalysis));
        var unkeyedCs = new CSharpGenerator().Generate(DomainProgramProjection.ToSyntax(unkeyed, unkeyedAnalysis));
        await Assert.That(keyedCs).IsEqualTo(unkeyedCs);
    }

    [Test]
    public async Task SameKeyName_OnTwoEntities_IsLegal() {
        var domain = ValidDomain.Create("Test", [
            Keyed("Order", ["Code"], Text("Code")),
            Keyed("Invoice", ["Code"], Text("Code")),
        ]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate)).IsEmpty();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
        await Assert.That(Structure(analysis, domain, "Order").Id).IsEquivalentTo(new[] { "Code" });
        await Assert.That(Structure(analysis, domain, "Invoice").Id).IsEquivalentTo(new[] { "Code" });
    }

    [Test]
    public async Task DuplicateKeyMember_WithinOneEntity_IsStructuralDuplicate() {
        var analysis = Analyze(Keyed("Order", ["Code", "Code"], Text("Code")));

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralDuplicate);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Code").And.Contains("Order");
    }

    [Test]
    public async Task UnknownKeyMember_IsInvalidKey() {
        var analysis = Analyze(Keyed("Order", ["Missing"], Text("Code")));

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Missing").And.Contains("Order");
    }

    [Test]
    public async Task BlankKeyMember_IsInvalidKey() {
        var analysis = Analyze(Keyed("Order", [" "], Text("Code")));

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation).Count).IsEqualTo(1);
    }

    [Test]
    public async Task OptionalKeyProperty_IsInvalidKey() {
        var analysis = Analyze(Keyed("Order", ["Code"], Text("Code", new OptionalConstraint())));

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("optional");
    }

    [Test]
    public async Task KeyProperty_AssignedByAnAction_IsInvalidKey() {
        var rename = new Poly.DomainModeling.Ontology.Action("Rename", InvocationResult.Void,
            [Text("code")],
            [new AssignEffect(DomainExpression.Property("Code"), DomainExpression.Parameter("code"))],
            []);
        var order = new Entity("Order", [Text("Code")], [rename], [], []) { KeyProperties = ["Code"] };

        var errors = Errors(Analyze(order), DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("immutable");
    }

    [Test]
    public async Task CollectionNavigationKey_IsInvalidKey() {
        var analysis = Analyze(
            [Keyed("Customer", ["Code"], Text("Code")), Keyed("Order", ["Lines"], Text("Number"))],
            [Nav("Lines", "Order", "Customer", RelationshipCardinality.OneToMany)]);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("singular");
    }

    [Test]
    public async Task SingularNavigationKey_ToKeyedTarget_IsLegal() {
        var domain = ValidDomain.Create("Test",
            [Keyed("Customer", ["Code"], Text("Code")), Keyed("Order", ["Customer", "Number"], Text("Number"))],
            [Nav("Customer", "Order", "Customer")]);
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation)).IsEmpty();
        await Assert.That(Structure(analysis, domain, "Order").Id).IsEquivalentTo(new[] { "Customer", "Number" });
    }

    [Test]
    public async Task NavigationKey_ToUnkeyedTarget_IsInvalidKey() {
        var analysis = Analyze(
            [new Entity("Customer", [Text("Code")], [], [], []), Keyed("Order", ["Customer"], Text("Number"))],
            [Nav("Customer", "Order", "Customer")]);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("Customer").And.Contains("no key");
    }

    [Test]
    public async Task NavigationKeyCycle_IsStructuralCycle() {
        var analysis = Analyze(
            [Keyed("A", ["b"], Text("x")), Keyed("B", ["a"], Text("y"))],
            [Nav("b", "A", "B"), Nav("a", "B", "A")]);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralCycle).Count).IsEqualTo(2);
    }

    [Test]
    public async Task SelfNavigationKey_IsStructuralCycle() {
        var analysis = Analyze(
            [Keyed("A", ["parent"], Text("x"))],
            [Nav("parent", "A", "A")]);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralCycle).Count).IsEqualTo(1);
    }

    [Test]
    public async Task ThreeEntityNavigationKeyCycle_IsStructuralCycleOnEachMember() {
        var analysis = Analyze(
            [Keyed("A", ["b"], Text("x")), Keyed("B", ["c"], Text("y")), Keyed("C", ["a"], Text("z"))],
            [Nav("b", "A", "B"), Nav("c", "B", "C"), Nav("a", "C", "A")]);

        var errors = Errors(analysis, DomainModelDiagnosticCodes.StructuralCycle);
        await Assert.That(errors.Count).IsEqualTo(3);
    }

    [Test]
    public async Task TwoHopKeyChain_EndingInScalarKey_IsNotACycle() {
        var analysis = Analyze(
            [Keyed("A", ["b"], Text("x")), Keyed("B", ["c"], Text("y")), Keyed("C", ["Code"], Text("Code"))],
            [Nav("b", "A", "B"), Nav("c", "B", "C")]);

        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.StructuralCycle)).IsEmpty();
        await Assert.That(analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task KeyProperty_AssignedByFirstStageEntryEffect_IsInvalidKey() {
        var start = new Stage("Start", [], [],
            [new AssignEffect(DomainExpression.Property("Code"), DomainExpression.Literal("X"))], []);
        var order = new Entity("Order", [Text("Code")], [], [], []) { KeyProperties = ["Code"], Stages = [start] };

        var errors = Errors(Analyze(order), DomainModelDiagnosticCodes.InvalidKeyDesignation);
        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).Contains("immutable");
    }

    [Test]
    public async Task KeyProperty_WithCreateTimeDefault_IsLegal() {
        var order = Keyed("Order", ["Code"],
            Text("Code", new DefaultValueConstraint(DomainExpression.Literal("X"))));

        var analysis = Analyze(order);
        await Assert.That(Errors(analysis, DomainModelDiagnosticCodes.InvalidKeyDesignation)).IsEmpty();
    }
}