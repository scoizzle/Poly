using Poly.Interpretation.AbstractSyntaxTree;
using Poly.Interpretation.Analysis;
using Poly.Interpretation.Analysis.ControlFlow;

using static Poly.Interpretation.AbstractSyntaxTree.NodeExtensions;

namespace Poly.Tests.Interpretation;

public class ControlFlowAnalysisTests {
    [Test]
    public async Task SimpleSequence_HasSingleBlock() {
        // Arrange: a simple block with sequential statements
        var ast = new Block(
            Wrap(1),
            Wrap(2),
            Wrap(3)
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(cfg.Entry).IsNotNull();
        await Assert.That(cfg.Entry.IsReachable).IsTrue();
    }

    [Test]
    public async Task IfStatement_CreatesBranches() {
        // Arrange: if statement with both branches
        var condition = new Variable("x");
        var thenBranch = Wrap(1);
        var elseBranch = Wrap(2);

        var ast = new IfStatement(condition, thenBranch, elseBranch);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have at least: condition block, then block, else block, merge block
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task ReturnStatement_TerminatesBlock() {
        // Arrange: block with return followed by code
        var ast = new Block(
            Wrap(1),
            new ReturnStatement(Wrap(42)),
            Wrap(3) // This should be dead code
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Check for unreachable code diagnostic
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task WhileLoop_CreatesBackEdge() {
        // Arrange: while loop
        var condition = new Variable("x");
        var body = Wrap(1);

        var ast = new WhileLoop(condition, body);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have at least: entry, condition, body, exit blocks
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task BreakStatement_JumpsToLoopExit() {
        // Arrange: while loop with break
        var condition = Wrap(true);
        var body = new Block(
            Wrap(1),
            new BreakStatement(),
            Wrap(2) // Dead code after break
        );

        var ast = new WhileLoop(condition, body);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after break
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task GotoAndLabel_ConnectsBlocks() {
        // Arrange: goto statement to a label
        var ast = new Block(
            Wrap(1),
            new GotoStatement("end"),
            Wrap(2), // Dead code
            new LabelDeclaration("end", Wrap(3))
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // The goto should connect to the label block
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task ForLoop_HasProperStructure() {
        // Arrange: for loop with all components
        var init = new Variable("i", Wrap(0));
        var condition = new Variable("i");
        var increment = Wrap(1);
        var body = Wrap(2);

        var ast = new ForLoop(init, condition, increment, body);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have: entry, condition, body, iterator, exit blocks
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task NestedIf_AllPathsReachable() {
        // Arrange: nested if statements
        var innerIf = new IfStatement(
            new Variable("y"),
            Wrap(1),
            Wrap(2)
        );

        var ast = new IfStatement(
            new Variable("x"),
            innerIf,
            Wrap(3)
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // No dead code warnings should be present
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DoWhileLoop_BodyExecutesOnce() {
        // Arrange: do-while loop
        var body = Wrap(1);
        var condition = new Variable("x");

        var ast = new DoWhileLoop(body, condition);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have: entry→body, condition, exit blocks
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task UnreachableBlocks_AreDetected() {
        // Arrange: code after return in an if branch
        var ast = new Block(
            new IfStatement(
                Wrap(true),
                new Block(
                    new ReturnStatement(Wrap(1)),
                    Wrap(99) // Dead code
                ),
                Wrap(2)
            ),
            Wrap(3)
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        var unreachableBlocks = cfg!.UnreachableBlocks.ToList();
        // There should be unreachable code (the 99)
        await Assert.That(result.Diagnostics.Where(d => d.Code == "CF0002").Count()).IsGreaterThan(0);
    }

    [Test]
    public async Task ContinueStatement_JumpsToLoopCondition() {
        // Arrange: while loop with continue
        var condition = new Variable("x");
        var body = new Block(
            new ContinueStatement(),
            Wrap(2) // Dead code after continue
        );

        var ast = new WhileLoop(condition, body);

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after continue
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ThrowStatement_TerminatesBlock() {
        // Arrange: block with throw followed by code
        var ast = new Block(
            Wrap(1),
            new ThrowStatement(new Variable("ex")),
            Wrap(3) // Dead code
        );

        var analyzer = new AnalyzerBuilder()
            .UseControlFlowAnalysis()
            .Build();

        // Act
        var result = analyzer.Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after throw
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }
}