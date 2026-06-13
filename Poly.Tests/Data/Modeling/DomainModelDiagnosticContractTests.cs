using System.Reflection;

using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class DomainModelDiagnosticContractTests {
    [Test]
    public async Task StructuralAnalyzer_DuplicateType_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var left = new Primitive(domain, "string", TypeCategory.Text);
        var right = new Primitive(domain, "string", TypeCategory.Text);

        new Domain.AddTypeCommand(domain, left).Apply();
        new Domain.AddTypeCommand(domain, right).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Structural.DuplicateFragment);
    }

    [Test]
    public async Task StructuralAnalyzer_DuplicateEntityProperty_UsesStructuralDuplicateOnly() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var left = new Property(domain, "Title", stringType);
        var right = new Property(domain, "Title", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, left).Apply();
        new Entity.AddPropertyCommand(entity, right).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var structuralDuplicate = analysis.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Code == DomainModelDiagnosticCodes.StructuralDuplicate &&
            d.Message.Contains("Title", StringComparison.Ordinal));

        await Assert.That(structuralDuplicate).IsNotNull();
        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility &&
            d.Message.Contains("Duplicate", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task SemanticAnalyzer_StageInheritance_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var parentStage = new Stage(domain, "Open");
        var childStage = new Stage(domain, "Pending");

        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();
        new Entity.AddStageCommand(parent, parentStage).Apply();
        new Entity.AddStageCommand(child, childStage).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticStageInheritance);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Semantic.StageInheritanceFragment);
    }

    [Test]
    public async Task StructuralAnalyzer_StageInheritanceCycle_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var a = new Stage(domain, "A");
        var b = new Stage(domain, "B") { Parent = a };
        var parentField = typeof(Stage).GetField("<Parent>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Stage parent backing field was not found.");
        parentField.SetValue(a, b);

        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddStageCommand(entity, a).Apply();
        new Entity.AddStageCommand(entity, b).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.StructuralCycle);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("participates in an inheritance cycle", StringComparison.Ordinal);
    }

    [Test]
    public async Task PolicyAnalyzer_MissingEntityPropertyRule_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Order");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var externalProperty = new Property(domain, "ExternalValue", stringType);
        var policy = new Policy(domain, "RequiresExternal");
        var rule = new PropertyRule(domain, "RequireExternalRule", externalProperty, new RequiredConstraint());

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Policy.AddRuleCommand(policy, rule).Apply();
        new Entity.AddPolicyCommand(entity, policy).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.PolicyMissingProperty);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Policy.MissingPropertyFragment);
    }

    [Test]
    public async Task EffectAnalyzer_PublishEventMissingBinding_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "SupportCase");
        var @event = new Event(domain, "CaseAssigned");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var assignedTo = new Property(domain, "AssignedTo", stringType);
        var action = new DomainAction(domain, "Assign", entity);
        var effect = new PublishEvent(domain) { Event = @event };

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Domain.AddTypeCommand(domain, @event).Apply();
        new Event.AddPropertyCommand(@event, assignedTo).Apply();
        new Entity.AddActionCommand(entity, action).Apply();
        new DomainAction.AddEffectCommand(action, effect).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EffectBinding);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Effect.BindingFragment);
    }

    [Test]
    public async Task StructuralAnalyzer_InvalidOwnershipCardinality_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var source = new Entity(domain, "Customer");
        var target = new Entity(domain, "SupportCase");
        var relationship = new Relationship(domain, "OwnedCases", source, target, RelationshipCardinality.ManyToMany, true);

        new Domain.AddTypeCommand(domain, source).Apply();
        new Domain.AddTypeCommand(domain, target).Apply();
        new Domain.AddRelationshipCommand(domain, relationship).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.StructuralOwnership);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Structural.OwnershipFragment);
    }

    [Test]
    public async Task StructuralAnalyzer_ForeignTypeMembership_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var otherDomain = new Domain("Other");
        var foreignType = new Primitive(otherDomain, "foreign-string", TypeCategory.Text);

        new Domain.AddTypeCommand(domain, foreignType).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.MutationInvariant);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Structural.MutationInvariantFragment);
    }

    [Test]
    public async Task SemanticAnalyzer_StageActionEntityMismatch_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var owner = new Entity(domain, "Owner");
        var other = new Entity(domain, "Other");
        var stage = new Stage(domain, "InProgress");
        var foreignAction = new DomainAction(domain, "Assign", other);

        new Domain.AddTypeCommand(domain, owner).Apply();
        new Domain.AddTypeCommand(domain, other).Apply();
        new Entity.AddStageCommand(owner, stage).Apply();
        new Stage.AddActionCommand(stage, foreignAction).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticActionVisibility);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Semantic.ActionVisibilityFragment);
    }

    [Test]
    public async Task SemanticAnalyzer_PropertyTypeFromForeignDomain_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var otherDomain = new Domain("Other");
        var entity = new Entity(domain, "Ticket");
        var foreignType = new Primitive(otherDomain, "foreign-text", TypeCategory.Text);
        var property = new Property(domain, "Title", foreignType);

        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, property).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticTypeCompatibility);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Semantic.TypeCompatibilityFragment);
    }

    [Test]
    public async Task PolicyAnalyzer_MissingActorType_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Order");
        var missingActor = new Actor(domain, "Ghost");
        var policy = new Policy(domain, "ActorPolicy");
        var rule = new ActorTypeRule(domain, "ActorRule", missingActor);

        new Domain.AddTypeCommand(domain, entity).Apply();
        new Policy.AddRuleCommand(policy, rule).Apply();
        new Entity.AddPolicyCommand(entity, policy).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.PolicyActorReference);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Policy.ActorReferenceFragment);
    }

    [Test]
    public async Task PolicyAnalyzer_UnknownConstraint_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Order");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var property = new Property(domain, "Title", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, property).Apply();
        new Property.AddConstraintCommand(property, new UnsupportedConstraint()).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.PolicyAstGeneration);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Policy.AstGenerationFragment);
    }

    [Test]
    public async Task PolicyAnalyzer_IncompatibleEnumAndRangeConstraint_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Order");
        var statusType = new Primitive(domain, "Status", TypeCategory.Text);
        var property = new Property(domain, "Status", statusType);

        new Domain.AddTypeCommand(domain, statusType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, property).Apply();
        new Property.AddConstraintCommand(property, new EnumConstraint(new EnumConstraint.EnumMember("Open"), new EnumConstraint.EnumMember("Closed"))).Apply();
        new Property.AddConstraintCommand(property, new RangeConstraint(0, 2)).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.PolicyAstGeneration);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains("incompatible EnumConstraint", StringComparison.Ordinal);
    }

    [Test]
    public async Task EffectAnalyzer_TransitionMissingRequiredPropertyCoverage_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "SupportCase");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var title = new Property(domain, "Title", stringType);
        var start = new Stage(domain, "Start");
        var done = new Stage(domain, "Done");
        var action = new DomainAction(domain, "Close", entity);
        var policy = new Policy(domain, "DoneRequiresTitle");
        var rule = new PropertyRule(domain, "RequireTitle", title, new RequiredConstraint());
        var transition = new StageTransition(domain) { TargetStage = done };

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddPropertyCommand(entity, title).Apply();
        new Entity.AddStageCommand(entity, start).Apply();
        new Entity.AddStageCommand(entity, done).Apply();
        new Policy.AddRuleCommand(policy, rule).Apply();
        new Stage.AddPolicyCommand(done, policy).Apply();
        new Entity.AddActionCommand(entity, action).Apply();
        new DomainAction.AddEffectCommand(action, transition).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.EffectUnsatisfiedRequirement);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Effect.UnsatisfiedRequirementFragment);
    }

    [Test]
    public async Task EnumConstraintSubsetAnalyzer_InvalidSubset_ReportsError() {
        var domain = new Domain("Support");
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var parentProperty = new Property(domain, "Status", stringType);
        var childProperty = new Property(domain, "Status", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();
        new Entity.AddPropertyCommand(parent, parentProperty).Apply();
        new Property.AddConstraintCommand(parentProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"),
            new EnumConstraint.EnumMember("Pending"))).Apply();
        new Entity.AddPropertyCommand(child, childProperty).Apply();
        new Property.AddConstraintCommand(childProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Unknown"))).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Semantic.ConstraintMismatchFragment);
    }

    [Test]
    public async Task EnumConstraintSubsetAnalyzer_ValidSubset_NoError() {
        var domain = new Domain("Support");
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var parentProperty = new Property(domain, "Status", stringType);
        var childProperty = new Property(domain, "Status", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();
        new Entity.AddPropertyCommand(parent, parentProperty).Apply();
        new Property.AddConstraintCommand(parentProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"),
            new EnumConstraint.EnumMember("Pending"))).Apply();
        new Entity.AddPropertyCommand(child, childProperty).Apply();
        new Property.AddConstraintCommand(childProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"))).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch);

        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EnumConstraintSubsetAnalyzer_EmptyChildEnum_NoError() {
        var domain = new Domain("Support");
        var parent = new Entity(domain, "Parent");
        var child = new Entity(domain, "Child", parent);
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var parentProperty = new Property(domain, "Status", stringType);
        var childProperty = new Property(domain, "Status", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();
        new Entity.AddPropertyCommand(parent, parentProperty).Apply();
        new Property.AddConstraintCommand(parentProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"))).Apply();
        new Entity.AddPropertyCommand(child, childProperty).Apply();
        // No enum constraint on child - inherits parent's; no subset mismatch

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch);

        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EnumConstraintSubsetAnalyzer_MultiLevelInheritance_Valid() {
        var domain = new Domain("Support");
        var grandParent = new Entity(domain, "GrandParent");
        var parent = new Entity(domain, "Parent", grandParent);
        var child = new Entity(domain, "Child", parent);
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var gpProperty = new Property(domain, "Status", stringType);
        var parentProperty = new Property(domain, "Status", stringType);
        var childProperty = new Property(domain, "Status", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, grandParent).Apply();
        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();

        new Entity.AddPropertyCommand(grandParent, gpProperty).Apply();
        new Property.AddConstraintCommand(gpProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"),
            new EnumConstraint.EnumMember("Pending"))).Apply();

        new Entity.AddPropertyCommand(parent, parentProperty).Apply();
        new Property.AddConstraintCommand(parentProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"))).Apply();

        new Entity.AddPropertyCommand(child, childProperty).Apply();
        new Property.AddConstraintCommand(childProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"))).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch);

        await Assert.That(diagnostic).IsNull();
    }

    [Test]
    public async Task EnumConstraintSubsetAnalyzer_MultiLevelInheritance_Invalid() {
        var domain = new Domain("Support");
        var grandParent = new Entity(domain, "GrandParent");
        var parent = new Entity(domain, "Parent", grandParent);
        var child = new Entity(domain, "Child", parent);
        var stringType = new Primitive(domain, "string", TypeCategory.Text);

        var gpProperty = new Property(domain, "Status", stringType);
        var parentProperty = new Property(domain, "Status", stringType);
        var childProperty = new Property(domain, "Status", stringType);

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, grandParent).Apply();
        new Domain.AddTypeCommand(domain, parent).Apply();
        new Domain.AddTypeCommand(domain, child).Apply();

        new Entity.AddPropertyCommand(grandParent, gpProperty).Apply();
        new Property.AddConstraintCommand(gpProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"),
            new EnumConstraint.EnumMember("Inactive"))).Apply();

        new Entity.AddPropertyCommand(parent, parentProperty).Apply();
        new Property.AddConstraintCommand(parentProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Active"))).Apply();

        new Entity.AddPropertyCommand(child, childProperty).Apply();
        // Child has "Unknown" which is not in parent's or grandparent's enum
        new Property.AddConstraintCommand(childProperty, new EnumConstraint(
            new EnumConstraint.EnumMember("Unknown"))).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.SemanticConstraintMismatch);

        await Assert.That(diagnostic).IsNotNull();
    }

    [Test]
    public async Task ContractIntegrationAnalyzer_MissingBindingParameter_UsesExpectedCodeAndMessageFragment() {
        var domain = new Domain("Support");
        var entity = new Entity(domain, "Ticket");
        var stringType = new Primitive(domain, "string", TypeCategory.Text);
        var action = new DomainAction(domain, "SyncTicket", entity);
        var contract = new ImportedContract(domain, "CrmContract", ContractSourceKind.ExternalProvider, "crm://contracts/ticket", "v1");
        var endpoint = new ContractEndpoint(domain, "UpdateTicket", ContractEndpointKind.Operation, ContractEndpointDirection.Inbound, stringType);
        var binding = new ContractBinding(domain, "CrmTicketSync", contract, endpoint, action, "payload");

        new Domain.AddTypeCommand(domain, stringType).Apply();
        new Domain.AddTypeCommand(domain, entity).Apply();
        new Entity.AddActionCommand(entity, action).Apply();
        new Domain.AddImportedContractCommand(domain, contract).Apply();
        new ImportedContract.AddEndpointCommand(contract, endpoint).Apply();
        new Domain.AddContractBindingCommand(domain, binding).Apply();

        var analysis = DomainModelAnalyzer.Analyze(domain);
        var diagnostic = analysis.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Code == DomainModelDiagnosticCodes.ContractIntegration);

        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Message).Contains(DomainModelDiagnosticContracts.Contract.IntegrationFragment);
    }

    private sealed record UnsupportedConstraint : Constraint {
        public TypeCategory ApplicableCategories => TypeCategory.None;
    }
}