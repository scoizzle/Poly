using Poly.Interpretation.Analysis.ControlFlow;
using Poly.Syntax.Analysis;
using Poly.Syntax.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

internal sealed class DefiniteAssignmentAnalyzer : INodeAnalyzer {
    public const string Id = "DefiniteAssignment";
    public string PassName => Id;
    public string[] Dependencies => [ControlFlowAnalysisPass.Id];
    public void Analyze(AnalysisContext context, Node node) {
        if (!context.TryBeginAnalyzerVisit<DefiniteAssignmentAnalyzer>(node))
            return;

        var scopeStack = new Stack<HashSet<string>>();
        var assigned = new HashSet<string>();
        AnalyzeImpl(context, node, scopeStack, ref assigned);
    }

    private static void AnalyzeImpl(AnalysisContext context, Node node,
        Stack<HashSet<string>> scopeStack, ref HashSet<string> assigned) {

        switch (node) {
            case Lambda lambda:
                scopeStack.Push(assigned);
                assigned = [.. assigned];
                AnalyzeChildrenImpl(context, node, scopeStack, ref assigned);
                if (lambda.Body is not null)
                    context.SetMetadata(lambda.Body, new DefiniteAssignmentMetadata([.. assigned]));
                assigned = scopeStack.Pop();
                return;

            case Block block:
                AnalyzeBlock(context, block, scopeStack, ref assigned);
                return;

            case Assignment { Destination: Variable v } when v.Name is not null:
                assigned.Add(v.Name);
                AnalyzeChildrenImpl(context, node, scopeStack, ref assigned);
                return;

            case IfStatement ifStmt:
                AnalyzeIf(context, ifStmt, scopeStack, ref assigned);
                return;

            case WhileLoop wl:
                var before = new HashSet<string>(assigned);
                AnalyzeChildrenImpl(context, wl.Condition, scopeStack, ref assigned);
                if (wl.Body is not null) AnalyzeChildrenImpl(context, wl.Body, scopeStack, ref assigned);
                assigned = before;
                return;

            case ForLoop fl:
                var beforeFor = new HashSet<string>(assigned);
                AnalyzeChildrenImpl(context, fl, scopeStack, ref assigned);
                assigned = beforeFor;
                return;

            case TryCatchFinally tcf:
                AnalyzeTry(context, tcf, scopeStack, ref assigned);
                return;

            case DoWhileLoop dwl:
                var beforeDo = new HashSet<string>(assigned);
                AnalyzeChildrenImpl(context, dwl.Body, scopeStack, ref assigned);
                AnalyzeChildrenImpl(context, dwl.Condition, scopeStack, ref assigned);
                assigned = beforeDo;
                return;

            case ForEachLoop fel:
                AnalyzeChildrenImpl(context, fel.Collection, scopeStack, ref assigned);
                if (fel.LoopVariable.Name is not null)
                    assigned.Add(fel.LoopVariable.Name);
                AnalyzeChildrenImpl(context, fel.Body, scopeStack, ref assigned);
                return;

            case SwitchStatement swt:
                var beforeSwitch = new HashSet<string>(assigned);
                HashSet<string>? merged = null;
                foreach (var sc in swt.Cases) {
                    assigned = [.. beforeSwitch];
                    AnalyzeChildrenImpl(context, sc.Body, scopeStack, ref assigned);
                    if (merged is null)
                        merged = [.. assigned];
                    else
                        merged.IntersectWith(assigned);
                }
                if (swt.DefaultCase is not null) {
                    assigned = [.. beforeSwitch];
                    AnalyzeChildrenImpl(context, swt.DefaultCase, scopeStack, ref assigned);
                    if (merged is null)
                        merged = [.. assigned];
                    else
                        merged.IntersectWith(assigned);
                }
                assigned = merged ?? [.. beforeSwitch];
                return;

            case UsingStatement us:
                AnalyzeChildrenImpl(context, us, scopeStack, ref assigned);
                return;

            case BreakStatement or ContinueStatement or GotoStatement or Return or ThrowStatement:
                return;

            default:
                AnalyzeChildrenImpl(context, node, scopeStack, ref assigned);
                return;
        }
    }

    private static void AnalyzeBlock(AnalysisContext context, Block block,
        Stack<HashSet<string>> scopeStack, ref HashSet<string> assigned) {
        for (int i = 0; i < block.Nodes.Count; i++)
            AnalyzeImpl(context, block.Nodes[i], scopeStack, ref assigned);
    }

    private static void AnalyzeIf(AnalysisContext context, IfStatement ifStmt,
        Stack<HashSet<string>> scopeStack, ref HashSet<string> assigned) {
        var before = new HashSet<string>(assigned);
        AnalyzeImpl(context, ifStmt.Condition, scopeStack, ref assigned);

        assigned = [.. before];
        if (ifStmt.ThenBranch is not null) AnalyzeImpl(context, ifStmt.ThenBranch, scopeStack, ref assigned);
        var afterThen = new HashSet<string>(assigned);

        assigned = [.. before];
        if (ifStmt.ElseBranch is not null) AnalyzeImpl(context, ifStmt.ElseBranch, scopeStack, ref assigned);

        assigned.IntersectWith(afterThen);
    }

    private static void AnalyzeTry(AnalysisContext context, TryCatchFinally tcf,
        Stack<HashSet<string>> scopeStack, ref HashSet<string> assigned) {
        var before = new HashSet<string>(assigned);

        assigned = [.. before];
        AnalyzeImpl(context, tcf.TryBlock, scopeStack, ref assigned);
        var afterTry = new HashSet<string>(assigned);

        assigned = [.. before];
        if (tcf.CatchClauses is not null)
            foreach (var cc in tcf.CatchClauses)
                AnalyzeImpl(context, cc.Body, scopeStack, ref assigned);
        var afterCatch = new HashSet<string>(assigned);

        assigned.IntersectWith(afterTry);
        assigned.IntersectWith(afterCatch);

        if (tcf.FinallyBlock is not null)
            AnalyzeImpl(context, tcf.FinallyBlock, scopeStack, ref assigned);
    }

    private static void AnalyzeChildrenImpl(AnalysisContext context, Node node,
        Stack<HashSet<string>> scopeStack, ref HashSet<string> assigned) {
        foreach (var child in node.Children) {
            if (child is null || !context.ShouldAnalyze(child))
                continue;
            AnalyzeImpl(context, child!, scopeStack, ref assigned);
        }
    }
}

/// <summary>Metadata recording the set of variables that are definitely
/// assigned at a given program point. Used by the VM to elide redundant
/// initialization checks and by the emitter to skip zero-init of proven
/// assigned locals.</summary>
/// <param name="DefinitelyAssigned">Set of variable names that have been
/// assigned on all paths reaching this point.</param>
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