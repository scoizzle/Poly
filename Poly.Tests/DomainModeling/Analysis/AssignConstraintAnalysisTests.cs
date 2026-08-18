using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology.Constraints;
using Poly.DomainModeling.Ontology.Effects;
using Poly.Introspection;

namespace Poly.Tests.DomainModeling.Analysis;

using Poly.DomainModeling.Ontology;
// Resolve Action ambiguity: Poly.DomainModeling.Ontology.Action vs System.Action
using DmAction = Poly.DomainModeling.Ontology.Action;
// Resolve PrimitiveType ambiguity: Poly.DomainModeling vs Poly.Introspection
using DmPrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;

/// <summary>
/// Tests for constraint validation on assignments, initializers, and parameter flows.
/// Verifies that the analysis phase catches constraint violations when literal values
/// or parameter flows violate property constraints such as range, length, pattern,
/// enum membership, and required.
/// </summary>
public class AssignConstraintAnalysisTests {
    // ── Primitives used across tests ───────────────────────────────────────
    private static readonly DmPrimitiveType Text = new("Text", TypeCategory.Text, []);
    private static readonly DmPrimitiveType Number = new("Number", TypeCategory.Numeric, []);

    // ════════════════════════════════════════════════════════════════════════
    //  Literal assignments against property constraints
    // ════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Assign_LiteralOutsideRange_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [new DmAction("SetTotal", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Literal(200))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation &&
            d.Severity == DiagnosticSeverity.Error)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralAtRangeBoundary_NoError() {
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [new DmAction("SetTotal", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Total"), DomainExpression.Literal(100))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_LiteralBelowRange_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Quantity", new DomainTypeReference("Number"),
                [new RangeConstraint(1, null)])],
            [new DmAction("SetQty", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Quantity"), DomainExpression.Literal(0))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralExceedsLength_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Code", new DomainTypeReference("Text"),
                [new LengthConstraint(1, 10)])],
            [new DmAction("SetCode", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Code"),
                    DomainExpression.Literal("this-is-way-too-long"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralValidLength_NoError() {
        var entity = new Entity("Item",
            [new Property("Code", new DomainTypeReference("Text"),
                [new LengthConstraint(1, 10)])],
            [new DmAction("SetCode", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Code"),
                    DomainExpression.Literal("ok"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_LiteralViolatesPattern_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Sku", new DomainTypeReference("Text"),
                [new PatternConstraint("^[A-Z]{3}-\\d{4}$")])],
            [new DmAction("SetSku", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Sku"),
                    DomainExpression.Literal("abc-123"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralMatchesPattern_NoError() {
        var entity = new Entity("Item",
            [new Property("Sku", new DomainTypeReference("Text"),
                [new PatternConstraint("^[A-Z]{3}-\\d{4}$")])],
            [new DmAction("SetSku", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Sku"),
                    DomainExpression.Literal("ABC-1234"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_LiteralNullOnRequired_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Name", new DomainTypeReference("Text"),
                [new RequiredConstraint()])],
            [new DmAction("SetName", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Name"), DomainExpression.Literal(null))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralNonNullOnRequired_NoError() {
        var entity = new Entity("Item",
            [new Property("Name", new DomainTypeReference("Text"),
                [new RequiredConstraint()])],
            [new DmAction("SetName", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Name"),
                    DomainExpression.Literal("hello"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_NoConstraintsOnProperty_NoConstraintDiagnostics() {
        var entity = new Entity("Item",
            [new Property("Name", new DomainTypeReference("Text"), [])],
            [new DmAction("SetName", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Name"),
                    DomainExpression.Literal("anything"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Create-instance initializer constraint validation
    // ════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task CreateInstance_InitializerLiteralOutOfRange_ReportsError() {
        var target = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [], [], []);
        var source = new Entity("Factory",
            [],
            [new DmAction("MakeItem", InvocationResult.Void, [],
                [new CreateEntityInstance(new DomainTypeReference("Item"),
                    [new PropertyBinding("Total", DomainExpression.Literal(500))])],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, source, target], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task CreateInstance_InitializerLiteralValid_NoError() {
        var target = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [], [], []);
        var source = new Entity("Factory",
            [],
            [new DmAction("MakeItem", InvocationResult.Void, [],
                [new CreateEntityInstance(new DomainTypeReference("Item"),
                    [new PropertyBinding("Total", DomainExpression.Literal(50))])],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, source, target], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Range constraint edge cases
    // ════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Assign_LiteralFloatWithinRange_NoError() {
        var entity = new Entity("Item",
            [new Property("Score", new DomainTypeReference("Number"),
                [new RangeConstraint(0.0, 100.0)])],
            [new DmAction("SetScore", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Score"),
                    DomainExpression.Literal(99.5))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_LiteralFloatOutOfRange_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Score", new DomainTypeReference("Number"),
                [new RangeConstraint(0.0, 100.0)])],
            [new DmAction("SetScore", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Score"),
                    DomainExpression.Literal(150.5))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_LiteralNegativeOutOfRange_ReportsError() {
        var entity = new Entity("Item",
            [new Property("Count", new DomainTypeReference("Number"),
                [new RangeConstraint(0, null)])],
            [new DmAction("SetCount", InvocationResult.Void, [],
                [new AssignEffect(DomainExpression.Property("Count"),
                    DomainExpression.Literal(-5))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Parameter-to-property constraint compatibility
    // ════════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Assign_ParameterExceedsPropertyRange_Warns() {
        // Parameter has range(0, 200) but property has range(0, 100)
        // Parameter allows values the property rejects → warning
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [new DmAction("SetTotal", InvocationResult.Void,
                [new Property("amount", new DomainTypeReference("Number"),
                    [new RangeConstraint(0, 200)])],
                [new AssignEffect(DomainExpression.Property("Total"),
                    DomainExpression.Parameter("amount"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }

    [Test]
    public async Task Assign_ParameterSubsetOfPropertyRange_NoWarning() {
        // Parameter has range(0, 50) which is within property range(0, 100)
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [new DmAction("SetTotal", InvocationResult.Void,
                [new Property("amount", new DomainTypeReference("Number"),
                    [new RangeConstraint(0, 50)])],
                [new AssignEffect(DomainExpression.Property("Total"),
                    DomainExpression.Parameter("amount"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsFalse();
    }

    [Test]
    public async Task Assign_ParameterNoRangeOnPropertyWithRange_Warns() {
        // Parameter has no range, property has range(0, 100)
        var entity = new Entity("Item",
            [new Property("Total", new DomainTypeReference("Number"),
                [new RangeConstraint(0, 100)])],
            [new DmAction("SetTotal", InvocationResult.Void,
                [new Property("amount", new DomainTypeReference("Number"), [])],
                [new AssignEffect(DomainExpression.Property("Total"),
                    DomainExpression.Parameter("amount"))],
                [])],
            [],
            []);
        var domain = DomainTestFactory.Create("Test", [Text, Number, entity], []);

        var analysis = DomainModelAnalyzer.Analyze(domain);

        await Assert.That(analysis.Diagnostics.Any(d =>
            d.Code == DomainModelDiagnosticCodes.EffectConstraintViolation)).IsTrue();
    }
}