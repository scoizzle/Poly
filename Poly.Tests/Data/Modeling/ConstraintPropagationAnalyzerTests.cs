using Poly.Data.Modeling;
using Poly.Data.Modeling.Analysis;
using Poly.Data.Modeling.Effects;
using Poly.Data.Modeling.Effects.Mutations;
using Poly.Data.Modeling.TypeSystem;
using Poly.Data.Modeling.Validation.Constraints;
using Poly.Introspection;

using DomainAction = Poly.Data.Modeling.Action;

namespace Poly.Tests.Data.Modeling;

public class ConstraintPropagationAnalyzerTests {
    private static Domain CreateDomain(string? name = null) => new(name ?? "Test Domain");
    private static Entity CreateEntity(Domain domain, string name) => new(domain, name);
    private static Primitive CreatePrimitive(Domain domain, string name, TypeCategory category) => new(domain, name, category);

    /// <summary>
    /// When Action A invokes Action B, constraints from Action B's effects
    /// should propagate back to Action A's parameters.
    /// </summary>
    [Test]
    public async Task NestedInvokeAction_PropagatesConstraintsFromInnerAction() {
        var domain = CreateDomain();
        var textType = CreatePrimitive(domain, "Text", TypeCategory.Text);

        var entity = CreateEntity(domain, "Book");
        var titleProp = new Property(domain, "Title", textType);
        MutationApply.AddType(domain, entity);
        MutationApply.AddProperty(entity, titleProp);
        MutationApply.AddConstraint(titleProp, new RequiredConstraint());

        // Inner action that accesses Book.Title
        var innerAction = new DomainAction(domain, "UpdateTitle", entity);
        var bookParam = new Property(domain, "book", entity);
        MutationApply.AddAction(entity, innerAction);
        MutationApply.AddParameter(innerAction, bookParam);

        var assignEffect = new Assign(domain) { Target = titleProp, Value = new Property(domain, "newTitle", textType) };
        MutationApply.AddEffect(innerAction, assignEffect);

        // Outer action that invokes inner action
        var outerAction = new DomainAction(domain, "ProcessBook", entity);
        var outerBookParam = new Property(domain, "book", entity);
        MutationApply.AddAction(entity, outerAction);
        MutationApply.AddParameter(outerAction, outerBookParam);

        var createEffect = new CreateEntityInstance(domain, entity);
        MutationApply.AddEffect(outerAction, createEffect);

        var invokeEffect = new InvokeAction(domain) { TargetAction = innerAction };
        MutationApply.AddEffect(outerAction, invokeEffect);
        invokeEffect.BindParameterFrom("book", createEffect, "entity");

        // Run analysis
        var analyzer = new DomainModelAnalyzer();
        var result = analyzer.Analyze(domain);

        await Assert.That(result.Diagnostics).IsEmpty();
    }

