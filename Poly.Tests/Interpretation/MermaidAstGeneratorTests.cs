using Poly.Interpretation;
using Poly.Interpretation.Mermaid;

namespace Poly.Tests.Interpretation;

/// <summary>
/// Unit coverage under Interpretation/ for <see cref="MermaidAstGenerator"/>
/// (Integration/ already has broader visualization samples).
/// </summary>
public class MermaidAstGeneratorTests {
    [Test]
    public async Task Generate_Add_ContainsGraphAndOperands() {
        var mermaid = new MermaidAstGenerator().Generate(new Add(new Constant(2), new Constant(3)));
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("Add");
        await Assert.That(mermaid).Contains("left");
        await Assert.That(mermaid).Contains("right");
    }

    [Test]
    public async Task Generate_IfStatement_ShowsBranches() {
        var node = new IfStatement(new Constant(true), new Constant(1L), new Constant(0L));
        var mermaid = new MermaidAstGenerator().Generate(node);
        await Assert.That(mermaid).Contains("If Statement");
        await Assert.That(mermaid).Contains("condition");
    }

    [Test]
    public async Task Generate_WithAnalysis_EnrichesLabels() {
        var ast = new Add(new Constant(2L), new Constant(3L));
        var analysis = Interpreter.Analyze(ast);
        var mermaid = new MermaidAstGenerator(analysis).Generate(ast);
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("Add");
    }

    [Test]
    public async Task Generate_DirectionLR_UsesRequestedDirection() {
        var mermaid = new MermaidAstGenerator().Generate(new Constant(1L), direction: "LR");
        await Assert.That(mermaid).Contains("graph LR");
    }

    [Test]
    public async Task Generate_AnalysisOnly_TypeDefinition_Smoke() {
        var node = new TypeDefinitionNode("Widget", "Sample");
        var mermaid = new MermaidAstGenerator().Generate(node);
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("TypeDefinitionNode");
    }
}
