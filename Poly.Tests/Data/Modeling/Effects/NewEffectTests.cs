using Poly.Data.Modeling;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.TypeSystem;
using Poly.Introspection;

using CompositeEffect = Poly.Data.Modeling.Effects.Composite;
using ConditionalEffect = Poly.Data.Modeling.Effects.Conditional;
using DeleteEntityInstanceEffect = Poly.Data.Modeling.Effects.DeleteEntityInstance;
using DomainAction = Poly.Data.Modeling.Action;
using LinkRelationshipEffect = Poly.Data.Modeling.Effects.LinkRelationship;
using TransitionRelationshipEffect = Poly.Data.Modeling.Effects.TransitionRelationship;
using UnlinkRelationshipEffect = Poly.Data.Modeling.Effects.UnlinkRelationship;

namespace Poly.Tests.Data.Modeling.Effects;

public class NewEffectTests {
    private static Domain CreateDomain(string? name = null) => new(name ?? "Test Domain");
    private static Entity CreateEntity(Domain domain, string name) => new(domain, name);
    private static Relationship CreateRelationship(Domain domain, string name, Entity? source = null, Entity? target = null) =>
        new(domain, name, source ?? CreateEntity(domain, "Source"), target ?? CreateEntity(domain, "Target"), RelationshipCardinality.ManyToMany, false);
    private static Stage CreateStage(Domain domain, string name) => new(domain, name);
    private static DomainValue CreateDomainValue(Domain domain, string name, DomainType type) => new TestDomainValue(domain, name, type);
    private static DomainAction CreateAction(Domain domain, string name, Entity entity) => new(domain, name, entity);
    private static Primitive CreatePrimitive(Domain domain, string name) => new(domain, name, TypeCategory.Text);

