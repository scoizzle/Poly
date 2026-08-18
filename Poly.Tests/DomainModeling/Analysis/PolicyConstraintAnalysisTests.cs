using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Evolution;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Bootstrap;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Tests for policy property-reference validation in PolicyConstraintAnalyzer.
/// Ensures that policies referencing non-existent entity properties are
/// rejected at analysis time (evolution gate).
/// </summary>
public class PolicyConstraintAnalysisTests {
    [Test]
    public async Task EntityPolicy_UnknownProperty_FailsEvolution() {
        // Policy references "MissingProp" which doesn't exist on Person
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), [])));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Person",
                new Policy("HasName",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("MissingProp"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("MissingProp")).IsTrue();
    }

    [Test]
    public async Task EntityPolicy_KnownProperty_Succeeds() {
        // Policy references "Name" which exists on Person
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), [])));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Person",
                new Policy("HasName",
                    DomainExpression.Equal(
                        DomainExpression.Property("Name"),
                        DomainExpression.Literal("Alice"))))]);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task EntityPolicy_OwnedAccess_NestedProperty_NotValidatedAgainstEntity() {
        // OwnedAccess("BirthCertificate", Property("Time")) — "Time" is on BirthCertificate,
        // not on Person. This should NOT cause a validation error.
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("BirthCertificate",
                       new DomainTypeReference("Boolean"), [])));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Person",
                new Policy("HasBirthCert",
                    DomainExpression.Exists(
                        DomainExpression.Owned("BirthCertificate",
                            DomainExpression.Property("Time")))))]);

        // Should succeed because OwnedAccess children are not validated against source entity
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task EntityPolicy_RelationshipNavigation_TargetProperty_NowValidatedAgainstEntity() {
        // Q1'''''.4: RelationshipNavigation("rel", Property("TargetField")) — "TargetField" is on
        // the related entity, not on the source entity. This should now trigger a validation error.
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Source")
                   .AddEntity("Target"));

        // Add a relationship and a policy using RelationshipNavigation
        var domain = new DomainEvolution(start).Evolve()
            .AddRelationship("rel", "Source", "Target", RelationshipCardinality.OneToOne, false)
            .Apply().Root;

        // TargetField doesn't exist on Target entity (Target has no properties)
        var result = new DomainEvolution(domain).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("CheckTarget",
                    DomainExpression.Exists(
                        DomainExpression.RelationshipNav("rel",
                            DomainExpression.Property("TargetField")))))]);

        // Should now fail because body property validation checks target entity
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("TargetField")).IsTrue();
    }

    [Test]
    public async Task StagePolicy_UnknownProperty_FailsEvolution() {
        // Stage policy references "MissingProp" which doesn't exist on Person
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), []))
                   .AddStage("Person", "Active"));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToStageChange("Person", "Active",
                new Policy("CheckName",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("MissingProp"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("MissingProp")).IsTrue();
    }

    [Test]
    public async Task StagePolicy_KnownProperty_Succeeds() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Age",
                       new DomainTypeReference("Number"), []))
                   .AddStage("Person", "Active"));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToStageChange("Person", "Active",
                new Policy("IsAdult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ActionPolicy_UnknownProperty_FailsEvolution() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), []))
                   .AddAction("Person", "DoSomething"));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToActionChange("Person", "DoSomething",
                new Policy("CheckName",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("MissingProp"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("MissingProp")).IsTrue();
    }

    [Test]
    public async Task ActionPolicy_KnownProperty_Succeeds() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Age",
                       new DomainTypeReference("Number"), []))
                   .AddAction("Person", "DoSomething"));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToActionChange("Person", "DoSomething",
                new Policy("IsAdult",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("Age"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task StageActionPolicy_UnknownProperty_FailsEvolution() {
        // Policy on a stage-scoped action referencing a non-existent entity property
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), []))
                   .AddStage("Person", "Active"));

        // Add action to stage then add policy to it
        var withAction = new DomainEvolution(start).Evolve()
            .AddActionToStage("Person", "Active", "DoIt")
            .Apply().Root;

        var result = new DomainEvolution(withAction).Apply([
            new AddPolicyToActionChange("Person", "DoIt",
                new Policy("CheckName",
                    DomainExpression.GreaterThanOrEqual(
                        DomainExpression.Property("MissingProp"),
                        DomainExpression.Literal(18))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("MissingProp")).IsTrue();
    }

    [Test]
    public async Task ParameterAccess_Property_NotValidated() {
        // ParameterAccess references are not entity properties and should be ignored
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), [])));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Person",
                new Policy("CheckParam",
                    DomainExpression.Equal(
                        DomainExpression.Property("Name"),
                        DomainExpression.Parameter("inputValue"))))]);

        // Should succeed — ParameterAccess("inputValue") is not a property reference
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task OwnedAccess_UnknownValueType_FailsEvolution() {
        // PCA.8: OwnedAccess names a ValueType that doesn't exist in the domain
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Person")
                   .AddPropertyToEntity("Person", new Property("Name",
                       new DomainTypeReference("Text"), [])));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Person",
                new Policy("BadRef",
                    DomainExpression.Exists(
                        DomainExpression.Owned("NonExistentValueType",
                            DomainExpression.Property("Time")))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("NonExistentValueType")).IsTrue();
    }

    // ── Q3′ quantifier analysis ───────────────────────────────

    [Test]
    public async Task Quantifier_Any_OnOneToMany_Succeeds() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Target")
                   .AddPropertyToEntity("Target", new Property("Flag",
                       new DomainTypeReference("Boolean"), []))
                   .AddEntity("Source"));

        var domain = new DomainEvolution(start).Evolve()
            .AddRelationship("items", "Source", "Target", RelationshipCardinality.OneToMany, false)
            .Apply().Root;

        var result = new DomainEvolution(domain).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("HasFlagged",
                    DomainExpression.Any("items",
                        DomainExpression.Equal(
                            DomainExpression.Property("Flag"),
                            DomainExpression.Literal(true)))))]);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Quantifier_Any_UnknownBodyProperty_FailsEvolution() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Target")
                   .AddEntity("Source"));

        var domain = new DomainEvolution(start).Evolve()
            .AddRelationship("items", "Source", "Target", RelationshipCardinality.OneToMany, false)
            .Apply().Root;

        var result = new DomainEvolution(domain).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("BadRef",
                    DomainExpression.Any("items",
                        DomainExpression.Property("NonExistent"))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("NonExistent")).IsTrue();
    }

    [Test]
    public async Task Quantifier_Any_OnOneToOne_FailsEvolution() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Target")
                   .AddPropertyToEntity("Target", new Property("Flag",
                       new DomainTypeReference("Boolean"), []))
                   .AddEntity("Source"));

        var domain = new DomainEvolution(start).Evolve()
            .AddRelationship("link", "Source", "Target", RelationshipCardinality.OneToOne, false)
            .Apply().Root;

        var result = new DomainEvolution(domain).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("BadQuant",
                    DomainExpression.Any("link",
                        DomainExpression.Property("Flag"))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("OneToOne")).IsTrue();
    }

    [Test]
    public async Task Quantifier_Any_UnknownRelationship_FailsEvolution() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Source"));

        var result = new DomainEvolution(start).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("BadQuant",
                    DomainExpression.Any("nonExistentRel",
                        DomainExpression.Property("Flag"))))]);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureSummary!.Contains("nonExistentRel")).IsTrue();
    }

    [Test]
    public async Task Quantifier_BareCount_NoBody_Succeeds() {
        var start = DomainFactory.Create("Test", builder =>
            builder.AddEntity("Target")
                   .AddEntity("Source"));

        var domain = new DomainEvolution(start).Evolve()
            .AddRelationship("items", "Source", "Target", RelationshipCardinality.OneToMany, false)
            .Apply().Root;

        var result = new DomainEvolution(domain).Apply([
            new AddPolicyToEntityChange("Source",
                new Policy("CountCheck",
                    DomainExpression.GreaterThan(
                        DomainExpression.Count("items", null),
                        DomainExpression.Literal(5L))))]);

        await Assert.That(result.Succeeded).IsTrue();
    }
}