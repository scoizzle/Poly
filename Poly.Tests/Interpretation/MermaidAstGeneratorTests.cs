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
        await Assert.That(mermaid).Contains("IfStatement");
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
    public async Task Generate_UnclaimedRoot_DoesNotRenderItsTypeName() {
        var node = new TypeDefinitionNode("Widget", "Sample");
        var mermaid = new MermaidAstGenerator().Generate(node);
        await Assert.That(mermaid).Contains("graph TB");
        await Assert.That(mermaid).Contains("unclaimed: TypeDefinitionNode");
    }

    [Test]
    public async Task EveryClaimedKind_RendersEveryChild() {
        var failures = new List<string>();
        foreach (var (kind, fact) in NodeFacts.All.Where(kv => kv.Value.Producers.HasFlag(Producers.Mermaid))) {
            var sample = NodeFactCorpus.Cases.SingleOrDefault(c => c.Kind == kind);
            if (sample is null) {
                failures.Add($"{fact.Label}: no corpus sample");
                continue;
            }

            var diagram = new MermaidAstGenerator().Generate(sample.KindNode);
            if (!diagram.Contains($"\"{fact.Label}\"", StringComparison.Ordinal)) {
                failures.Add($"{fact.Label}: rendered without its table label");
            }

            foreach (var child in sample.KindNode.Children.Where(c => c is not null)) {
                if (!diagram.Contains(child!.Id.Value, StringComparison.Ordinal)) {
                    failures.Add($"{fact.Label}: dropped {child.GetType().Name}");
                }
            }
        }

        await Assert.That(failures).IsEmpty()
            .Because($"a Mermaid-claimed kind renders its label and keeps every child: {string.Join("; ", failures)}");
    }

    [Test]
    public async Task UnhandledKind_RendersItsLabelAndChildren() {
        var bare = new LabelDeclaration("outer", new Block(new Constant(1L)));
        var mermaid = new MermaidAstGenerator().Generate(bare);
        await Assert.That(mermaid).Contains("\"LabelDeclaration\"");
        await Assert.That(mermaid).Contains(bare.Statement.Id.Value);
    }

    [Test]
    public async Task UnclaimedKind_IsNotPresentedAsADiagramNode() {
        var bare = new Await(new Constant(1L));
        var mermaid = new MermaidAstGenerator().Generate(bare);
        await Assert.That(mermaid.Contains("\"Await\"")).IsFalse();
        await Assert.That(mermaid).Contains("unclaimed: Await");
        await Assert.That(mermaid).Contains(bare.Operand.Id.Value);
    }
}