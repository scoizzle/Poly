using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling.Analysis;

public class DomainModelAnalyzerPipelineTests {
    [Test]
    public async Task DomainModelAnalyzerPipeline_WithValidModel_HasNoErrors() {
        var domain = DomainTestFactory.CreateDomain();
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive {
            Domain = domain,
            Name = "string",
            Category = TypeCategory.Text
        };

        domain.AddType(stringType);
        domain.AddType(entity);

        var pipeline = DomainModelAnalyzerPipeline.CreateDefault();

        var result = pipeline.Analyze(domain);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Diagnostics.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DomainModelAnalyzerPipeline_WithUnattachedRelationship_ReportsDiagnostic() {
        var domain = DomainTestFactory.CreateDomain();
        var customer = new Entity(domain, "Customer");
        var supportCase = new Entity(domain, "SupportCase");
        domain.AddType(customer);
        domain.AddType(supportCase);

        var relationship = new Relationship(domain, "CustomerCases") {
            Source = customer,
            Target = supportCase,
            Cardinality = RelationshipCardinality.OneToMany,
            SourceOwnsTarget = true
        };

        domain.AddRelationship(relationship);

        var pipeline = DomainModelAnalyzerPipeline.CreateDefault();

        var result = pipeline.Analyze(domain);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "DM0001")).IsTrue();
    }

    [Test]
    public async Task DomainModelAnalyzerPipeline_WithInvalidStageLineage_ReportsDiagnostic() {
        var domain = DomainTestFactory.CreateDomain();
        var ownerA = new Entity(domain, "OwnerA");
        var ownerB = new Entity(domain, "OwnerB");
        domain.AddType(ownerA);
        domain.AddType(ownerB);

        var bRoot = new Stage {
            Domain = domain,
            Name = "BRoot"
        };
        ownerB.AddStage(bRoot);

        var invalidStage = new Stage {
            Domain = domain,
            Name = "AChild",
            Parent = bRoot
        };
        ownerA.AddStage(invalidStage);

        var pipeline = DomainModelAnalyzerPipeline.CreateDefault();

        var result = pipeline.Analyze(domain);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "DM0002")).IsTrue();
    }
}