    [Test]
    public async Task DeleteEntityInstance_MissingEntityType_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "DeleteCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var effect = new DeleteEntityInstanceEffect(domain) { EntityType = null! };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("EntityType"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task DeleteEntityInstance_EntityTypeFromForeignDomain_ReportsError() {
        var domain = CreateDomain();
        var foreignDomain = CreateDomain("Foreign Domain");
        var entity = CreateEntity(domain, "SupportCase");
        var foreignEntity = CreateEntity(foreignDomain, "ForeignEntity");
        var action = CreateAction(domain, "DeleteCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(foreignDomain, foreignEntity);
        MutationApply.AddAction(entity, action);

        var effect = new DeleteEntityInstanceEffect(domain) { EntityType = foreignEntity };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task DeleteEntityInstance_ValidEntity_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var note = CreateEntity(domain, "Note");
        var action = CreateAction(domain, "DeleteNote", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, note);
        MutationApply.AddAction(entity, action);

        var effect = new DeleteEntityInstanceEffect(domain) { EntityType = note };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task LinkRelationship_MissingRelationship_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var primitive = CreatePrimitive(domain, "string");
        var target = CreateDomainValue(domain, "case-1", primitive);
        var action = CreateAction(domain, "LinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var effect = new LinkRelationshipEffect(domain) { Relationship = null!, Target = target };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Relationship"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task LinkRelationship_MissingTarget_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var action = CreateAction(domain, "LinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new LinkRelationshipEffect(domain) { Relationship = relationship, Target = null! };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Target"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task LinkRelationship_RelationshipFromForeignDomain_ReportsError() {
        var domain = CreateDomain();
        var foreignDomain = CreateDomain("Foreign");
        var entity = CreateEntity(domain, "SupportCase");
        var foreignSource = CreateEntity(foreignDomain, "ForeignSource");
        var foreignTargetEntity = CreateEntity(foreignDomain, "ForeignTarget");
        var foreignRelationship = CreateRelationship(foreignDomain, "ForeignRel", foreignSource, foreignTargetEntity);
        var primitive = CreatePrimitive(domain, "string");
        var target = CreateDomainValue(domain, "case-1", primitive);
        var action = CreateAction(domain, "LinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(foreignDomain, foreignSource);
        MutationApply.AddType(foreignDomain, foreignTargetEntity);
        MutationApply.AddRelationship(foreignDomain, foreignRelationship);
        MutationApply.AddAction(entity, action);

        var effect = new LinkRelationshipEffect(domain) { Relationship = foreignRelationship, Target = target };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task LinkRelationship_TargetFromForeignDomain_ReportsError() {
        var domain = CreateDomain();
        var foreignDomain = CreateDomain("Foreign");
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var foreignPrimitive = CreatePrimitive(foreignDomain, "string");
        var foreignTarget = CreateDomainValue(foreignDomain, "foreign-1", foreignPrimitive);
        var action = CreateAction(domain, "LinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new LinkRelationshipEffect(domain) { Relationship = relationship, Target = foreignTarget };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("domain"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task LinkRelationship_Valid_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var primitive = CreatePrimitive(domain, "string");
        var target = CreateDomainValue(domain, "case-1", primitive);
        var action = CreateAction(domain, "LinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new LinkRelationshipEffect(domain) { Relationship = relationship, Target = target };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task UnlinkRelationship_MissingRelationship_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var primitive = CreatePrimitive(domain, "string");
        var target = CreateDomainValue(domain, "case-1", primitive);
        var action = CreateAction(domain, "UnlinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var effect = new UnlinkRelationshipEffect(domain) { Relationship = null!, Target = target };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Relationship"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task UnlinkRelationship_MissingTarget_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var action = CreateAction(domain, "UnlinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new UnlinkRelationshipEffect(domain) { Relationship = relationship, Target = null! };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Target"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task UnlinkRelationship_Valid_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var primitive = CreatePrimitive(domain, "string");
        var target = CreateDomainValue(domain, "case-1", primitive);
        var action = CreateAction(domain, "UnlinkCase", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new UnlinkRelationshipEffect(domain) { Relationship = relationship, Target = target };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task TransitionRelationship_MissingRelationship_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var targetStage = CreateStage(domain, "Active");
        var action = CreateAction(domain, "Activate", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var effect = new TransitionRelationshipEffect(domain) { Relationship = null!, TargetStage = targetStage };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Relationship"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task TransitionRelationship_MissingTargetStage_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var action = CreateAction(domain, "TransitionRel", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddAction(entity, action);

        var effect = new TransitionRelationshipEffect(domain) { Relationship = relationship, TargetStage = null! };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("TargetStage"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task TransitionRelationship_StageNotBelongingToRelationship_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var validStage = CreateStage(domain, "ValidStage");
        var foreignStage = CreateStage(domain, "ForeignStage");
        var action = CreateAction(domain, "TransitionRel", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddStage(relationship, validStage);
        MutationApply.AddAction(entity, action);

        var effect = new TransitionRelationshipEffect(domain) { Relationship = relationship, TargetStage = foreignStage };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("must belong to relationship"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task TransitionRelationship_Valid_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var source = CreateEntity(domain, "Source");
        var targetEntity = CreateEntity(domain, "Target");
        var relationship = CreateRelationship(domain, "RelatedCases", source, targetEntity);
        var activeStage = CreateStage(domain, "Active");
        var action = CreateAction(domain, "ActivateRel", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddType(domain, source);
        MutationApply.AddType(domain, targetEntity);
        MutationApply.AddRelationship(domain, relationship);
        MutationApply.AddStage(relationship, activeStage);
        MutationApply.AddAction(entity, action);

        var effect = new TransitionRelationshipEffect(domain) { Relationship = relationship, TargetStage = activeStage };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task Conditional_MissingCondition_ReportsError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "ConditionalAction", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var innerEffect = new DeleteEntityInstanceEffect(domain) { EntityType = entity };
        var conditional = new ConditionalEffect(domain) { Condition = null! };
        conditional.AddEffect(innerEffect);
        var result = MutationApply.AddEffect(action, conditional);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Condition"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Conditional_WithValidChildEffects_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "ConditionalAction", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var innerEffect = new DeleteEntityInstanceEffect(domain) { EntityType = entity };
        var conditional = new ConditionalEffect(domain) { Condition = new Constant(true) };
        conditional.AddEffect(innerEffect);
        var result = MutationApply.AddEffect(action, conditional);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task Conditional_WithInvalidChildEffect_PropagatesError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "ConditionalAction", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var invalidEffect = new DeleteEntityInstanceEffect(domain) { EntityType = null! };
        var conditional = new ConditionalEffect(domain) { Condition = new Constant(true) };
        conditional.AddEffect(invalidEffect);
        var result = MutationApply.AddEffect(action, conditional);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("EntityType"));
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task Composite_AddNullEffect_ThrowsArgumentNullException() {
        var domain = CreateDomain();
        var composite = new CompositeEffect(domain);

        await Assert.That(async () => composite.AddEffect(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Composite_WithValidChildEffects_Succeeds() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "CompositeAction", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var deleteEffect = new DeleteEntityInstanceEffect(domain) { EntityType = entity };
        var composite = new CompositeEffect(domain);
        composite.AddEffect(deleteEffect);
        var result = MutationApply.AddEffect(action, composite);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task Composite_WithInvalidChildEffect_PropagatesError() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "SupportCase");
        var action = CreateAction(domain, "CompositeAction", entity);
        MutationApply.AddType(domain, entity);
        MutationApply.AddAction(entity, action);

        var invalidEffect = new DeleteEntityInstanceEffect(domain) { EntityType = null! };
        var composite = new CompositeEffect(domain);
        composite.AddEffect(invalidEffect);
        var result = MutationApply.AddEffect(action, composite);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("EntityType"));
        await Assert.That(error is not null).IsTrue();
    }

    // PublishEvent binding tests

    [Test]
    public async Task PublishEvent_AllPropertiesBoundViaActionParameter_Succeeds() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var param = new Property(domain, "OrderId", primitive);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);
        MutationApply.AddParameter(action, param);

        var effect = new PublishEvent(domain) { Event = @event };
        var result = domain.CreateMutation()
            .AddEffect(action, effect)
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.ActionParameter(param.Name))
            .Apply();

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task PublishEvent_AllPropertiesBoundViaEntityProperty_Succeeds() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var entityProp = new Property(domain, "OrderId", primitive);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddProperty(entity, entityProp);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);

        var effect = new PublishEvent(domain) { Event = @event };
        var result = domain.CreateMutation()
            .AddEffect(action, effect)
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.EntityProperty(entityProp.Name))
            .Apply();

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is null).IsTrue();
    }

    [Test]
    public async Task PublishEvent_MissingBinding_ReportsError() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);

        var effect = new PublishEvent(domain) { Event = @event };
        var result = MutationApply.AddEffect(action, effect);

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task PublishEvent_BindingReferencesNonexistentParameter_ReportsError() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);

        var effect = new PublishEvent(domain) { Event = @event };
        var result = domain.CreateMutation()
            .AddEffect(action, effect)
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.ActionParameter("NonExistentParam"))
            .Apply();

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task PublishEvent_BindingReferencesNonexistentEntityProperty_ReportsError() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);

        var effect = new PublishEvent(domain) { Event = @event };
        var result = domain.CreateMutation()
            .AddEffect(action, effect)
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.EntityProperty("NonExistentProp"))
            .Apply();

        var error = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        await Assert.That(error is not null).IsTrue();
    }

    [Test]
    public async Task SetEventPropertyBinding_Rollback_RemovesBinding() {
        var domain = CreateDomain();
        var primitive = CreatePrimitive(domain, "string");
        var entity = CreateEntity(domain, "Order");
        var @event = new Event(domain, "OrderPlaced");
        var action = CreateAction(domain, "PlaceOrder", entity);
        var param = new Property(domain, "OrderId", primitive);
        var eventProp = new Property(domain, "OrderId", primitive);

        MutationApply.AddType(domain, primitive);
        MutationApply.AddType(domain, entity);
        MutationApply.AddEvent(entity, @event);
        MutationApply.AddProperty(@event, eventProp);
        MutationApply.AddAction(entity, action);
        MutationApply.AddParameter(action, param);

        var effect = new PublishEvent(domain) { Event = @event };
        domain.CreateMutation().AddEffect(action, effect)
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.ActionParameter(param.Name))
            .Apply();

        // Now overwrite with a bad binding — should roll back to old binding
        var badResult = domain.CreateMutation()
            .SetEventPropertyBinding(action, effect, eventProp.Name, new EventPropertyBindingSource.ActionParameter("DoesNotExist"))
            .Apply();

        // After rollback, the original binding should still be present
        await Assert.That(effect.PropertyBindings.ContainsKey(eventProp.Name)).IsTrue();
        var binding = effect.PropertyBindings[eventProp.Name] as EventPropertyBindingSource.ActionParameter;
        await Assert.That(binding?.ParameterName).IsEqualTo(param.Name);
    }

    private sealed record TestDomainValue(Domain domain, string name, DomainType type) : DomainValue(domain, name, type);
}