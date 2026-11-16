namespace Poly.Tests.DomainModeling.Lowering;

/// <summary>P-11b (#687): the walk that gives an action route its loads, entered at a create
/// (<see cref="Poly.Packs.Product.MinimalApiGenerator.ReadsOfCreate"/>): one root per Required link.</summary>
public partial class MinimalApiGeneratorTests {
    [Test]
    public async Task CreateReads_ARequiredLink_IsARootOfTheCreateTree() {
        var compile = CompileForHost("""
            domain Chain
            Region: entity { Code: Text unique  Name: Text }
            Course: entity {
              Code: Text unique
              region: owned Region optional
            }
            Section: entity {
              Code: Text unique
              Label: Text optional
              course: Course
              Open: stage { entry { assign Label to course region Name } }
            }
            Lecture: entity {
              Code: Text unique
              course: Course
            }
            """);
        var generator = HostGenerator(compile);
        var entities = compile.Domain.Types.OfType<Poly.DomainModeling.Ontology.Entity>().ToDictionary(e => e.Name);

        await Assert.That(generator.ReadsOfCreate(entities["Section"]).Render()).IsEqualTo("this{}; course{Region}");
        await Assert.That(generator.ReadsOfCreate(entities["Lecture"]).Render()).IsEqualTo("this{}; course{}");
        await Assert.That(generator.ReadsOfCreate(entities["Course"]).Render()).IsEqualTo("this{}");
    }

    [Test]
    public async Task CreateReads_ATargetWithACollectionOfTheChild_LoadsItBecauseCreateAttachesToIt() {
        // The child of a collection is an aggregate child, so it has no POST route today; the walk is the same one a route
        // for it would preload from.
        var compile = CompileForHost("""
            domain Lib
            Patron: entity {
              Email: Text unique
              loans: many Loan via borrower
            }
            Loan: entity {
              Code: Text unique
              borrower: Patron
            }
            """);
        var entities = compile.Domain.Types.OfType<Poly.DomainModeling.Ontology.Entity>().ToDictionary(e => e.Name);

        await Assert.That(HostGenerator(compile).ReadsOfCreate(entities["Loan"]).Render()).IsEqualTo("this{}; borrower{Loans[loaded]}");
        await Assert.That(HostGenerator(compile).Generate("Ctx")).DoesNotContain("MapPost(\"/api/loans\"");
    }

    /// <summary>A link name that matches no entity parameter of the lowered <c>Create</c> would load nothing silently:
    /// the walk refuses it instead.</summary>
    [Test]
    public async Task ReadsOfCreate_ALinkNameMatchingNoParameter_IsRefused() {
        var compile = CompileForHost(SectionPoly);
        var section = compile.Domain.Types.OfType<Poly.DomainModeling.Ontology.Entity>().Single(e => e.Name == "Section");

        var refusal = Assert.Throws<Poly.Packs.Product.ActionReadsRefusedException>(
            () => HostGenerator(compile).ReadsOfCreate(section, new HashSet<string> { "course", "nope" }));

        await Assert.That(refusal.Message).Contains("Section.Create");
        await Assert.That(refusal.Message).Contains("'nope'");
        await Assert.That(refusal.Message).DoesNotContain("'course'");
    }

    /// <summary>A refused create fails host generation, naming <c>Section.Create</c>: the host never serves a create with
    /// less than it reads.</summary>
    [Test]
    public async Task CreateRoute_ARefusedCreate_FailsHostGenerationNamingTheCreate() {
        var compile = CompileForHost(SectionPoly);
        var body = ReturnsSuccessAfter(new Poly.Ast.Nodes.Invoke(
            new Poly.Ast.Nodes.Member(new Poly.Ast.Nodes.ThisReference(), "LinkRelated"),
            new Poly.Ast.Nodes.Constant("course"), new Poly.Ast.Nodes.Constant(null!)));

        var refusal = Assert.Throws<Poly.Packs.Product.ActionReadsRefusedException>(
            () => WithBody(compile, "Section", "__BodyOfCreate", body).Generate("Ctx"));

        await Assert.That(refusal.Message).Contains("Section.Create");
        await Assert.That(refusal.Message).Contains("cannot be served over HTTP");
    }
}
