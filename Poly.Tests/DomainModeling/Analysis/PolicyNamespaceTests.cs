using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;
using Poly.DomainModeling.Ontology;

using Action = Poly.DomainModeling.Ontology.Action;

namespace Poly.Tests.DomainModeling.Analysis;

/// <summary>
/// Policy methods share one unqualified namespace on the entity type with the
/// action methods. Action-local definitions (reachable through
/// <c>EvolutionBuilder.AddPolicyToAction</c>) take part in that namespace, so a
/// guard can never bind another definition's body or an action method.
/// </summary>
public sealed class PolicyNamespaceTests {
    private static Action Act(string name, params Policy[] policies) =>
        new(name, InvocationResult.Void, [], [], policies);

    private static Domain ItemWith(params Action[] actions) =>
        DomainTestFactory.Create("T", [new Entity("Item", [], actions, [], [])], []);

    private static IReadOnlyList<Diagnostic> Duplicates(AnalysisResult analysis) =>
        analysis.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && d.Code == DomainModelDiagnosticCodes.StructuralDuplicate)
            .ToList();

    // Two actions each define a local Ok. Before, only A's body was emitted and B ran with it.
    private static Domain TwoLocalOks() => ItemWith(
        Act("A", new Policy("Ok", DomainExpression.Literal(true))),
        Act("B", new Policy("Ok", DomainExpression.Literal(false))));

    // Go's local policy is named like the Submit action. Before, Go's guard called Submit().
    private static Domain LocalPolicyNamedLikeAction() => ItemWith(
        Act("Submit"),
        Act("Go", new Policy("Submit", DomainExpression.Literal(false))));

    [Test]
    public async Task ActionLocalPolicies_SameNameDifferentBodies_IsRedefinition() {
        var errors = Duplicates(DomainModelAnalyzer.Analyze(TwoLocalOks()));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("Policy 'Ok' is already defined on entity 'Item'.");
    }

    [Test]
    public async Task ActionLocalPolicies_SameNameDifferentBodies_ExportThrows() {
        var domain = TwoLocalOks();
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(() => DomainProgramProjection.ToSyntax(domain, (INodeMetadataProvider)analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Policy 'Ok' is already defined");
    }

    [Test]
    public async Task ActionLocalPolicy_NamedLikeAction_IsCollision() {
        var errors = Duplicates(DomainModelAnalyzer.Analyze(LocalPolicyNamedLikeAction()));

        await Assert.That(errors.Count).IsEqualTo(1);
        await Assert.That(errors[0].Message)
            .IsEqualTo("Name collision: an action and a policy are both named 'Submit' on entity 'Item'.");
    }

    [Test]
    public async Task ActionLocalPolicy_NamedLikeAction_ExportThrows() {
        var domain = LocalPolicyNamedLikeAction();
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(() => DomainProgramProjection.ToSyntax(domain, (INodeMetadataProvider)analysis))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Policy 'Submit' is already defined");
    }

    [Test]
    public async Task SamePolicyInstance_OnTwoActions_IsOneMethod() {
        var ok = new Policy("Ok", DomainExpression.Literal(true));
        var domain = ItemWith(Act("A", ok), Act("B", ok));
        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(Duplicates(analysis).Count).IsEqualTo(0);
        var item = DomainProgramProjection.ToSyntax(domain, analysis).Single(t => t.Name == "Item");
        await Assert.That(item.Methods!.Count(m => m.Name == "Ok")).IsEqualTo(1);
    }
}
