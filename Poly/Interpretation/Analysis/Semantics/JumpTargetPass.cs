using Poly.Analysis;
using Poly.Ast.Nodes;

namespace Poly.Interpretation.Analysis.Semantics;

// ── Metadata types ──────────────────────────────────────────────

/// <summary>
/// Metadata stamped on a jump statement (<see cref="BreakStatement"/>,
/// <see cref="ContinueStatement"/>, or <see cref="GotoStatement"/>)
/// indicating which node it targets.
///
/// For break/continue, <see cref="TargetNodeId"/> is the enclosing loop's
/// <see cref="NodeId"/>.  For goto, it is the target
/// <see cref="LabelDeclaration"/>'s <see cref="NodeId"/>.
///
/// The jump kind is determined by the node's runtime type —
/// <see cref="ResolvedJumpTarget"/> only stores the target identity.
///
/// Set by <see cref="JumpTargetAnalyzer"/>.
/// </summary>
/// <param name="TargetNodeId">The target loop or label declaration node.</param>
public sealed record ResolvedJumpTarget(NodeId TargetNodeId) : IAnalysisMetadata;

// ── Analyzer ────────────────────────────────────────────────────

/// <summary>
/// Resolves jump targets for break, continue, and goto statements.
///
/// For break/continue: determines which enclosing loop the statement targets
/// (innermost for unlabeled; matching-label for labeled).
/// For goto: resolves the target label declaration within the same scope.
///
/// Stamps <see cref="ResolvedJumpTarget"/> metadata on the jump statement.
///
/// Reports diagnostics for:
///   - break/continue outside an enclosing loop
///   - goto targeting an undefined label
///   - labeled break/continue with no matching enclosing loop
/// </summary>
internal sealed class JumpTargetAnalyzer : INodeAnalyzer {
    public const string Id = "JumpTarget";
    public string PassName => Id;
    public string[] Dependencies => [];
    public void Analyze(AnalysisContext context, Node node) {
        // Walk the tree from any scope boundary — TypeDefinitionNode members,
        // MethodDefinitionNode bodies, Lambda bodies, or the script root.
        switch (node) {
            case TypeDefinitionNode typeDef:
                WalkTypeDefinition(context, typeDef);
                return;

            case MethodDefinitionNode method when method.Body is not null:
                ResolveScope(context, method.Body);
                return;

            case Lambda:
                // Lambda bodies are isolated scopes — jump statements
                // inside a lambda cannot cross the lambda boundary to
                // loops in the enclosing method.
                return;

            default:
                // Script-level code (Block at root, etc.) — treat the
                // entire tree as a single function scope.
                ResolveScope(context, node);
                break;
        }
    }

    private static void WalkTypeDefinition(AnalysisContext context, TypeDefinitionNode typeDef) {
        if (typeDef.Constructors is not null) {
            foreach (var ctor in typeDef.Constructors) {
                if (ctor.Body is not null)
                    ResolveScope(context, ctor.Body);
            }
        }

        if (typeDef.Methods is not null) {
            foreach (var method in typeDef.Methods) {
                if (method.Body is not null)
                    ResolveScope(context, method.Body);
            }
        }

        if (typeDef.Properties is not null) {
            foreach (var prop in typeDef.Properties) {
                if (prop.Getter?.Body is not null)
                    ResolveScope(context, prop.Getter.Body);
                if (prop.Setter?.Body is not null)
                    ResolveScope(context, prop.Setter.Body);
                if (prop.Initializer?.Value is not null)
                    ResolveScope(context, prop.Initializer.Value);
            }
        }
    }

    /// <summary>
    /// Resolves all jumps within a single function scope.  Each scope gets
    /// its own label dictionary and loop stack — labels defined in one
    /// function are invisible to gotos in another.
    /// </summary>
    private static void ResolveScope(AnalysisContext context, Node body) {
        var labels = new Dictionary<string, NodeId>(StringComparer.Ordinal);
        CollectLabels(body, labels);
        ResolveJumps(context, body, labels, new Stack<(NodeId Id, string? Label)>());
    }

    /// <summary>
    /// Recursively collects all <see cref="LabelDeclaration"/> nodes within
    /// the subtree, keyed by name.  Does not cross lambda boundaries.
    /// </summary>
    private static void CollectLabels(Node node, Dictionary<string, NodeId> labels) {
        if (node is LabelDeclaration label) {
            labels.TryAdd(label.Name, label.Id);
            // Fall through to recurse into the labeled statement's children,
            // since labels inside it are in the same function scope.
        }

        // Do not cross lambda boundaries — lambdas are isolated scopes.
        if (node is Lambda)
            return;

        foreach (var child in node.Children) {
            if (child is not null)
                CollectLabels(child!, labels);
        }
    }