    /// <summary>
    /// Cyclic InvokeAction (A invokes B invokes A) should not cause infinite recursion.
    /// </summary>
    [Test]
    public async Task CyclicInvokeAction_DoesNotCauseInfiniteRecursion() {
        var domain = CreateDomain();
        var entity = CreateEntity(domain, "Item");
        MutationApply.AddType(domain, entity);

        // Action A
        var actionA = new DomainAction(domain, "ActionA", entity);
        var paramA = new Property(domain, "item", entity);
        MutationApply.AddAction(entity, actionA);
        MutationApply.AddParameter(actionA, paramA);

        // Action B
        var actionB = new DomainAction(domain, "ActionB", entity);
        var paramB = new Property(domain, "item", entity);
        MutationApply.AddAction(entity, actionB);
        MutationApply.AddParameter(actionB, paramB);

        // ActionA invokes ActionB
        var invokeB = new InvokeAction(domain) { TargetAction = actionB };
        MutationApply.AddEffect(actionA, invokeB);
        var createForB = new CreateEntityInstance(domain, entity);
        MutationApply.AddEffect(actionA, createForB);
        invokeB.BindParameterFrom("item", createForB, CreateEntityInstance.ResultParameterName);

        // ActionB invokes ActionA (cycle)
        var invokeA = new InvokeAction(domain) { TargetAction = actionA };
        MutationApply.AddEffect(actionB, invokeA);
        var createForA = new CreateEntityInstance(domain, entity);
        MutationApply.AddEffect(actionB, createForA);
        invokeA.BindParameterFrom("item", createForA, CreateEntityInstance.ResultParameterName);

        // Run analysis - should complete without infinite recursion
        var analyzer = new DomainModelAnalyzer();
        var result = analyzer.Analyze(domain);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    /// <summary>
    /// Constraints from deeply nested InvokeAction (A -> B -> C) should propagate to A's parameters.
    /// </summary>
    [Test]
    public async Task DeeplyNestedInvokeAction_PropagatesConstraintsToOutermostAction() {
        var domain = CreateDomain();
        var textType = CreatePrimitive(domain, "Text", TypeCategory.Text);

        var entity = CreateEntity(domain, "Document");
        var contentProp = new Property(domain, "Content", textType);
        MutationApply.AddType(domain, entity);
        MutationApply.AddProperty(entity, contentProp);
        MutationApply.AddConstraint(contentProp, new RequiredConstraint());

        // Innermost action C
        var actionC = new DomainAction(domain, "SaveContent", entity);
        var paramC = new Property(domain, "doc", entity);
        MutationApply.AddAction(entity, actionC);
        MutationApply.AddParameter(actionC, paramC);

        var assignC = new Assign(domain) { Target = contentProp, Value = new Property(domain, "text", textType) };
        MutationApply.AddEffect(actionC, assignC);

        // Middle action B - invokes C
        var actionB = new DomainAction(domain, "ProcessDocument", entity);
        var paramB = new Property(domain, "doc", entity);
        MutationApply.AddAction(entity, actionB);
        MutationApply.AddParameter(actionB, paramB);

        var invokeC = new InvokeAction(domain) { TargetAction = actionC };
        MutationApply.AddEffect(actionB, invokeC);
        var createForC = new CreateEntityInstance(domain, entity);
        MutationApply.AddEffect(actionB, createForC);
        invokeC.BindParameterFrom("doc", createForC, CreateEntityInstance.ResultParameterName);

        // Outermost action A - invokes B
        var actionA = new DomainAction(domain, "HandleDocument", entity);
        var paramA = new Property(domain, "doc", entity);
        MutationApply.AddAction(entity, actionA);
        MutationApply.AddParameter(actionA, paramA);

        var invokeB = new InvokeAction(domain) { TargetAction = actionB };
        MutationApply.AddEffect(actionA, invokeB);
        var createForB = new CreateEntityInstance(domain, entity);
        MutationApply.AddEffect(actionA, createForB);
        invokeB.BindParameterFrom("doc", createForB, CreateEntityInstance.ResultParameterName);

        // Run analysis
        var analyzer = new DomainModelAnalyzer();
        var result = analyzer.Analyze(domain);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.Diagnostics).IsEmpty();
    }

    [Test]
    public async Task ExpressionConstraintPropagation_InvalidNumericConstants_DoNotThrow() {
        var domain = CreateDomain();
        var numberType = CreatePrimitive(domain, "Number", TypeCategory.Integer);
        var entity = CreateEntity(domain, "Invoice");
        var amount = new Property(domain, "Amount", numberType);
        MutationApply.AddType(domain, numberType);
        MutationApply.AddType(domain, entity);
        MutationApply.AddProperty(entity, amount);
        MutationApply.AddConstraint(amount, new RangeConstraint(double.MinValue, double.MaxValue));

        var action = new DomainAction(domain, "SetAmount", entity);
        var parameter = new Property(domain, "amount", numberType);
        MutationApply.AddAction(entity, action);
        MutationApply.AddParameter(action, parameter);
        MutationApply.AddEffect(action, new Assign(domain) {
            Target = amount,
            Value = new ExpressionValue(domain, "ComputedAmount", numberType) {
                Expression = new Add(new Parameter(parameter.Name), new Constant(double.MaxValue))
            }
        });

        var analyzer = new DomainModelAnalyzer();
        var result = analyzer.Analyze(domain);

        await Assert.That(result).IsNotNull();
    }
}