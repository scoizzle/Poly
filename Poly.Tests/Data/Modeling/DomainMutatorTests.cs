using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

namespace Poly.Tests.Data.Modeling;

public class DomainMutationTests {
    [Test]
    public async Task AddType_ValidMutation_Commits() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        _ = mutation.AddType(stringType);
        var result = mutation.Apply();

        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(domain.Types.Contains(stringType)).IsTrue();
    }

    [Test]
    public async Task AddPolicy_WhenAnalyzerFails_RollsBackMutation() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child");
        var parentOnlyProperty = new Property(domain, "ExternalValue", stringType);

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(parent);
        _ = mutation.AddType(child);
        _ = mutation.AddProperty(parent, parentOnlyProperty);

        var invalidPolicy = new Policy(domain, "MissingPropertyPolicy");
        _ = mutation.AddRule(invalidPolicy, new PropertyRule(domain, "ExternalValueRequired", parentOnlyProperty, new RequiredConstraint()));

        _ = mutation.AddPolicy(child, invalidPolicy);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("ExternalValue"));
        await Assert.That(error is not null).IsTrue();
        await Assert.That(child.Policies.Contains(invalidPolicy)).IsFalse();
    }

    [Test]
    public async Task AddProperty_CommitsThroughSharedAnalyzer() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var entity = new Entity(domain, "Ticket");
        var title = new Property(domain, "Title", stringType);

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(entity);

        _ = mutation.AddProperty(entity, title);
        var result = mutation.Apply();

        await Assert.That(result.HasErrors).IsFalse();
        await Assert.That(entity.Properties.Contains(title)).IsTrue();
    }

    [Test]
    public async Task SetRelationship_WithInvalidOwnership_ThrowsAndPreservesState() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var source = new Entity(domain, "Source");
        var target = new Entity(domain, "Target");

        _ = mutation.AddType(stringType);
        _ = mutation.AddType(source);
        _ = mutation.AddType(target);

        var relationship = new Relationship(domain, "SourceTarget", source, target, RelationshipCardinality.OneToOne, true);

        _ = mutation.AddRelationship(relationship);
        _ = mutation.AddEntityRelationship(source, relationship);

        _ = mutation.SetRelationship(relationship, source, target, RelationshipCardinality.ManyToMany, true);
        var result = mutation.Apply();
        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must be one-to-one or one-to-many"));
        await Assert.That(error is not null).IsTrue();
        await Assert.That(relationship.Cardinality).IsEqualTo(RelationshipCardinality.OneToOne);
    }

    [Test]
    public async Task Mutation_AfterApply_IsNotReusable() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();

        _ = mutation.AddType(new Primitive(domain, "string", TypeCategory.Text));
        _ = mutation.Apply();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            _ = mutation.AddType(new Primitive(domain, "int", TypeCategory.Integer));
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task AddType_WithForeignDomain_ReportsMutationInvariant_AndRollsBackCleanly() {
        var domain = new Domain("Support");
        var otherDomain = new Domain("Other");
        var foreignType = new Primitive(otherDomain, "foreign-string", TypeCategory.Text);

        var result = MutationApply.AddType(domain, foreignType);

        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.MutationInvariant)).IsTrue();
        await Assert.That(domain.Types.Any(t => t.Name == "foreign-string")).IsFalse();

        var reanalysis = new DomainModelAnalyzer().Analyze(domain);
        await Assert.That(reanalysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task AddType_WithDuplicateName_ReportsStructuralDuplicate_AndRollsBackCleanly() {
        var domain = new Domain("Support");
        var mutation = domain.CreateMutation();
        var left = new Primitive(domain, "string", TypeCategory.Text);
        var right = new Primitive(domain, "string", TypeCategory.Text);

        _ = mutation.AddType(left).AddType(right);
        var result = mutation.Apply();

        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)).IsTrue();
        await Assert.That(domain.Types.Any(t => t.Name == "string")).IsFalse();

        var reanalysis = new DomainModelAnalyzer().Analyze(domain);
        await Assert.That(reanalysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task AddRelationship_WithForeignEndpoint_ReportsMutationInvariant_AndRollsBackCleanly() {
        var domain = new Domain("Support");
        var otherDomain = new Domain("Other");

        var customer = new Entity(domain, "Customer");
        var externalCase = new Entity(otherDomain, "SupportCase");

        _ = MutationApply.AddType(domain, customer);

        var relationship = new Relationship(domain, "CustomerCases", customer, externalCase, RelationshipCardinality.OneToMany, false);
        var result = MutationApply.AddRelationship(domain, relationship);

        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.MutationInvariant)).IsTrue();
        await Assert.That(domain.Relationships.Contains(relationship)).IsFalse();

        var reanalysis = new DomainModelAnalyzer().Analyze(domain);
        await Assert.That(reanalysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task AddEventProperty_WithDuplicateName_ReportsStructuralDuplicate_AndRollsBackCleanly() {
        var domain = new Domain("Support");
        var eventType = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var mutation = domain.CreateMutation();

        _ = mutation.AddType(eventType).AddType(stringType);
        _ = mutation.AddProperty(eventType, new Property(domain, "AssignedTo", stringType));
        _ = mutation.AddProperty(eventType, new Property(domain, "AssignedTo", stringType));
        var result = mutation.Apply();

        await Assert.That(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate && d.Message.Contains("AssignedTo"))).IsTrue();
        await Assert.That(eventType.Properties.Count).IsEqualTo(0);

        var reanalysis = new DomainModelAnalyzer().Analyze(domain);
        await Assert.That(reanalysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)).IsFalse();
    }

    [Test]
    public async Task AnalyzeIncremental_WithInvalidatedEventProperty_StillReportsStructuralDuplicate() {
        var domain = new Domain("Support");
        var analyzer = new DomainModelAnalyzer();
        var eventType = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        new Domain.AddTypeCommand(domain, eventType).Apply();
        new Domain.AddTypeCommand(domain, stringType).Apply();

        var existingProperty = new Property(domain, "AssignedTo", stringType);
        new Event.AddPropertyCommand(eventType, existingProperty).Apply();

        var prior = analyzer.Analyze(domain);
        await Assert.That(prior.HasErrors).IsFalse();

        var duplicateProperty = new Property(domain, "AssignedTo", stringType);
        new Event.AddPropertyCommand(eventType, duplicateProperty).Apply();

        var incremental = analyzer.Analyze(domain, prior, [duplicateProperty]);
        await Assert.That(incremental.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate
            && d.Message.Contains("AssignedTo"))).IsTrue();
    }

    [Test]
    public async Task AnalyzeIncremental_WithInvalidatedForeignType_StillReportsMutationInvariant() {
        var domain = new Domain("Support");
        var analyzer = new DomainModelAnalyzer();
        var local = new Primitive(domain, "string", TypeCategory.Text);
        new Domain.AddTypeCommand(domain, local).Apply();

        var prior = analyzer.Analyze(domain);
        await Assert.That(prior.HasErrors).IsFalse();

        var otherDomain = new Domain("Other");
        var foreign = new Primitive(otherDomain, "foreign-string", TypeCategory.Text);
        new Domain.AddTypeCommand(domain, foreign).Apply();

        var incremental = analyzer.Analyze(domain, prior, [foreign]);
        await Assert.That(incremental.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error
            && d.Code == DomainModelDiagnosticCodes.MutationInvariant
            && d.Message.Contains("foreign-string"))).IsTrue();
    }
}