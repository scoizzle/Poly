using Poly.Syntax.AbstractSyntaxTree;

using InterpAnd = Poly.Syntax.AbstractSyntaxTree.And;
using InterpEqual = Poly.Syntax.AbstractSyntaxTree.Equal;
using InterpGreaterThanOrEqual = Poly.Syntax.AbstractSyntaxTree.GreaterThanOrEqual;
using InterpLessThanOrEqual = Poly.Syntax.AbstractSyntaxTree.LessThanOrEqual;
using InterpNotEqual = Poly.Syntax.AbstractSyntaxTree.NotEqual;
using InterpOr = Poly.Syntax.AbstractSyntaxTree.Or;

namespace Poly.Data.Modeling;

/// <summary>
/// Lowers Domain Modeling syntax clauses into executable interpretation AST nodes.
/// The lowering process is contextualized by analysis results and a root subject node.
/// </summary>
public sealed class DomainLoweringGenerator {
    private readonly AnalysisResult _analysis;

    public DomainLoweringGenerator(AnalysisResult analysis) {
        ArgumentNullException.ThrowIfNull(analysis);
        _analysis = analysis;
    }

    public Node Lower(Node root, Node subjectRoot) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(subjectRoot);

        var lowered = LowerCore(root, subjectRoot);

        // Honor replacement metadata produced by analyzers for the lowered output.
        var replacement = _analysis.GetNodeReplacement(lowered);
        return replacement ?? lowered;
    }

    private static Node LowerCore(Node expression, Node subjectRoot) {
        _ = subjectRoot;

        return expression switch {
            InterpAnd and => new InterpAnd(LowerCore(and.LeftHandValue, subjectRoot), LowerCore(and.RightHandValue, subjectRoot)),
            InterpOr or => new InterpOr(LowerCore(or.LeftHandValue, subjectRoot), LowerCore(or.RightHandValue, subjectRoot)),
            InterpEqual equal => new InterpEqual(LowerCore(equal.LeftHandValue, subjectRoot), LowerCore(equal.RightHandValue, subjectRoot)),
            InterpNotEqual notEqual => new InterpNotEqual(LowerCore(notEqual.LeftHandValue, subjectRoot), LowerCore(notEqual.RightHandValue, subjectRoot)),
            InterpGreaterThanOrEqual greaterThanOrEqual => new InterpGreaterThanOrEqual(LowerCore(greaterThanOrEqual.LeftHandValue, subjectRoot), LowerCore(greaterThanOrEqual.RightHandValue, subjectRoot)),
            InterpLessThanOrEqual lessThanOrEqual => new InterpLessThanOrEqual(LowerCore(lessThanOrEqual.LeftHandValue, subjectRoot), LowerCore(lessThanOrEqual.RightHandValue, subjectRoot)),
            Member memberAccess => new Member(LowerCore(memberAccess.Value, subjectRoot), memberAccess.MemberName),
            Constant constant => new Constant(constant.Value),
            _ => expression
        };
    }
}