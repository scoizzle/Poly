namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

/// <summary>
/// Walks the AST top-down, assigning unique integer label IDs to every
/// control-flow structure.  Stores the result as per-node metadata consumed
/// by <see cref="UopGenerationPass"/> and the lowering assembler.
/// </summary>
internal sealed class LabelAssignmentPass : INodeAnalyzer {
    private int _nextLabel;
    private readonly Stack<LoopScope> _loopStack = new();

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<LabelAssignmentPass>(node))
            return;

        switch (node) {
            case WhileLoop wl:
                AssignWhileLoopLabels(context, wl);
                break;
            case DoWhileLoop dwl:
                AssignDoWhileLoopLabels(context, dwl);
                break;
            case ForLoop fl:
                AssignForLoopLabels(context, fl);
                break;
            case IfStatement ifs:
                AssignIfLabels(context, ifs);
                break;
            case Conditional cond:
                AssignConditionalLabels(context, cond);
                break;
            case BreakStatement:
                AssignBreakLabel(context, node);
                return;
            case ContinueStatement:
                AssignContinueLabel(context, node);
                return;
        }

        this.AnalyzeChildren(context, node);
    }

    // ── WhileLoop ─────────────────────────────────────────────────

    private void AssignWhileLoopLabels(AnalysisContext context, WhileLoop wl) {
        int cont = Alloc();
        int end = Alloc();
        context.SetMetadata(wl, new WhileLoopLabelMetadata(cont, end));

        _loopStack.Push(new LoopScope(cont, end));
        this.AnalyzeChildren(context, wl);
        _loopStack.Pop();
    }

    // ── DoWhileLoop ───────────────────────────────────────────────

    private void AssignDoWhileLoopLabels(AnalysisContext context, DoWhileLoop dwl) {
        int cont = Alloc();
        int end = Alloc();
        context.SetMetadata(dwl, new DoWhileLoopLabelMetadata(cont, end));

        _loopStack.Push(new LoopScope(cont, end));
        this.AnalyzeChildren(context, dwl);
        _loopStack.Pop();
    }

    // ── ForLoop ───────────────────────────────────────────────────

    private void AssignForLoopLabels(AnalysisContext context, ForLoop fl) {
        int cond = Alloc();
        int end = Alloc();
        context.SetMetadata(fl, new ForLoopLabelMetadata(cond, end));

        _loopStack.Push(new LoopScope(cond, end));
        this.AnalyzeChildren(context, fl);
        _loopStack.Pop();
    }

    // ── IfStatement ───────────────────────────────────────────────

    private void AssignIfLabels(AnalysisContext context, IfStatement ifs) {
        int? elseLabel = ifs.ElseBranch is not null ? Alloc() : null;
        int end = Alloc();
        context.SetMetadata(ifs, new IfLabelMetadata(elseLabel, end));

        // No loop scope — IfStatement doesn't create one.
        this.AnalyzeChildren(context, ifs);
    }

    // ── Conditional ───────────────────────────────────────────────

    private void AssignConditionalLabels(AnalysisContext context, Conditional cond) {
        int falseLabel = Alloc();
        int end = Alloc();
        context.SetMetadata(cond, new ConditionalLabelMetadata(falseLabel, end));

        this.AnalyzeChildren(context, cond);
    }

    // ── Break / Continue ──────────────────────────────────────────

    private void AssignBreakLabel(AnalysisContext context, Node node) {
        if (_loopStack.Count == 0)
            return;
        context.SetMetadata(node, new BreakTargetMetadata(_loopStack.Peek().EndLabel));
    }

    private void AssignContinueLabel(AnalysisContext context, Node node) {
        if (_loopStack.Count == 0)
            return;
        context.SetMetadata(node, new ContinueTargetMetadata(_loopStack.Peek().ContLabel));
    }

    // ── Helpers ───────────────────────────────────────────────────

    private int Alloc() => _nextLabel++;
}

// ── Metadata for Break / Continue targets ──────────────────────────

/// <summary>Resolved label target for a BreakStatement.</summary>
/// <param name="TargetLabel">The EndLabel of the nearest enclosing loop.</param>
public sealed record BreakTargetMetadata(int TargetLabel) : IAnalysisMetadata;

/// <summary>Resolved label target for a ContinueStatement.</summary>
/// <param name="TargetLabel">The ContLabel of the nearest enclosing loop.</param>
public sealed record ContinueTargetMetadata(int TargetLabel) : IAnalysisMetadata;

// ── AnalyzerBuilder extension ──────────────────────────────────────

public static class LabelAssignmentExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseLabelAssignment() {
            builder.AddAnalyzer(new LabelAssignmentPass());
            return builder;
        }
    }
}