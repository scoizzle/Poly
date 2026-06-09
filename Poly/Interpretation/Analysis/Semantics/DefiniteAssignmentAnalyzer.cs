using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class DefiniteAssignmentAnalyzer : INodeAnalyzer {
    private readonly Stack<HashSet<string>> _scopeStack = new();
    private HashSet<string> _assigned = [];

    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<DefiniteAssignmentAnalyzer>(node))
            return;

        switch (node) {
            case Lambda lambda:
                _scopeStack.Push(_assigned);
                _assigned = [.. _assigned]; // copy parent scope (captured vars)
                this.AnalyzeChildren(context, node);
                // Store metadata on the lambda body for the lowering to query
                if (lambda.Body is not null)
                    context.SetMetadata(lambda.Body, new DefiniteAssignmentMetadata([.. _assigned]));
                _assigned = _scopeStack.Pop();
                return;

            case Block block:
                AnalyzeBlock(context, block);
                return; // children already processed in AnalyzeBlock

            case Assignment { Destination: Variable v } when v.Name is not null:
                _assigned.Add(v.Name);
                this.AnalyzeChildren(context, node);
                return;

            case IfStatement ifStmt:
                AnalyzeIf(context, ifStmt);
                return;

            case WhileLoop wl:
                var before = new HashSet<string>(_assigned);
                this.AnalyzeChildren(context, wl.Condition);
                if (wl.Body is not null) this.AnalyzeChildren(context, wl.Body);
                _assigned = before;
                return;

            case ForLoop fl:
                var beforeFor = new HashSet<string>(_assigned);
                this.AnalyzeChildren(context, fl);
                _assigned = beforeFor;
                return;

            case TryCatchFinally tcf:
                AnalyzeTry(context, tcf);
                return;

            case BreakStatement or ContinueStatement or GotoStatement or Return or ThrowStatement:
                return; // no children, and they don't assign

            default:
                this.AnalyzeChildren(context, node);
                return;
        }
    }

    private void AnalyzeBlock(AnalysisContext context, Block block) {
        for (int i = 0; i < block.Nodes.Count; i++)
            Analyze(context, block.Nodes[i]);
    }

    private void AnalyzeIf(AnalysisContext context, IfStatement ifStmt) {
        var before = new HashSet<string>(_assigned);
        Analyze(context, ifStmt.Condition);

        // Then branch
        _assigned = [.. before];
        if (ifStmt.ThenBranch is not null) Analyze(context, ifStmt.ThenBranch);
        var afterThen = new HashSet<string>(_assigned);

        // Else branch (or no else — use 'before' as the else path)
        _assigned = [.. before];
        if (ifStmt.ElseBranch is not null) Analyze(context, ifStmt.ElseBranch);

        // Intersection: variable is definitely assigned only if assigned in both paths
        _assigned.IntersectWith(afterThen);
    }

    private void AnalyzeTry(AnalysisContext context, TryCatchFinally tcf) {
        var before = new HashSet<string>(_assigned);

        _assigned = [.. before];
        Analyze(context, tcf.TryBlock);
        var afterTry = new HashSet<string>(_assigned);

        _assigned = [.. before];
        if (tcf.CatchClauses is not null)
            foreach (var cc in tcf.CatchClauses)
                Analyze(context, cc.Body);
        var afterCatch = new HashSet<string>(_assigned);

        _assigned.IntersectWith(afterTry);
        _assigned.IntersectWith(afterCatch);

        if (tcf.FinallyBlock is not null)
            Analyze(context, tcf.FinallyBlock);
    }
}

public sealed record DefiniteAssignmentMetadata(HashSet<string> DefinitelyAssigned) : IAnalysisMetadata;

public static class DefiniteAssignmentExtensions {
    extension(AnalyzerBuilder builder) {
        public AnalyzerBuilder UseDefiniteAssignmentAnalysis() {
            builder.AddAnalyzer(new DefiniteAssignmentAnalyzer());
            return builder;
        }
    }

    extension(INodeMetadataProvider provider) {
        public bool IsDefinitelyAssigned(Node node, string variableName) {
            return provider.GetMetadata<DefiniteAssignmentMetadata>(node)
                ?.DefinitelyAssigned.Contains(variableName) ?? false;
        }
    }
}