    /// <summary>
    /// Walks the AST within a single scope, maintaining a loop stack
    /// and stamping metadata on break/continue/goto statements.
    /// </summary>
    private static void ResolveJumps(
        AnalysisContext context,
        Node node,
        Dictionary<string, NodeId> labels,
        Stack<(NodeId Id, string? Label)> loops) {
        // Do not cross lambda boundaries.
        if (node is Lambda)
            return;

        switch (node) {
            case WhileLoop w:
                loops.Push((w.Id, w.Label));
                ResolveJumps(context, w.Condition, labels, loops);
                ResolveJumps(context, w.Body, labels, loops);
                loops.Pop();
                break;

            case ForLoop f:
                loops.Push((f.Id, null));
                if (f.Initializer is not null)
                    ResolveJumps(context, f.Initializer, labels, loops);
                if (f.Condition is not null)
                    ResolveJumps(context, f.Condition, labels, loops);
                ResolveJumps(context, f.Body, labels, loops);
                if (f.Increment is not null)
                    ResolveJumps(context, f.Increment, labels, loops);
                loops.Pop();
                break;

            case DoWhileLoop d:
                loops.Push((d.Id, null));
                ResolveJumps(context, d.Body, labels, loops);
                ResolveJumps(context, d.Condition, labels, loops);
                loops.Pop();
                break;

            case ForEachLoop fe:
                loops.Push((fe.Id, null));
                ResolveJumps(context, fe.Collection, labels, loops);
                ResolveJumps(context, fe.Body, labels, loops);
                loops.Pop();
                break;

            case LabelDeclaration ld:
                // Labels don't create a new scope — just recurse into the
                // labeled statement with the same loop stack.
                ResolveJumps(context, ld.Statement, labels, loops);
                break;

            case BreakStatement br:
                ResolveBreak(context, br, loops);
                break;

            case ContinueStatement ct:
                ResolveContinue(context, ct, loops);
                break;

            case GotoStatement g:
                ResolveGoto(context, g, labels);
                break;

            default:
                foreach (var child in node.Children) {
                    if (child is not null)
                        ResolveJumps(context, child!, labels, loops);
                }
                break;
        }
    }

    private static void ResolveBreak(
        AnalysisContext context,
        BreakStatement br,
        Stack<(NodeId Id, string? Label)> loops) {
        if (br.Label is not null) {
            // Named break — search the loop stack for a matching label.
            foreach (var (id, label) in loops) {
                if (string.Equals(label, br.Label, StringComparison.Ordinal)) {
                    context.SetMetadata(br, new ResolvedJumpTarget(id));
                    return;
                }
            }
            context.ReportDiagnostic(br, DiagnosticSeverity.Error,
                $"No enclosing loop with label '{br.Label}' found for break statement.", "JT0001");
        }
        else if (loops.Count > 0) {
            context.SetMetadata(br, new ResolvedJumpTarget(loops.Peek().Id));
        }
        else {
            context.ReportDiagnostic(br, DiagnosticSeverity.Error,
                "break statement outside an enclosing loop.", "JT0002");
        }
    }

    private static void ResolveContinue(
        AnalysisContext context,
        ContinueStatement ct,
        Stack<(NodeId Id, string? Label)> loops) {
        if (ct.Label is not null) {
            // Named continue — search the loop stack for a matching label.
            foreach (var (id, label) in loops) {
                if (string.Equals(label, ct.Label, StringComparison.Ordinal)) {
                    context.SetMetadata(ct, new ResolvedJumpTarget(id));
                    return;
                }
            }
            context.ReportDiagnostic(ct, DiagnosticSeverity.Error,
                $"No enclosing loop with label '{ct.Label}' found for continue statement.", "JT0003");
        }
        else if (loops.Count > 0) {
            context.SetMetadata(ct, new ResolvedJumpTarget(loops.Peek().Id));
        }
        else {
            context.ReportDiagnostic(ct, DiagnosticSeverity.Error,
                "continue statement outside an enclosing loop.", "JT0004");
        }
    }

    private static void ResolveGoto(
        AnalysisContext context,
        GotoStatement g,
        Dictionary<string, NodeId> labels) {
        if (labels.TryGetValue(g.Target, out var targetId)) {
            context.SetMetadata(g, new ResolvedJumpTarget(targetId));
        }
        else {
            context.ReportDiagnostic(g, DiagnosticSeverity.Error,
                $"Goto target label '{g.Target}' not found.", "JT0005");
        }
    }
}

// ── Extension method ────────────────────────────────────────────

public static class JumpTargetResolutionExtensions {
    extension(AnalyzerBuilder builder) {
        /// <summary>
        /// Adds the <see cref="JumpTargetAnalyzer"/> to the analysis pipeline,
        /// resolving break/continue targets to their enclosing loop and goto
        /// targets to their matching label declaration.
        ///
        /// <para>Stamps <see cref="ResolvedJumpTarget"/> metadata on the corresponding nodes.</para>
        /// </summary>
        public AnalyzerBuilder UseJumpTargetResolution() {
            builder.AddAnalyzer(new JumpTargetAnalyzer());
            return builder;
        }
    }
}