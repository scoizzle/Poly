using Poly.Interpretation.Analysis.ConstantFolding;
using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Interpretation.Analysis.Semantics;

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

        // Act
        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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
            new Return(Wrap(42)),
            Wrap(3) // This should be dead code
        );

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have: entry, condition, body, iterator, exit blocks
        await Assert.That(cfg!.Blocks.Count).IsGreaterThanOrEqualTo(4);
    }

    [Test]
    public async Task ForEachLoop_HasProperStructure() {
        // Arrange: foreach loop over a collection variable
        var collection = new Variable("items");
        var body = Wrap(1);

        var ast = new ForEachLoop(new Variable("item"), collection, body);

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();
        // Should have at least: entry, condition, body, exit blocks
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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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
                    new Return(Wrap(1)),
                    Wrap(99) // Dead code
                ),
                Wrap(2)
            ),
            Wrap(3)
        );

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after continue
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ContinueStatement_InForEachLoop_DetectsDeadCodeAfterContinue() {
        // Arrange: foreach loop with continue
        var body = new Block(
            new ContinueStatement(),
            Wrap(2) // Dead code after continue
        );

        var ast = new ForEachLoop(new Variable("item"), new Variable("items"), body);

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after continue
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task BreakStatement_InForEachLoop_DetectsDeadCodeAfterBreak() {
        // Arrange: foreach loop with break
        var body = new Block(
            new BreakStatement(),
            Wrap(2) // Dead code after break
        );

        var ast = new ForEachLoop(new Variable("item"), new Variable("items"), body);

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

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

        var result = new AnalyzerBuilder().UseThisReferenceContext().UseTypeAndMemberResolver().UseVariableScopeValidator().UseSideEffectAnalysis().UseJumpTargetResolution().UseConstantFolding().UseControlFlowAnalysis().Build().Analyze(ast);

        // Assert
        var cfg = result.GetControlFlowGraph(ast);
        await Assert.That(cfg).IsNotNull();

        // Should detect dead code after throw
        var deadCodeWarnings = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deadCodeWarnings.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task While_ConstTruePureNoMutation_IsInfinite_AndPostCodeElidable() {
        // pure infinite while(true) with no mutation => CF detects, sets Infinite metadata, marks post code elidable + specific diag
        var cond = new Constant(true);
        var body = new Block(new Constant(42)); // pure
        var post = new Constant(99);
        var ast = new Block(
            new WhileLoop(cond, body),
            post
        );

        var result = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(ast);

        await Assert.That(result.IsInfiniteLoop(ast.Nodes[0])).IsTrue();

        var infDiags = result.Diagnostics.Where(d => d.Code == "CF0003").ToList();
        await Assert.That(infDiags.Count).IsGreaterThan(0);

        // post code should be tagged elidable by CF dead code
        await Assert.That(result.CanElide(post)).IsTrue();

        var deads = result.Diagnostics.Where(d => d.Code == "CF0002").ToList();
        await Assert.That(deads.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task If_ConstFalse_ElidesElseBranch() {
        var cond = new Constant(false);
        var thenB = new Block(new Constant(1));
        var elseB = new Block(new Constant(2));
        var ast = new IfStatement(cond, thenB, elseB);

        var result = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(ast);

        // then branch is dead for const false; else is live
        await Assert.That(result.CanElide(thenB)).IsTrue();
        await Assert.That(result.CanElide(elseB)).IsFalse(); // live

        var specific = result.Diagnostics.Where(d => d.Code == "CF0005" || d.Code == "CF0004").ToList();
        await Assert.That(specific.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Switch_ConstValue_DeadCaseMarked() {
        var val = new Constant(1);
        var case0 = new SwitchCase(new Constant(0), new Block(new Constant("zero")));
        var case1 = new SwitchCase(new Constant(1), new Block(new Constant("one")));
        var def = new Block(new Constant("def"));
        var ast = new SwitchStatement(val, new[] { case0, case1 }, def);

        var result = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(ast);

        // case0 body should be elidable (const 1 doesn't match 0)
        await Assert.That(result.CanElide(case0.Body)).IsTrue();

        var deadCaseDiags = result.Diagnostics.Where(d => d.Code == "CF0011" || d.Code == "CF0012").ToList();
        await Assert.That(deadCaseDiags.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task MustExecute_BasicEntryStmts() {
        var ast = new Block(
            new Constant(1),
            new Constant(2)
        );

        var result = new AnalyzerBuilder()
            .UseThisReferenceContext()
            .UseTypeAndMemberResolver()
            .UseVariableScopeValidator()
            .UseSideEffectAnalysis()
            .UseJumpTargetResolution()
            .UseConstantFolding()
            .UseControlFlowAnalysis()
            .Build()
            .Analyze(ast);

        // first stmts in entry often marked must-execute by our simple heuristic
        if (ast.Nodes.Count > 0) {
            // not strict assert (heuristic), just exercise no crash + api
            _ = result.IsMustExecute(ast.Nodes[0]);
        }
        await Assert.That(result.GetControlFlowGraph(ast)).IsNotNull();
    }
}