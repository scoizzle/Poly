using Poly.Syntax.DomainModeling;

using static Poly.Syntax.AbstractSyntaxTree.NodeExtensions;

using InterpAnd = Poly.Syntax.AbstractSyntaxTree.Boolean.And;
using InterpEqual = Poly.Syntax.AbstractSyntaxTree.Equality.Equal;
using InterpGreaterThanOrEqual = Poly.Syntax.AbstractSyntaxTree.Comparison.GreaterThanOrEqual;
using InterpLessThanOrEqual = Poly.Syntax.AbstractSyntaxTree.Comparison.LessThanOrEqual;
using InterpMember = Poly.Syntax.AbstractSyntaxTree.MemberAccess;
using InterpNotEqual = Poly.Syntax.AbstractSyntaxTree.Equality.NotEqual;
using InterpOr = Poly.Syntax.AbstractSyntaxTree.Boolean.Or;

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
        return expression switch {
            Literal literal => Wrap(literal.Value),
            Member member => new InterpMember(LowerCore(member.Target, subjectRoot), member.Name),
            And and => new InterpAnd(LowerCore(and.Left, subjectRoot), LowerCore(and.Right, subjectRoot)),
            Or or => new InterpOr(LowerCore(or.Left, subjectRoot), LowerCore(or.Right, subjectRoot)),
            Equal equal => new InterpEqual(LowerCore(equal.Left, subjectRoot), LowerCore(equal.Right, subjectRoot)),
            NotEqual notEqual => new InterpNotEqual(LowerCore(notEqual.Left, subjectRoot), LowerCore(notEqual.Right, subjectRoot)),
            GreaterThanOrEqual greaterThanOrEqual => new InterpGreaterThanOrEqual(LowerCore(greaterThanOrEqual.Left, subjectRoot), LowerCore(greaterThanOrEqual.Right, subjectRoot)),
            LessThanOrEqual lessThanOrEqual => new InterpLessThanOrEqual(LowerCore(lessThanOrEqual.Left, subjectRoot), LowerCore(lessThanOrEqual.Right, subjectRoot)),
            _ => expression
        };
    }
}